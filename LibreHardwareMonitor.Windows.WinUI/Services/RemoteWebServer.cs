// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

public sealed class RemoteWebServer : IRemoteWebServer
{
    private readonly IComputer _computer;
    private readonly object _sensorReadLock;
    private Func<SensorTreeItemViewModel?> _rootProvider = () => null;
    private readonly Version _version = typeof(RemoteWebServer).Assembly.GetName().Version ?? new Version(0, 0);
    private CancellationTokenSource? _cts;
    private HttpListener? _listener;
    private Task? _listenerTask;
    private bool _quit;

    public RemoteWebServer(
        IComputer computer,
        object sensorReadLock,
        string listenerIp,
        int listenerPort,
        bool authEnabled,
        string userName,
        string passwordHash)
    {
        _computer = computer;
        _sensorReadLock = sensorReadLock;
        ListenerIp = listenerIp;
        ListenerPort = listenerPort;
        AuthEnabled = authEnabled;
        UserName = userName;
        PasswordHash = passwordHash;

        try
        {
            _listener = new HttpListener { IgnoreWriteExceptions = true };
        }
        catch (PlatformNotSupportedException)
        {
            _listener = null;
        }
    }

    /// <summary>
    /// Sets the accessor used to read the current sensor tree root. Supplied after construction so the
    /// server can be created by the container without a constructor dependency on the view model.
    /// </summary>
    public void SetRootProvider(Func<SensorTreeItemViewModel?> rootProvider)
    {
        _rootProvider = rootProvider;
    }

    public bool AuthEnabled { get; set; }

    public bool IsRunning => _listener?.IsListening == true;

    public string ListenerIp { get; set; }

    public int ListenerPort { get; set; }

    public string PasswordHash { get; private set; }

    public bool PlatformNotSupported => _listener == null;

    public string UserName { get; set; }

    public void Dispose()
    {
        Quit();
    }

    public void SetPassword(string plainPassword)
    {
        PasswordHash = PasswordHasher.Hash(plainPassword);
    }

    public bool Start()
    {
        if (PlatformNotSupported || _listener == null)
            return false;

        try
        {
            if (_listener.IsListening)
                return true;

            string listenerIp = ResolveListenerIp();
            string prefix = $"http://{listenerIp}:{ListenerPort}/";

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(prefix);
            _listener.Realm = "Libre Hardware Monitor";
            _listener.AuthenticationSchemes = AuthEnabled ? AuthenticationSchemes.Basic : AuthenticationSchemes.Anonymous;
            _listener.Start();

            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ProcessRequestsAsync(_cts.Token));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Stop()
    {
        if (PlatformNotSupported || _listener == null)
            return false;

        try
        {
            _cts?.Cancel();
            _listenerTask?.Wait(TimeSpan.FromSeconds(5));
            _listener.Stop();
            _cts?.Dispose();
            _cts = null;
            _listenerTask = null;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Quit()
    {
        if (PlatformNotSupported || _quit)
            return;

        _quit = true;
        Stop();
        try
        {
            _listener?.Abort();
        }
        catch
        {
        }
    }

    private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
    {
        if (_listener == null)
            return;

        while (_listener.IsListening && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleContextAsync(context), cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                if (!_listener.IsListening)
                    break;
            }
            catch
            {
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            if (!IsAuthenticated(context))
            {
                context.Response.StatusCode = 401;
                await SendResponseAsync(context.Response, "<HTML><BODY><H4>401 Unauthorized</H4>Authorization required.</BODY></HTML>", "text/html");
                return;
            }

            HttpListenerRequest request = context.Request;
            RemoteWebRoute route = ResolveRoute(request.HttpMethod, request.Url?.AbsolutePath, request.RawUrl);

            switch (route.Kind)
            {
                case RemoteWebRouteKind.Post:
                    await HandlePostRequestAsync(context.Response, request);
                    break;
                case RemoteWebRouteKind.DataJson:
                    await SendJsonAsync(context.Response, request);
                    break;
                case RemoteWebRouteKind.Metrics:
                    await SendPrometheusAsync(context.Response, request);
                    break;
                case RemoteWebRouteKind.Sensor:
                {
                    Dictionary<string, object?> result = [];
                    HandleSensorRequest(request, result);
                    await SendJsonSensorAsync(context.Response, result);
                    break;
                }
                case RemoteWebRouteKind.ResetAllMinMax:
                    _computer.Accept(new SensorVisitor(sensor =>
                    {
                        sensor.ResetMin();
                        sensor.ResetMax();
                    }));
                    await SendJsonAsync(context.Response, request);
                    break;
                case RemoteWebRouteKind.Resource:
                    await ServeResourceFileAsync(context.Response, route.ResourcePath);
                    break;
            }
        }
        catch
        {
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    internal enum RemoteWebRouteKind
    {
        Post,
        DataJson,
        Metrics,
        Sensor,
        ResetAllMinMax,
        Resource
    }

    internal readonly record struct RemoteWebRoute(RemoteWebRouteKind Kind, string ResourcePath);

    // Maps an HTTP method + path to the route that handles it. Endpoints are matched on the path component only
    // (AbsolutePath excludes the query string) and compared exactly. Prefix matching previously let any static asset
    // whose name starts with an endpoint name (e.g. "metrics.html", "sensor-icons.css") be hijacked by the API handlers.
    internal static RemoteWebRoute ResolveRoute(string httpMethod, string? absolutePath, string? rawUrl)
    {
        if (httpMethod == "POST")
            return new RemoteWebRoute(RemoteWebRouteKind.Post, "");

        string requestedFile = (absolutePath ?? rawUrl ?? "").TrimStart('/');
        if (requestedFile.Length == 0)
            requestedFile = "index.html";

        if (requestedFile.Equals("data.json", StringComparison.OrdinalIgnoreCase))
            return new RemoteWebRoute(RemoteWebRouteKind.DataJson, "");

        if (requestedFile.Equals("metrics", StringComparison.OrdinalIgnoreCase))
            return new RemoteWebRoute(RemoteWebRouteKind.Metrics, "");

        if (requestedFile.Equals("Sensor", StringComparison.OrdinalIgnoreCase))
            return new RemoteWebRoute(RemoteWebRouteKind.Sensor, "");

        if (requestedFile.Equals("ResetAllMinMax", StringComparison.OrdinalIgnoreCase))
            return new RemoteWebRoute(RemoteWebRouteKind.ResetAllMinMax, "");

        if (requestedFile.StartsWith("images_icon/", StringComparison.OrdinalIgnoreCase))
            return new RemoteWebRoute(RemoteWebRouteKind.Resource, requestedFile["images_icon/".Length..]);

        return new RemoteWebRoute(RemoteWebRouteKind.Resource, PathForWebResource(requestedFile));
    }

    private bool IsAuthenticated(HttpListenerContext context)
    {
        if (!AuthEnabled)
            return true;

        try
        {
            if (context.User?.Identity is HttpListenerBasicIdentity identity)
                return VerifyCredentials(identity.Name, identity.Password);
        }
        catch
        {
        }

        return false;
    }

    // Validates Basic-auth credentials. Extracted so the comparison can be unit-tested without an HttpListenerContext.
    internal bool VerifyCredentials(string? userName, string? password)
    {
        if (!AuthEnabled)
            return true;

        if (userName == null || password == null)
            return false;

        // Compare the user name and the password hash in constant time, and evaluate both fully (no && short-circuit)
        // so neither a wrong user name nor a wrong password can be distinguished from response timing.
        bool userMatch = CredentialComparer.FixedTimeEquals(userName, UserName);
        bool passwordMatch = PasswordHasher.Verify(password, PasswordHash, out bool isLegacy);
        if (!(userMatch & passwordMatch))
            return false;

        // Transparently upgrade a legacy unsalted-SHA-256 credential to PBKDF2 on first successful authentication. The
        // upgraded hash is persisted by the view model when settings are next saved (e.g. on shutdown).
        if (isLegacy)
            PasswordHash = PasswordHasher.Hash(password);

        return true;
    }

    private async Task HandlePostRequestAsync(HttpListenerResponse response, HttpListenerRequest request)
    {
        Dictionary<string, object?> result = new() { ["result"] = "ok" };
        try
        {
            if (request.Url?.Segments.Length == 2 && request.Url.Segments[1].TrimEnd('/').Equals("Sensor", StringComparison.OrdinalIgnoreCase))
                HandleSensorRequest(request, result);
            else
                throw new ArgumentException("Empty URL, possible values: ['Sensor']");
        }
        catch (Exception ex)
        {
            // Return a generic message; the exception detail (which can reveal internal state) stays server-side.
            result["result"] = "fail";
            result["message"] = "Bad request";
            Debug.WriteLine($"Remote web server POST request failed: {ex}");
        }

        await SendJsonSensorAsync(response, result);
    }

    private void HandleSensorRequest(HttpListenerRequest request, Dictionary<string, object?> result)
    {
        IDictionary<string, string> query = ParseQuery(request.Url?.Query);
        if (!query.TryGetValue("action", out string? action))
            throw new ArgumentNullException(nameof(action), "No action provided");
        if (!query.TryGetValue("id", out string? id))
            throw new ArgumentNullException(nameof(id), "No id provided");

        SensorTreeItemViewModel sensorNode = FindSensor(id)
                                             ?? throw new ArgumentException("Unknown id " + id + " specified");
        ISensor sensor = sensorNode.Sensor!;

        if (action == "ResetMinMax")
        {
            sensor.ResetMin();
            sensor.ResetMax();
            action = "Get";
        }

        switch (action)
        {
            case "Set" when query.TryGetValue("value", out string? value):
                SetSensorControlValue(sensor, value);
                result["result"] = "ok";
                break;
            case "Set":
                throw new ArgumentNullException("value", "No value provided");
            case "Get":
                result["result"] = "ok";
                result["value"] = sensor.Value;
                result["min"] = sensor.Min;
                result["max"] = sensor.Max;
                result["format"] = SensorFormatter.GetFormatString(sensor);
                break;
            default:
                throw new ArgumentException("Unknown action type " + action);
        }
    }

    private SensorTreeItemViewModel? FindSensor(string id)
    {
        return _rootProvider()?.EnumerateSensors()
            .FirstOrDefault(sensorItem => sensorItem.Sensor?.Identifier.ToString() == id);
    }

    private static void SetSensorControlValue(ISensor sensor, string value)
    {
        if (sensor.Control == null)
            throw new ArgumentException("Specified sensor '" + sensor.Identifier + "' can not be set");

        if (value == "null")
            sensor.Control.SetDefault();
        else
            sensor.Control.SetSoftware(float.Parse(value, CultureInfo.InvariantCulture));
    }

    private async Task SendJsonAsync(HttpListenerResponse response, HttpListenerRequest request)
    {
        Dictionary<string, object?> json = new()
        {
            ["id"] = 0,
            ["Version"] = $"{_version.Major}.{_version.Minor}.{_version.Build}",
            ["Text"] = "Sensor",
            ["Min"] = "Min",
            ["Value"] = "Value",
            ["Max"] = "Max",
            ["ImageURL"] = ""
        };

        int nodeIndex = 1;
        SensorTreeItemViewModel? root = _rootProvider();
        json["Children"] = root == null ? Array.Empty<object>() : new List<object> { GenerateJsonForNode(root, ref nodeIndex) };

        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(json, options));
        bool acceptGzip = request.Headers["Accept-Encoding"]?.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0;

        WriteCommonHeaders(response);
        response.ContentType = "application/json";

        if (acceptGzip)
        {
            response.AddHeader("Content-Encoding", "gzip");
            using MemoryStream memory = new();
            using (GZipStream zip = new(memory, CompressionMode.Compress, true))
                await zip.WriteAsync(buffer);
            buffer = memory.ToArray();
        }

        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    internal static Dictionary<string, object?> GenerateJsonForNode(SensorTreeItemViewModel node, ref int nodeIndex)
    {
        Dictionary<string, object?> jsonNode = new()
        {
            ["id"] = nodeIndex++,
            ["Text"] = node.Text,
            ["Min"] = "",
            ["Value"] = "",
            ["Max"] = "",
            ["ImageURL"] = GetImageUrl(node)
        };

        if (node.Sensor != null)
        {
            jsonNode["SensorId"] = node.Sensor.Identifier.ToString();
            jsonNode["Type"] = node.Sensor.SensorType.ToString();
            jsonNode["Min"] = node.Min;
            jsonNode["Value"] = node.Value;
            jsonNode["Max"] = node.Max;
            jsonNode["RawMin"] = node.Sensor.Min;
            jsonNode["RawValue"] = node.Sensor.Value;
            jsonNode["RawMax"] = node.Sensor.Max;
            jsonNode["ImageURL"] = "images/transparent.png";
        }
        else if (node.Hardware != null)
        {
            jsonNode["HardwareId"] = node.Hardware.Identifier.ToString();
        }

        List<object> children = [];
        foreach (SensorTreeItemViewModel child in node.Children)
            children.Add(GenerateJsonForNode(child, ref nodeIndex));
        jsonNode["Children"] = children;

        return jsonNode;
    }

    private async Task SendPrometheusAsync(HttpListenerResponse response, HttpListenerRequest request)
    {
        Dictionary<string, int> settings = ParsePrometheusSettings(ParseQuery(request.Url?.Query));

        // Serialize against the sensor update loop: GeneratePrometheusResponse enumerates each sensor's live Values ring
        // buffer, which the update thread mutates concurrently (an unsynchronized enumeration would throw).
        string content;
        lock (_sensorReadLock)
            content = GeneratePrometheusResponse(_rootProvider(), settings);

        WriteCommonHeaders(response);
        response.AddHeader("X-archivelength", settings["archivelength"].ToString(CultureInfo.InvariantCulture));
        response.AddHeader("X-timestamps", settings["timestamps"].ToString(CultureInfo.InvariantCulture));
        response.AddHeader("X-lastvalue", settings["lastvalue"].ToString(CultureInfo.InvariantCulture));
        await SendResponseAsync(response, content, "text/plain");
    }

    internal static Dictionary<string, int> ParsePrometheusSettings(IDictionary<string, string> query)
    {
        int archive = ClampQueryValue(query, "archivelength", 0, 0, 10);
        int timestamps = ClampQueryValue(query, "timestamps", 0, 0, 1);
        int lastValue = ClampQueryValue(query, "lastvalue", 1, 0, 1);

        if (archive == 0 && lastValue == 0)
        {
            archive = 1;
            timestamps = 1;
        }

        if (archive > 0)
            timestamps = 1;

        return new Dictionary<string, int>
        {
            ["archivelength"] = archive,
            ["timestamps"] = timestamps,
            ["lastvalue"] = lastValue
        };
    }

    internal static int ClampQueryValue(IDictionary<string, string> query, string key, int fallback, int min, int max)
    {
        if (!query.TryGetValue(key, out string? rawValue) || !int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return fallback;

        return Math.Clamp(value, min, max);
    }

    internal static string GeneratePrometheusResponse(SensorTreeItemViewModel? root, Dictionary<string, int> settings)
    {
        if (root == null)
            return "";

        StringBuilder builder = new();
        string host = root.Text;
        string lastTagName = "";

        foreach (SensorTreeItemViewModel hardware in root.Enumerate().Where(item => item.Hardware != null))
        {
            string hardwareType = hardware.Hardware!.HardwareType.ToString();
            string hardwareName = hardware.Hardware.Name;
            string hardwareId = hardware.Hardware.Identifier.ToString();
            string hardwareAlias = $"{hardwareName} ({hardwareId})";

            foreach (SensorTreeItemViewModel sensorItem in hardware.EnumerateSensors())
            {
                ISensor sensor = sensorItem.Sensor!;
                (string suffix, double factor) = GetPrometheusUnit(sensor.SensorType);
                string tagName = $"lhm_{hardwareType}_{sensor.SensorType}{suffix}".ToLowerInvariant();
                string sensorName = sensor.Name.Replace("#", "");
                string sensorId = sensor.Identifier.ToString().StartsWith(hardwareId, StringComparison.Ordinal)
                    ? sensor.Identifier.ToString()[hardwareId.Length..]
                    : sensor.Identifier.ToString();
                string sensorAlias = $"{sensorName} ({sensorId})";
                string tagLine = $$"""{{tagName}} {"sensorName"="{{EscapePrometheusLabel(sensorName)}}", "sensorAlias"="{{EscapePrometheusLabel(sensorAlias)}}", "hardwareName"="{{EscapePrometheusLabel(hardwareName)}}", "hardwareAlias"="{{EscapePrometheusLabel(hardwareAlias)}}", "sensorId"="{{EscapePrometheusLabel(sensorId)}}", "hardwareId"="{{EscapePrometheusLabel(hardwareId)}}", "host"="{{EscapePrometheusLabel(host)}}"}""";

                if (lastTagName != tagName)
                {
                    builder.Append("# TYPE ").Append(tagName).AppendLine(" gauge");
                    lastTagName = tagName;
                }

                int counter = 0;
                foreach (SensorValue value in sensor.Values.Reverse())
                {
                    if (counter++ > settings["archivelength"])
                        break;
                    if (float.IsNaN(value.Value))
                    {
                        builder.Append("# HELP ").Append(tagLine).AppendLine(" has an invalid value and was skipped.");
                        continue;
                    }
                    if (counter == 1 && settings["lastvalue"] == 0)
                        continue;

                    builder.Append(tagLine).Append(' ').Append((value.Value * factor).ToString(CultureInfo.InvariantCulture));
                    if (settings["timestamps"] == 1)
                        builder.Append(' ').Append(((DateTimeOffset)value.Time).ToUnixTimeMilliseconds());
                    builder.AppendLine();
                }
            }
        }

        return builder.ToString();
    }

    internal static (string Suffix, double Factor) GetPrometheusUnit(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Clock => ("_hertz", 1000000),
            SensorType.Conductivity => ("_seconds_per_centimeter", 0.000001),
            SensorType.Control => ("_percent", 1),
            SensorType.Current => ("_amperes", 1),
            SensorType.Data => ("_bytes", 1000000000),
            SensorType.Energy => ("_watthour", 0.001),
            SensorType.Fan => ("_rpm", 1),
            SensorType.Flow => ("_liters_per_hour", 1),
            SensorType.Frequency => ("_hertz", 1),
            SensorType.Humidity => ("_percent", 1),
            SensorType.Level => ("_percent", 1),
            SensorType.Load => ("_percent", 1),
            SensorType.Noise => ("_decibels", 1),
            SensorType.Power => ("_watts", 1),
            SensorType.SmallData => ("_bytes", 1024 * 1024),
            SensorType.Temperature => ("_celsius", 1),
            SensorType.Throughput => ("_bytes_per_second", 1),
            SensorType.TimeSpan => ("_seconds", 1),
            SensorType.Timing => ("_seconds", 0.000000001),
            SensorType.Voltage => ("_volts", 1),
            _ => ("", 1)
        };
    }

    private async Task SendJsonSensorAsync(HttpListenerResponse response, Dictionary<string, object?> sensorData)
    {
        WriteCommonHeaders(response);
        await SendResponseAsync(response, JsonSerializer.Serialize(sensorData), "application/json");
    }

    private static async Task SendResponseAsync(HttpListenerResponse response, string content, string contentType)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(content);
        response.ContentType = contentType;
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private async Task ServeResourceFileAsync(HttpListenerResponse response, string relativePath)
    {
        string normalizedPath = relativePath.Replace('/', '.').Replace('\\', '.').Replace("custom-theme", "custom_theme");
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".Resources." + normalizedPath, StringComparison.OrdinalIgnoreCase)
                                    || name.EndsWith(".Resources.Web." + normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        await using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        response.ContentType = GetContentType(Path.GetExtension(relativePath));
        response.ContentLength64 = stream.Length;
        await stream.CopyToAsync(response.OutputStream);
        response.Close();
    }

    private string ResolveListenerIp()
    {
        // Explicit wildcards bind every interface; pass them through with their exact prefixes.
        if (ListenerIp is "+" or "*" or "0.0.0.0")
            return ListenerIp;

        // "?" (and a blank value) is the unconfigured "auto" default, which historically bound all interfaces.
        if (string.IsNullOrWhiteSpace(ListenerIp) || ListenerIp == "?")
            return "+";

        // A specific address: bind exactly what the user configured. Unlike before, we no longer silently fall back to
        // "+" (every interface) when the address is not one of this host's enumerated addresses — that widened the
        // user's chosen scope and was even persisted back to settings. If the address cannot be bound, Start() fails and
        // the caller surfaces the error, so the user's binding intent is respected.
        return ListenerIp;
    }

    private static string PathForWebResource(string requestedFile)
    {
        return "Web." + requestedFile.TrimStart('/').Replace('/', '.');
    }

    private static string GetImageUrl(SensorTreeItemViewModel node)
    {
        if (node.Hardware != null)
            return "images_icon/" + GetHardwareImageFile(node.Hardware.HardwareType);
        if (node.Kind == SensorTreeItemKind.SensorType)
            return "images_icon/" + GetTypeImageFile(node.Text);
        if (node.Kind == SensorTreeItemKind.Root)
            return "images_icon/computer.png";

        return "images/transparent.png";
    }

    private static string GetHardwareImageFile(HardwareType hardwareType)
    {
        return hardwareType switch
        {
            HardwareType.Cpu => "cpu.png",
            HardwareType.GpuNvidia => "nvidia.png",
            HardwareType.GpuAmd => "ati.png",
            HardwareType.GpuIntel => "intel.png",
            HardwareType.Storage => "hdd.png",
            HardwareType.Motherboard => "mainboard.png",
            HardwareType.SuperIO => "chip.png",
            HardwareType.Memory => "ram.png",
            HardwareType.Cooler => "fan.png",
            HardwareType.Network => "nic.png",
            HardwareType.Psu => "power-supply.png",
            HardwareType.Battery => "battery.png",
            HardwareType.PowerMonitor => "powermonitor.png",
            _ => "cpu.png"
        };
    }

    private static string GetTypeImageFile(string sensorTypeText)
    {
        return sensorTypeText switch
        {
            "Voltages" or "Currents" or "Conductivity" => "voltage.png",
            "Clocks" or "Timings" or "Frequencies" => "clock.png",
            "Load" => "load.png",
            "Temperatures" => "temperature.png",
            "Fans" => "fan.png",
            "Flows" or "Humidity" => "flow.png",
            "Controls" => "control.png",
            "Levels" => "level.png",
            "Powers" => "power.png",
            "Noise" => "loudspeaker.png",
            "Throughput" => "throughput.png",
            "Data" or "Small Data" => "data.png",
            "Energy" or "Time Spans" => "time.png",
            _ => "power.png"
        };
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".css" => "text/css",
            ".gif" => "image/gif",
            ".htm" or ".html" => "text/html",
            ".ico" => "image/x-icon",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".js" => "application/javascript",
            ".png" => "image/png",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    internal static IDictionary<string, string> ParseQuery(string? query)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] split = part.Split('=', 2);
            string key = WebUtility.UrlDecode(split[0]);
            string value = split.Length > 1 ? WebUtility.UrlDecode(split[1]) : "";
            result[key] = value;
        }

        return result;
    }

    private static void WriteCommonHeaders(HttpListenerResponse response)
    {
        response.AddHeader("Cache-Control", "no-cache");

        // No "Access-Control-Allow-Origin: *": the bundled web UI is served from this same origin (so it needs no CORS
        // grant), and omitting the header lets the browser's same-origin policy keep arbitrary third-party sites from
        // reading sensor data. Server-to-server scrapers such as Prometheus are unaffected.
    }

    // Escapes a Prometheus exposition-format label value. Hardware/sensor names can contain characters that would
    // otherwise break the line or inject extra labels, so backslash, double-quote and newline must be escaped.
    private static string EscapePrometheusLabel(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

    // Retained for backward compatibility (and tests). The legacy unsalted SHA-256 scheme is now used only to verify and
    // upgrade pre-existing credentials; new passwords are hashed by PasswordHasher (PBKDF2).
    public static string ComputeSHA256(string text)
    {
        return PasswordHasher.ComputeLegacySha256(text);
    }
}

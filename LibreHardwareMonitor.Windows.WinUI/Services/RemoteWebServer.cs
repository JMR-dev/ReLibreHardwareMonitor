// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

public sealed class RemoteWebServer : IDisposable
{
    private readonly IComputer _computer;
    private readonly Func<SensorTreeItemViewModel?> _rootProvider;
    private readonly Version _version = typeof(RemoteWebServer).Assembly.GetName().Version ?? new Version(0, 0);
    private CancellationTokenSource? _cts;
    private HttpListener? _listener;
    private Task? _listenerTask;

    public RemoteWebServer(
        Func<SensorTreeItemViewModel?> rootProvider,
        IComputer computer,
        string listenerIp,
        int listenerPort,
        bool authEnabled,
        string userName,
        string passwordSha256)
    {
        _rootProvider = rootProvider;
        _computer = computer;
        ListenerIp = listenerIp;
        ListenerPort = listenerPort;
        AuthEnabled = authEnabled;
        UserName = userName;
        PasswordSHA256 = passwordSha256;

        try
        {
            _listener = new HttpListener { IgnoreWriteExceptions = true };
        }
        catch (PlatformNotSupportedException)
        {
            _listener = null;
        }
    }

    public bool AuthEnabled { get; set; }

    public bool IsRunning => _listener?.IsListening == true;

    public string ListenerIp { get; set; }

    public int ListenerPort { get; set; }

    public string PasswordSHA256 { get; private set; }

    public bool PlatformNotSupported => _listener == null;

    public string UserName { get; set; }

    public void Dispose()
    {
        Quit();
    }

    public void SetPassword(string plainPassword)
    {
        PasswordSHA256 = ComputeSHA256(plainPassword);
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
        if (PlatformNotSupported)
            return;

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
            if (request.HttpMethod == "POST")
            {
                await HandlePostRequestAsync(context.Response, request);
                return;
            }

            string requestedFile = request.RawUrl?.TrimStart('/') ?? "";
            if (requestedFile.Length == 0)
                requestedFile = "index.html";

            if (requestedFile.Equals("data.json", StringComparison.OrdinalIgnoreCase))
            {
                await SendJsonAsync(context.Response, request);
                return;
            }

            if (requestedFile.StartsWith("metrics", StringComparison.OrdinalIgnoreCase))
            {
                await SendPrometheusAsync(context.Response, request);
                return;
            }

            if (requestedFile.StartsWith("Sensor", StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, object?> result = [];
                HandleSensorRequest(request, result);
                await SendJsonSensorAsync(context.Response, result);
                return;
            }

            if (requestedFile.StartsWith("ResetAllMinMax", StringComparison.OrdinalIgnoreCase))
            {
                _computer.Accept(new SensorVisitor(sensor =>
                {
                    sensor.ResetMin();
                    sensor.ResetMax();
                }));
                await SendJsonAsync(context.Response, request);
                return;
            }

            if (requestedFile.StartsWith("images_icon/", StringComparison.OrdinalIgnoreCase))
            {
                await ServeResourceFileAsync(context.Response, requestedFile["images_icon/".Length..]);
                return;
            }

            await ServeResourceFileAsync(context.Response, PathForWebResource(requestedFile));
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

    private bool IsAuthenticated(HttpListenerContext context)
    {
        if (!AuthEnabled)
            return true;

        try
        {
            if (context.User?.Identity is HttpListenerBasicIdentity identity)
                return identity.Name == UserName && ComputeSHA256(identity.Password) == PasswordSHA256;
        }
        catch
        {
        }

        return false;
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
            result["result"] = "fail";
            result["message"] = ex.ToString();
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

        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");
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

    private Dictionary<string, object?> GenerateJsonForNode(SensorTreeItemViewModel node, ref int nodeIndex)
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
        Dictionary<string, int> settings = ParsePrometheusSettings(request);
        string content = GeneratePrometheusResponse(_rootProvider(), settings);

        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("X-archivelength", settings["archivelength"].ToString(CultureInfo.InvariantCulture));
        response.AddHeader("X-timestamps", settings["timestamps"].ToString(CultureInfo.InvariantCulture));
        response.AddHeader("X-lastvalue", settings["lastvalue"].ToString(CultureInfo.InvariantCulture));
        await SendResponseAsync(response, content, "text/plain");
    }

    private static Dictionary<string, int> ParsePrometheusSettings(HttpListenerRequest request)
    {
        Dictionary<string, string> query = new(ParseQuery(request.Url?.Query), StringComparer.OrdinalIgnoreCase);
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

    private static int ClampQueryValue(IDictionary<string, string> query, string key, int fallback, int min, int max)
    {
        if (!query.TryGetValue(key, out string? rawValue) || !int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return fallback;

        return Math.Clamp(value, min, max);
    }

    private static string GeneratePrometheusResponse(SensorTreeItemViewModel? root, Dictionary<string, int> settings)
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
                string tagLine = $$"""{{tagName}} {"sensorName"="{{sensorName}}", "sensorAlias"="{{sensorAlias}}", "hardwareName"="{{hardwareName}}", "hardwareAlias"="{{hardwareAlias}}", "sensorId"="{{sensorId}}", "hardwareId"="{{hardwareId}}", "host"="{{host}}"}""";

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

    private static (string Suffix, double Factor) GetPrometheusUnit(SensorType sensorType)
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
        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");
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
        if (ListenerIp is "+" or "*" or "0.0.0.0")
            return ListenerIp;

        try
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            if (host.AddressList.Any(ip => ip.ToString() == ListenerIp))
                return ListenerIp;
        }
        catch
        {
        }

        ListenerIp = "+";
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

    private static IDictionary<string, string> ParseQuery(string? query)
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

    public static string ComputeSHA256(string text)
    {
        using SHA256 hash = SHA256.Create();
        return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
    }
}

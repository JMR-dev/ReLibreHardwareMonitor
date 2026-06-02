// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Moq;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Services;

// Characterization tests pinning the behavior the Phase 2 security hardening must preserve:
// request routing (including the no-hijack rule), query parsing, Prometheus settings/units/shape,
// the data.json node shape, and credential-verification semantics (including legacy SHA-256).
public class RemoteWebServerTests
{
    // ---- ResolveRoute -------------------------------------------------------

    [Theory]
    [InlineData("GET", "/data.json", "DataJson")]
    [InlineData("GET", "/DATA.JSON", "DataJson")]
    [InlineData("GET", "/metrics", "Metrics")]
    [InlineData("GET", "/Sensor", "Sensor")]
    [InlineData("GET", "/ResetAllMinMax", "ResetAllMinMax")]
    [InlineData("POST", "/Sensor", "Post")]
    [InlineData("POST", "/anything", "Post")]
    public void ResolveRoute_MapsEndpoints(string method, string path, string expectedKind)
    {
        Assert.Equal(expectedKind, RemoteWebServer.ResolveRoute(method, path, null).Kind.ToString());
    }

    [Theory]
    // A static asset whose name merely starts with an endpoint name must NOT be hijacked by the API handler.
    [InlineData("/metrics.html", "Web.metrics.html")]
    [InlineData("/data.json.html", "Web.data.json.html")]
    [InlineData("/sensor-icons.css", "Web.sensor-icons.css")]
    public void ResolveRoute_StaticAssetsAreNotHijackedByEndpoints(string path, string expectedResource)
    {
        RemoteWebServer.RemoteWebRoute route = RemoteWebServer.ResolveRoute("GET", path, null);
        Assert.Equal(RemoteWebServer.RemoteWebRouteKind.Resource, route.Kind);
        Assert.Equal(expectedResource, route.ResourcePath);
    }

    [Fact]
    public void ResolveRoute_EmptyPath_ServesIndexHtmlResource()
    {
        RemoteWebServer.RemoteWebRoute route = RemoteWebServer.ResolveRoute("GET", "/", null);
        Assert.Equal(RemoteWebServer.RemoteWebRouteKind.Resource, route.Kind);
        Assert.Equal("Web.index.html", route.ResourcePath);
    }

    [Fact]
    public void ResolveRoute_IconPrefix_ServesRawRemainderResource()
    {
        RemoteWebServer.RemoteWebRoute route = RemoteWebServer.ResolveRoute("GET", "/images_icon/cpu.png", null);
        Assert.Equal(RemoteWebServer.RemoteWebRouteKind.Resource, route.Kind);
        Assert.Equal("cpu.png", route.ResourcePath);
    }

    [Fact]
    public void ResolveRoute_FallsBackToRawUrlWhenAbsolutePathNull()
    {
        Assert.Equal(RemoteWebServer.RemoteWebRouteKind.DataJson, RemoteWebServer.ResolveRoute("GET", null, "/data.json").Kind);
    }

    // ---- ParseQuery ---------------------------------------------------------

    [Fact]
    public void ParseQuery_ParsesAndUrlDecodes()
    {
        IDictionary<string, string> query = RemoteWebServer.ParseQuery("?action=Get&id=%2Fcpu%2F0");
        Assert.Equal("Get", query["action"]);
        Assert.Equal("/cpu/0", query["id"]);
    }

    [Fact]
    public void ParseQuery_KeysAreCaseInsensitive()
    {
        IDictionary<string, string> query = RemoteWebServer.ParseQuery("?Action=Get");
        Assert.True(query.ContainsKey("action"));
    }

    [Fact]
    public void ParseQuery_MissingEquals_YieldsEmptyValue()
    {
        IDictionary<string, string> query = RemoteWebServer.ParseQuery("?flag");
        Assert.Equal("", query["flag"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseQuery_EmptyOrNull_ReturnsEmpty(string? query)
    {
        Assert.Empty(RemoteWebServer.ParseQuery(query));
    }

    // ---- ClampQueryValue ----------------------------------------------------

    [Fact]
    public void ClampQueryValue_ParsesAndClamps()
    {
        var query = new Dictionary<string, string> { ["v"] = "5" };
        Assert.Equal(5, RemoteWebServer.ClampQueryValue(query, "v", 0, 0, 10));

        query["v"] = "99";
        Assert.Equal(10, RemoteWebServer.ClampQueryValue(query, "v", 0, 0, 10));

        query["v"] = "-5";
        Assert.Equal(0, RemoteWebServer.ClampQueryValue(query, "v", 0, 0, 10));
    }

    [Fact]
    public void ClampQueryValue_MissingOrUnparseable_ReturnsFallback()
    {
        var query = new Dictionary<string, string> { ["v"] = "abc" };
        Assert.Equal(3, RemoteWebServer.ClampQueryValue(query, "v", 3, 0, 10));
        Assert.Equal(3, RemoteWebServer.ClampQueryValue(query, "missing", 3, 0, 10));
    }

    // ---- ParsePrometheusSettings (the archive/lastvalue/timestamps interplay) ----

    [Fact]
    public void ParsePrometheusSettings_Defaults_LastValueOnly()
    {
        Dictionary<string, int> settings = RemoteWebServer.ParsePrometheusSettings(new Dictionary<string, string>());
        Assert.Equal(0, settings["archivelength"]);
        Assert.Equal(0, settings["timestamps"]);
        Assert.Equal(1, settings["lastvalue"]);
    }

    [Fact]
    public void ParsePrometheusSettings_NoArchiveNoLastValue_ForcesArchiveAndTimestamps()
    {
        Dictionary<string, int> settings = RemoteWebServer.ParsePrometheusSettings(new Dictionary<string, string> { ["lastvalue"] = "0" });
        Assert.Equal(1, settings["archivelength"]);
        Assert.Equal(1, settings["timestamps"]);
        Assert.Equal(0, settings["lastvalue"]);
    }

    [Fact]
    public void ParsePrometheusSettings_ArchivePositive_ForcesTimestamps()
    {
        Dictionary<string, int> settings = RemoteWebServer.ParsePrometheusSettings(new Dictionary<string, string> { ["archivelength"] = "3" });
        Assert.Equal(3, settings["archivelength"]);
        Assert.Equal(1, settings["timestamps"]);
        Assert.Equal(1, settings["lastvalue"]);
    }

    // ---- GetPrometheusUnit --------------------------------------------------

    [Theory]
    [InlineData(SensorType.Temperature, "_celsius", 1.0)]
    [InlineData(SensorType.Clock, "_hertz", 1000000.0)]
    [InlineData(SensorType.Data, "_bytes", 1000000000.0)]
    [InlineData(SensorType.SmallData, "_bytes", 1048576.0)]
    [InlineData(SensorType.Load, "_percent", 1.0)]
    [InlineData((SensorType)999, "", 1.0)]
    public void GetPrometheusUnit_MapsUnitAndFactor(SensorType sensorType, string expectedSuffix, double expectedFactor)
    {
        (string suffix, double factor) = RemoteWebServer.GetPrometheusUnit(sensorType);
        Assert.Equal(expectedSuffix, suffix);
        Assert.Equal(expectedFactor, factor);
    }

    // ---- GenerateJsonForNode (data.json shape) ------------------------------

    [Fact]
    public void GenerateJsonForNode_ProducesExpectedShape()
    {
        ISensor sensor = CreateSensor("Core 0", SensorType.Temperature, new Identifier("cpu", "0", "temperature", "0"), value: 50f, min: 40f, max: 60f);
        IHardware hardware = CreateHardware("CPU", HardwareType.Cpu, new Identifier("cpu", "0"), sensor);

        SensorTreeItemViewModel root = SensorTreeItemViewModel.CreateRoot("HOST");
        root.Children.Add(SensorTreeItemViewModel.FromHardware(hardware, AppSettings.LoadDefault()));

        int nodeIndex = 1;
        Dictionary<string, object?> json = RemoteWebServer.GenerateJsonForNode(root, ref nodeIndex);

        Assert.Equal(1, json["id"]);
        Assert.Equal("HOST", json["Text"]);

        var hardwareJson = (Dictionary<string, object?>)((List<object>)json["Children"]!).Single();
        Assert.Equal("CPU", hardwareJson["Text"]);
        Assert.Equal(hardware.Identifier.ToString(), hardwareJson["HardwareId"]);

        var typeGroupJson = (Dictionary<string, object?>)((List<object>)hardwareJson["Children"]!).Single();
        var sensorJson = (Dictionary<string, object?>)((List<object>)typeGroupJson["Children"]!).Single();

        Assert.Equal(sensor.Identifier.ToString(), sensorJson["SensorId"]);
        Assert.Equal("Temperature", sensorJson["Type"]);
        Assert.Equal(50f, sensorJson["RawValue"]);
        Assert.Equal(40f, sensorJson["RawMin"]);
        Assert.Equal(60f, sensorJson["RawMax"]);
        Assert.Equal("images/transparent.png", sensorJson["ImageURL"]);
    }

    // ---- GeneratePrometheusResponse -----------------------------------------

    private static readonly Dictionary<string, int> LastValueSettings = new()
    {
        ["archivelength"] = 0,
        ["timestamps"] = 0,
        ["lastvalue"] = 1
    };

    [Fact]
    public void GeneratePrometheusResponse_EmitsTypeLineAndMetric()
    {
        ISensor sensor = CreateSensor("Core 0", SensorType.Temperature, new Identifier("cpu", "0", "temperature", "0"), value: 50f);
        sensor = WithValues(sensor, new SensorValue(50f, DateTime.UtcNow));
        SensorTreeItemViewModel root = BuildRoot("HOST", CreateHardware("CPU", HardwareType.Cpu, new Identifier("cpu", "0"), sensor));

        string output = RemoteWebServer.GeneratePrometheusResponse(root, LastValueSettings);

        Assert.Contains("# TYPE lhm_cpu_temperature_celsius gauge", output);
        Assert.Contains("lhm_cpu_temperature_celsius {", output);
        Assert.Contains("\"host\"=\"HOST\"", output);
        Assert.Contains(" 50", output);
    }

    [Fact]
    public void GeneratePrometheusResponse_NaNValue_IsSkippedWithHelpLine()
    {
        ISensor sensor = CreateSensor("Core 0", SensorType.Temperature, new Identifier("cpu", "0", "temperature", "0"), value: float.NaN);
        sensor = WithValues(sensor, new SensorValue(float.NaN, DateTime.UtcNow));
        SensorTreeItemViewModel root = BuildRoot("HOST", CreateHardware("CPU", HardwareType.Cpu, new Identifier("cpu", "0"), sensor));

        string output = RemoteWebServer.GeneratePrometheusResponse(root, LastValueSettings);

        Assert.Contains("has an invalid value and was skipped", output);
    }

    [Fact]
    public void GeneratePrometheusResponse_SameTagEmittedOnce()
    {
        ISensor sensor1 = WithValues(CreateSensor("Core 0", SensorType.Temperature, new Identifier("cpu", "0", "temperature", "0"), 50f), new SensorValue(50f, DateTime.UtcNow));
        ISensor sensor2 = WithValues(CreateSensor("Core 1", SensorType.Temperature, new Identifier("cpu", "0", "temperature", "1"), 60f), new SensorValue(60f, DateTime.UtcNow));
        IHardware hardware = CreateHardware("CPU", HardwareType.Cpu, new Identifier("cpu", "0"), sensor1, sensor2);
        SensorTreeItemViewModel root = BuildRoot("HOST", hardware);

        string output = RemoteWebServer.GeneratePrometheusResponse(root, LastValueSettings);

        int typeLineCount = output.Split('\n').Count(line => line.StartsWith("# TYPE lhm_cpu_temperature_celsius"));
        Assert.Equal(1, typeLineCount);
    }

    [Fact]
    public void GeneratePrometheusResponse_NullRoot_ReturnsEmpty()
    {
        Assert.Equal("", RemoteWebServer.GeneratePrometheusResponse(null, LastValueSettings));
    }

    [Fact]
    public void GeneratePrometheusResponse_EscapesLabelValues()
    {
        ISensor sensor = WithValues(CreateSensor("Core \"0\"", SensorType.Temperature, new Identifier("cpu", "0", "temperature", "0"), 50f), new SensorValue(50f, DateTime.UtcNow));
        SensorTreeItemViewModel root = BuildRoot("HOST", CreateHardware("CPU", HardwareType.Cpu, new Identifier("cpu", "0"), sensor));

        string output = RemoteWebServer.GeneratePrometheusResponse(root, LastValueSettings);

        // The double quote in the sensor name must be backslash-escaped so it can't break or inject labels.
        Assert.Contains("Core \\\"0\\\"", output);
    }

    // ---- ComputeSHA256 (legacy hashing; Phase 2 must keep verifying these) ----

    [Fact]
    public void ComputeSHA256_KnownVector()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", RemoteWebServer.ComputeSHA256("abc"));
    }

    // ---- VerifyCredentials --------------------------------------------------

    [Fact]
    public void VerifyCredentials_AuthDisabled_AlwaysTrue()
    {
        using RemoteWebServer server = CreateServer(authEnabled: false, "admin", "secret");
        Assert.True(server.VerifyCredentials("anything", "anything"));
        Assert.True(server.VerifyCredentials(null, null));
    }

    [Fact]
    public void VerifyCredentials_CorrectCredentials_True()
    {
        using RemoteWebServer server = CreateServer(authEnabled: true, "admin", "secret");
        Assert.True(server.VerifyCredentials("admin", "secret"));
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("root", "secret")]
    [InlineData(null, "secret")]
    [InlineData("admin", null)]
    public void VerifyCredentials_BadCredentials_False(string? userName, string? password)
    {
        using RemoteWebServer server = CreateServer(authEnabled: true, "admin", "secret");
        Assert.False(server.VerifyCredentials(userName, password));
    }

    [Fact]
    public void SetPassword_ProducesPbkdf2HashThatVerifies()
    {
        using RemoteWebServer server = CreateServer(authEnabled: true, "admin", "secret");
        server.SetPassword("newpass");

        Assert.StartsWith("pbkdf2$", server.PasswordHash);
        Assert.True(server.VerifyCredentials("admin", "newpass"));
        Assert.False(server.VerifyCredentials("admin", "secret"));
    }

    [Fact]
    public void VerifyCredentials_LegacyHash_UpgradesToPbkdf2OnSuccess()
    {
        // CreateServer seeds the stored hash with the legacy unsalted SHA-256 of the password.
        using RemoteWebServer server = CreateServer(authEnabled: true, "admin", "secret");
        Assert.False(server.PasswordHash.StartsWith("pbkdf2$"));

        Assert.True(server.VerifyCredentials("admin", "secret")); // verified via the legacy path...
        Assert.StartsWith("pbkdf2$", server.PasswordHash);        // ...and transparently upgraded

        Assert.True(server.VerifyCredentials("admin", "secret")); // still verifies via the upgraded hash
    }

    // ---- helpers ------------------------------------------------------------

    private static RemoteWebServer CreateServer(bool authEnabled, string userName, string password)
    {
        return new RemoteWebServer(() => null, Mock.Of<IComputer>(), new object(), "localhost", 8085, authEnabled, userName, RemoteWebServer.ComputeSHA256(password));
    }

    private static ISensor CreateSensor(string name, SensorType type, Identifier identifier, float? value = null, float? min = null, float? max = null)
    {
        var mock = new Mock<ISensor>();
        mock.Setup(s => s.Name).Returns(name);
        mock.Setup(s => s.SensorType).Returns(type);
        mock.Setup(s => s.Identifier).Returns(identifier);
        mock.Setup(s => s.Index).Returns(0);
        mock.Setup(s => s.Value).Returns(value);
        mock.Setup(s => s.Min).Returns(min);
        mock.Setup(s => s.Max).Returns(max);
        mock.Setup(s => s.Values).Returns(Array.Empty<SensorValue>());
        return mock.Object;
    }

    private static ISensor WithValues(ISensor sensor, params SensorValue[] values)
    {
        Mock.Get(sensor).Setup(s => s.Values).Returns(values);
        return sensor;
    }

    private static IHardware CreateHardware(string name, HardwareType type, Identifier identifier, params ISensor[] sensors)
    {
        var mock = new Mock<IHardware>();
        mock.Setup(h => h.Name).Returns(name);
        mock.Setup(h => h.HardwareType).Returns(type);
        mock.Setup(h => h.Identifier).Returns(identifier);
        mock.Setup(h => h.Sensors).Returns(sensors);
        mock.Setup(h => h.SubHardware).Returns(Array.Empty<IHardware>());
        return mock.Object;
    }

    private static SensorTreeItemViewModel BuildRoot(string host, IHardware hardware)
    {
        SensorTreeItemViewModel root = SensorTreeItemViewModel.CreateRoot(host);
        root.Children.Add(SensorTreeItemViewModel.FromHardware(hardware, AppSettings.LoadDefault()));
        return root;
    }
}

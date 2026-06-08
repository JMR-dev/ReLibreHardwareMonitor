using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace LibreHardwareMonitorLib.Tests.Hardware;

public class SettingsParsingTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    public void IsTruthy_ParsesExpectedTokens(string value, bool expected)
    {
        Assert.Equal(expected, SettingsParsing.IsTruthy(value));
    }

    [Fact]
    public void ShouldDefer_UsesEnvironmentValueBeforeSetting()
    {
        string environmentVariable = $"{nameof(SettingsParsingTests)}_{Guid.NewGuid():N}";
        DictionarySettings settings = new();
        settings.SetValue("test.defer", "false");

        try
        {
            Environment.SetEnvironmentVariable(environmentVariable, "yes");

            Assert.True(SettingsParsing.ShouldDefer(settings, "test.defer", environmentVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
        }
    }

    [Fact]
    public void ShouldDefer_FallsBackToSettingWhenEnvironmentValueIsBlank()
    {
        string environmentVariable = $"{nameof(SettingsParsingTests)}_{Guid.NewGuid():N}";
        DictionarySettings settings = new();
        settings.SetValue("test.defer", "true");

        try
        {
            Environment.SetEnvironmentVariable(environmentVariable, "");

            Assert.True(SettingsParsing.ShouldDefer(settings, "test.defer", environmentVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
        }
    }

    private sealed class DictionarySettings : ISettings
    {
        private readonly Dictionary<string, string> _values = new();

        public bool Contains(string name)
        {
            return _values.ContainsKey(name);
        }

        public void SetValue(string name, string value)
        {
            _values[name] = value;
        }

        public string GetValue(string name, string value)
        {
            return _values.TryGetValue(name, out string? result) ? result : value;
        }

        public void Remove(string name)
        {
            _values.Remove(name);
        }
    }
}

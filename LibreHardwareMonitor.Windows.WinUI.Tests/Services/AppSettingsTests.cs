using System;
using System.IO;
using System.Reflection;
using LibreHardwareMonitor.Windows.WinUI.Services;
using Windows.UI;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Services;

public class AppSettingsTests : IDisposable
{
    private readonly AppSettings _settings;
    private readonly string _tempFile;

    public AppSettingsTests()
    {
        _tempFile = Path.GetTempFileName();
        
        // Use reflection to instantiate AppSettings with custom filename
        var ctor = typeof(AppSettings).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
        _settings = (AppSettings)ctor!.Invoke(new object[] { _tempFile });
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void FileName_ReturnsCorrectPath()
    {
        Assert.Equal(_tempFile, _settings.FileName);
    }

    [Fact]
    public void SetAndGetString()
    {
        _settings.SetValue("testStr", "hello");
        Assert.True(_settings.Contains("testStr"));
        Assert.Equal("hello", _settings.GetValue("testStr", "default"));
        Assert.Equal("default", _settings.GetValue("missing", "default"));
    }

    [Fact]
    public void SetAndGetInt()
    {
        _settings.SetValue("testInt", 42);
        Assert.Equal(42, _settings.GetValue("testInt", 0));
        Assert.Equal(10, _settings.GetValue("missing", 10));
    }

    [Fact]
    public void SetAndGetFloat()
    {
        _settings.SetValue("testFloat", 3.14f);
        Assert.Equal(3.14f, _settings.GetValue("testFloat", 0f));
        Assert.Equal(1.5f, _settings.GetValue("missing", 1.5f));
    }

    [Fact]
    public void SetAndGetDouble()
    {
        _settings.SetValue("testDouble", 3.14159);
        Assert.Equal(3.14159, _settings.GetValue("testDouble", 0d));
        Assert.Equal(1.5d, _settings.GetValue("missing", 1.5d));
    }

    [Fact]
    public void SetAndGetBool()
    {
        _settings.SetValue("testBool", true);
        Assert.True(_settings.GetValue("testBool", false));
        Assert.False(_settings.GetValue("missing", false));
    }

    [Fact]
    public void SetAndGetColor()
    {
        var color = Color.FromArgb(255, 100, 150, 200);
        _settings.SetValue("testColor", color);
        var retrievedColor = _settings.GetValue("testColor", Color.FromArgb(0, 0, 0, 0));
        
        Assert.Equal(color.A, retrievedColor.A);
        Assert.Equal(color.R, retrievedColor.R);
        Assert.Equal(color.G, retrievedColor.G);
        Assert.Equal(color.B, retrievedColor.B);

        var defaultColor = Color.FromArgb(10, 20, 30, 40);
        Assert.Equal(defaultColor, _settings.GetValue("missing", defaultColor));
    }

    [Fact]
    public void Remove_DeletesKey()
    {
        _settings.SetValue("toRemove", "value");
        Assert.True(_settings.Contains("toRemove"));
        _settings.Remove("toRemove");
        Assert.False(_settings.Contains("toRemove"));
    }

    [Fact]
    public void SaveAndLoad_PersistsData()
    {
        _settings.SetValue("persistKey", "persistValue");
        _settings.Save();

        // Create new instance pointing to same file
        var ctor = typeof(AppSettings).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
        var loadedSettings = (AppSettings)ctor!.Invoke(new object[] { _tempFile });
        
        loadedSettings.Load();

        Assert.Equal("persistValue", loadedSettings.GetValue("persistKey", ""));
    }
}

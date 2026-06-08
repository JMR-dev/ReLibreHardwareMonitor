using System;
using System.IO;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace LibreHardwareMonitorLib.Tests.Hardware;

public class StartupTraceLogSupportTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\r\nb", "\"a\r\nb\"")]
    public void EscapeCsv_EscapesOnlyWhenNeeded(string value, string expected)
    {
        Assert.Equal(expected, StartupTraceLogSupport.EscapeCsv(value));
    }

    [Fact]
    public void GetLogFileName_EmptyPathUsesBaseDirectory()
    {
        string path = StartupTraceLogSupport.GetLogFileName("TestTrace", "");

        Assert.Equal(NormalizeDirectory(AppContext.BaseDirectory), NormalizeDirectory(Path.GetDirectoryName(path)!));
        Assert.StartsWith("TestTrace-", Path.GetFileName(path));
        Assert.EndsWith(".log", Path.GetFileName(path));
    }

    [Fact]
    public void GetLogFileName_DirectoryPathCombinesGeneratedFileName()
    {
        string path = StartupTraceLogSupport.GetLogFileName("TestTrace", AppContext.BaseDirectory);

        Assert.Equal(NormalizeDirectory(AppContext.BaseDirectory), NormalizeDirectory(Path.GetDirectoryName(path)!));
        Assert.StartsWith("TestTrace-", Path.GetFileName(path));
        Assert.EndsWith(".log", Path.GetFileName(path));
    }

    [Fact]
    public void GetLogFileName_ExplicitFilePathIsReturnedAsConfigured()
    {
        string configuredPath = Path.Combine(AppContext.BaseDirectory, "explicit-startup.log");

        string path = StartupTraceLogSupport.GetLogFileName("Ignored", configuredPath);

        Assert.Equal(configuredPath, path);
    }

    private static string NormalizeDirectory(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

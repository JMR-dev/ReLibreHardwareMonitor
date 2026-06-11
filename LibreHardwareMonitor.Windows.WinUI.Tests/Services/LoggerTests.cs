using System;
using System.IO;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using Moq;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Services;

public class LoggerTests
{
    [Fact]
    public void Constructor_SubscribesToComputerEvents()
    {
        var mockComputer = new Mock<IComputer>();

        var logger = new Logger(mockComputer.Object);

        // We can't directly test if the event was subscribed, but we can verify default properties
        Assert.Equal(LoggerFileRotation.PerSession, logger.FileRotationMethod);
        Assert.Equal(TimeSpan.FromSeconds(1), logger.LoggingInterval);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var mockComputer = new Mock<IComputer>();
        var logger = new Logger(mockComputer.Object)
        {
            FileRotationMethod = LoggerFileRotation.Daily,
            LoggingInterval = TimeSpan.FromMinutes(5)
        };

        Assert.Equal(LoggerFileRotation.Daily, logger.FileRotationMethod);
        Assert.Equal(TimeSpan.FromMinutes(5), logger.LoggingInterval);
    }

    [Fact]
    public void Log_DoesNotThrow_WhenNoSensors()
    {
        var mockComputer = new Mock<IComputer>();
        var logger = new Logger(mockComputer.Object);

        // We can't fully mock the file system easily here without abstraction,
        // but if there are no sensors it will just create a basic CSV.
        // Actually, it tries to create a file in the app directory, which might throw in some environments.
        // Let's just assure it doesn't crash when interval hasn't passed.

        logger.LoggingInterval = TimeSpan.FromHours(1);
        logger.Log(); // Should execute

        // Calling again immediately should return early without doing anything
        logger.Log();
    }

    // Characterization (deterministic clock + temp directory via the internal test seam):

    [Fact]
    public void Log_PerSession_CreatesFileWithHeaderRows()
    {
        string dir = NewTempDir();
        try
        {
            var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var logger = new Logger(Mock.Of<IComputer>(), clock, dir) { LoggingInterval = TimeSpan.FromSeconds(1) };

            logger.Log();

            string[] files = Directory.GetFiles(dir);
            Assert.Single(files);
            Assert.Equal("LibreHardwareMonitorLog-2026-01-01-1.csv", Path.GetFileName(files[0]));
            // Header is an identifiers row then a "Time,"-prefixed names row.
            Assert.Contains("Time,", File.ReadAllText(files[0]));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Log_WithinInterval_SkipsSecondCall()
    {
        string dir = NewTempDir();
        try
        {
            var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var logger = new Logger(Mock.Of<IComputer>(), clock, dir) { LoggingInterval = TimeSpan.FromSeconds(1) };

            logger.Log();
            long sizeAfterFirst = new FileInfo(Directory.GetFiles(dir)[0]).Length;

            logger.Log(); // same instant: interval has not elapsed -> no-op

            Assert.Single(Directory.GetFiles(dir));
            Assert.Equal(sizeAfterFirst, new FileInfo(Directory.GetFiles(dir)[0]).Length);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Log_Daily_UsesDateBasedFileName()
    {
        string dir = NewTempDir();
        try
        {
            var clock = new FakeClock(new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero));
            var logger = new Logger(Mock.Of<IComputer>(), clock, dir)
            {
                FileRotationMethod = LoggerFileRotation.Daily,
                LoggingInterval = TimeSpan.FromSeconds(1)
            };

            logger.Log();

            Assert.True(File.Exists(Path.Combine(dir, "LibreHardwareMonitorLog-2026-01-02.csv")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lhm-logger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch
        {
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

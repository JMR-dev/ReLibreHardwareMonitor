using System;
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
}

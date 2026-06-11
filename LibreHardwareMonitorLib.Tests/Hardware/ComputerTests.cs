using System;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace LibreHardwareMonitorLib.Tests.Hardware;

public class ComputerTests
{
    [Fact]
    public void OpenAndClose_WorksCorrectly_AndCompletesHardwareDiscoveryTask()
    {
        var computer = new Computer
        {
            IsCpuEnabled = false,
            IsGpuEnabled = false,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false,
            IsBatteryEnabled = false,
            IsPsuEnabled = false,
            IsPowerMonitorEnabled = false
        };

        // HardwareDiscoveryTask should be available
        Assert.NotNull(computer.HardwareDiscoveryTask);
        
        // Open the computer
        computer.Open();

        // HardwareDiscoveryTask should complete because all are disabled, so no long-running task
        Assert.True(computer.HardwareDiscoveryTask.IsCompleted);

        // Close the computer
        computer.Close();

        // Ensure it doesn't throw and remains in a good state
        Assert.True(computer.HardwareDiscoveryTask.IsCompleted);
    }

    [Fact]
    public async Task OpenAsync_WorksCorrectly_AndCompletesHardwareDiscoveryTask()
    {
        var computer = new Computer
        {
            IsCpuEnabled = false,
            IsGpuEnabled = false,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false,
            IsBatteryEnabled = false,
            IsPsuEnabled = false,
            IsPowerMonitorEnabled = false
        };

        await computer.OpenAsync();

        // The HardwareDiscoveryTask should complete successfully.
        Assert.True(computer.HardwareDiscoveryTask.IsCompletedSuccessfully);

        computer.Close();
    }

    [Fact]
    public void Reset_ClearsHardwareAndCompletesDiscovery()
    {
        var computer = new Computer
        {
            IsCpuEnabled = false
        };

        computer.Open();
        Assert.True(computer.HardwareDiscoveryTask.IsCompleted);

        computer.Reset();

        Assert.Empty(computer.Hardware);
        Assert.True(computer.HardwareDiscoveryTask.IsCompleted);
    }
}

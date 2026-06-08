// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.IO;
using System.Reflection;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Services;

// Characterization tests pinning behavior the Phase 4 cleanup must preserve: the exact settings keys each
// hardware-enable flag writes (the planned (getter, setter, key) table must keep these identical for config
// compatibility), and the basic RebuildTree/TreeRebuilt contract. The async tree-rebuild coalescer is covered
// once it is extracted into its own testable unit during Phase 4.
public class HardwareMonitorServiceTests
{
    [Fact]
    public void Constructor_AppliesDeferredDetectionDefaultsUsingSharedHardwareKeys()
    {
        AppSettings settings = CreateBlankSettings();
        using var service = new HardwareMonitorService(settings);
        string[] deferredDefaults =
        {
            HardwareSettingsKeys.MemoryDeferDimmDetection,
            HardwareSettingsKeys.CpuDeferInitialUpdate,
            HardwareSettingsKeys.CpuDeferTscEstimation,
            HardwareSettingsKeys.NvidiaDeferDetection,
            HardwareSettingsKeys.StorageDeferDetection,
            HardwareSettingsKeys.NetworkDeferDetection,
            HardwareSettingsKeys.IntelGpuDeferDetection,
            HardwareSettingsKeys.ControllerDeferDetection,
            HardwareSettingsKeys.PsuDeferDetection,
        };

        foreach (string key in deferredDefaults)
        {
            Assert.True(settings.Contains(key), $"Missing default for {key}.");
            Assert.True(settings.GetValue(key, false), $"Expected {key} to default to true.");
        }
    }

    [Fact]
    public void EnableFlags_WriteExpectedSettingKeysAndComputerFlags()
    {
        using var service = new HardwareMonitorService(AppSettings.LoadDefault());

        service.IsMotherboardEnabled = false;
        Assert.False(service.Settings.GetValue("mainboardMenuItem", true));
        Assert.False(service.Computer.IsMotherboardEnabled);

        service.IsCpuEnabled = false;
        Assert.False(service.Settings.GetValue("cpuMenuItem", true));
        Assert.False(service.Computer.IsCpuEnabled);

        service.IsMemoryEnabled = false;
        Assert.False(service.Settings.GetValue("ramMenuItem", true));
        Assert.False(service.Computer.IsMemoryEnabled);

        service.IsGpuEnabled = false;
        Assert.False(service.Settings.GetValue("gpuMenuItem", true));
        Assert.False(service.Computer.IsGpuEnabled);

        service.IsPowerMonitorEnabled = false;
        Assert.False(service.Settings.GetValue("powerMonitorMenuItem", true));
        Assert.False(service.Computer.IsPowerMonitorEnabled);

        service.IsControllerEnabled = false;
        Assert.False(service.Settings.GetValue("fanControllerMenuItem", true));
        Assert.False(service.Computer.IsControllerEnabled);

        service.IsStorageEnabled = false;
        Assert.False(service.Settings.GetValue("hddMenuItem", true));
        Assert.False(service.Computer.IsStorageEnabled);

        service.IsNetworkEnabled = false;
        Assert.False(service.Settings.GetValue("nicMenuItem", true));
        Assert.False(service.Computer.IsNetworkEnabled);

        service.IsPsuEnabled = false;
        Assert.False(service.Settings.GetValue("psuMenuItem", true));
        Assert.False(service.Computer.IsPsuEnabled);

        service.IsBatteryEnabled = false;
        Assert.False(service.Settings.GetValue("batteryMenuItem", true));
        Assert.False(service.Computer.IsBatteryEnabled);
    }

    [Fact]
    public void RebuildTree_RaisesTreeRebuiltAndPopulatesRoot()
    {
        using var service = new HardwareMonitorService(AppSettings.LoadDefault());
        bool raised = false;
        service.TreeRebuilt += (_, _) => raised = true;

        service.RebuildTree();

        Assert.True(raised);
        Assert.NotNull(service.Root);
        Assert.Equal(Environment.MachineName, service.Root.Text);
    }

    [Fact]
    public void RebuildTree_WithoutRaiseFlag_DoesNotInvokeEvent()
    {
        using var service = new HardwareMonitorService(AppSettings.LoadDefault());
        bool raised = false;
        service.TreeRebuilt += (_, _) => raised = true;

        service.RebuildTree(raiseTreeRebuilt: false);

        Assert.False(raised);
    }

    private static AppSettings CreateBlankSettings()
    {
        string fileName = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.config");
        ConstructorInfo? ctor = typeof(AppSettings).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
        return (AppSettings)ctor!.Invoke(new object[] { fileName });
    }
}

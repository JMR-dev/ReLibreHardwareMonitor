// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private readonly object _updateLock = new();
    private readonly UpdateVisitor _updateVisitor = new();
    private bool _isOpen;

    public HardwareMonitorService(AppSettings settings)
    {
        Settings = settings;
        Computer = new Computer(settings);
        Computer.HardwareAdded += HardwareChanged;
        Computer.HardwareRemoved += HardwareChanged;
        ApplyHardwareFlagsFromSettings();
    }

    public event EventHandler? TreeRebuilt;

    public Computer Computer { get; }

    public AppSettings Settings { get; }

    public SensorTreeItemViewModel Root { get; private set; } = SensorTreeItemViewModel.CreateRoot(Environment.MachineName);

    public bool IsMotherboardEnabled
    {
        get => Computer.IsMotherboardEnabled;
        set => SetHardwareEnabled("mainboardMenuItem", value, v => Computer.IsMotherboardEnabled = v);
    }

    public bool IsCpuEnabled
    {
        get => Computer.IsCpuEnabled;
        set => SetHardwareEnabled("cpuMenuItem", value, v => Computer.IsCpuEnabled = v);
    }

    public bool IsMemoryEnabled
    {
        get => Computer.IsMemoryEnabled;
        set => SetHardwareEnabled("ramMenuItem", value, v => Computer.IsMemoryEnabled = v);
    }

    public bool IsGpuEnabled
    {
        get => Computer.IsGpuEnabled;
        set => SetHardwareEnabled("gpuMenuItem", value, v => Computer.IsGpuEnabled = v);
    }

    public bool IsPowerMonitorEnabled
    {
        get => Computer.IsPowerMonitorEnabled;
        set => SetHardwareEnabled("powerMonitorMenuItem", value, v => Computer.IsPowerMonitorEnabled = v);
    }

    public bool IsControllerEnabled
    {
        get => Computer.IsControllerEnabled;
        set => SetHardwareEnabled("fanControllerMenuItem", value, v => Computer.IsControllerEnabled = v);
    }

    public bool IsStorageEnabled
    {
        get => Computer.IsStorageEnabled;
        set => SetHardwareEnabled("hddMenuItem", value, v => Computer.IsStorageEnabled = v);
    }

    public bool IsNetworkEnabled
    {
        get => Computer.IsNetworkEnabled;
        set => SetHardwareEnabled("nicMenuItem", value, v => Computer.IsNetworkEnabled = v);
    }

    public bool IsPsuEnabled
    {
        get => Computer.IsPsuEnabled;
        set => SetHardwareEnabled("psuMenuItem", value, v => Computer.IsPsuEnabled = v);
    }

    public bool IsBatteryEnabled
    {
        get => Computer.IsBatteryEnabled;
        set => SetHardwareEnabled("batteryMenuItem", value, v => Computer.IsBatteryEnabled = v);
    }

    public bool ForceDriveWakeup
    {
        get => Settings.GetValue("forceDriveWakeupItem", false);
        set
        {
            Settings.SetValue("forceDriveWakeupItem", value);
            foreach (StorageDevice storageDevice in Computer.Hardware.OfType<StorageDevice>())
                storageDevice.ForceWakeup = value;
        }
    }

    public void Open(bool raiseTreeRebuilt = true)
    {
        if (_isOpen)
            return;

        Computer.Open();
        _isOpen = true;
        RebuildTree(raiseTreeRebuilt);
    }

    public void Reset()
    {
        if (_isOpen)
            Computer.Close();

        Root = SensorTreeItemViewModel.CreateRoot(Environment.MachineName);
        Computer.Open();
        RebuildTree();
    }

    public Task UpdateAsync()
    {
        return Task.Run(() =>
        {
            lock (_updateLock)
                Computer.Accept(_updateVisitor);
        });
    }

    public void ResetMinMax()
    {
        SensorVisitor visitor = new(sensor => sensor.ResetMin());
        visitor.VisitComputer(Computer);
        visitor = new SensorVisitor(sensor => sensor.ResetMax());
        visitor.VisitComputer(Computer);
    }

    public void ClearSensorValues()
    {
        SensorVisitor visitor = new(sensor => sensor.ClearValues());
        visitor.VisitComputer(Computer);
    }

    public void RebuildTree(bool raiseTreeRebuilt = true)
    {
        SensorTreeItemViewModel root = SensorTreeItemViewModel.CreateRoot(Environment.MachineName);
        foreach (IHardware hardware in Computer.Hardware.OrderBy(hardware => hardware.HardwareType).ThenBy(hardware => hardware.Name, StringComparer.OrdinalIgnoreCase))
            root.Children.Add(SensorTreeItemViewModel.FromHardware(hardware, Settings));

        Root = root;
        if (raiseTreeRebuilt)
            TreeRebuilt?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Computer.HardwareAdded -= HardwareChanged;
        Computer.HardwareRemoved -= HardwareChanged;
        Computer.Close();
    }

    private void ApplyHardwareFlagsFromSettings()
    {
        Computer.IsMotherboardEnabled = Settings.GetValue("mainboardMenuItem", true);
        Computer.IsCpuEnabled = Settings.GetValue("cpuMenuItem", true);
        Computer.IsMemoryEnabled = Settings.GetValue("ramMenuItem", true);
        Computer.IsGpuEnabled = Settings.GetValue("gpuMenuItem", true);
        Computer.IsPowerMonitorEnabled = Settings.GetValue("powerMonitorMenuItem", true);
        Computer.IsControllerEnabled = Settings.GetValue("fanControllerMenuItem", true);
        Computer.IsStorageEnabled = Settings.GetValue("hddMenuItem", true);
        Computer.IsNetworkEnabled = Settings.GetValue("nicMenuItem", true);
        Computer.IsPsuEnabled = Settings.GetValue("psuMenuItem", true);
        Computer.IsBatteryEnabled = Settings.GetValue("batteryMenuItem", true);
        ForceDriveWakeup = Settings.GetValue("forceDriveWakeupItem", false);
    }

    private void SetHardwareEnabled(string settingName, bool value, Action<bool> setter)
    {
        setter(value);
        Settings.SetValue(settingName, value);
        if (_isOpen)
            RebuildTree();
    }

    private void HardwareChanged(IHardware hardware)
    {
        if (_isOpen)
            RebuildTree();
    }
}

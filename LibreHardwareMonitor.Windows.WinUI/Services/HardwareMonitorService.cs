// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

public sealed class HardwareMonitorService : IHardwareMonitorService
{
    private const string DeferDimmDetectionSetting = "memory.deferDimmDetection";
    private const string DeferCpuInitialUpdateSetting = "cpu.deferInitialUpdate";
    private const string DeferTscEstimationSetting = "cpu.deferTscEstimation";
    private const string DeferNetworkDetectionSetting = "network.deferDetection";
    private const string DeferNvidiaDetectionSetting = "nvidia.deferDetection";
    private const string DeferStorageDetectionSetting = "storage.deferDetection";
    private const string DeferIntelGpuDetectionSetting = "gpu.deferIntelDetection";
    private const string DeferControllerDetectionSetting = "controller.deferDetection";
    private const string DeferPsuDetectionSetting = "psu.deferDetection";

    private readonly object _updateLock = new();
    private readonly UpdateVisitor _updateVisitor = new();
    private readonly TreeRebuildCoalescer _treeRebuildCoalescer;
    private bool _isOpen;
    private bool _disposed;

    public HardwareMonitorService(AppSettings settings)
    {
        Settings = settings;
        _treeRebuildCoalescer = new TreeRebuildCoalescer(() => RebuildTree(), () => _isOpen);
        ApplyWinUiHardwareDefaults();
        Computer = new Computer(settings);
        Computer.HardwareAdded += HardwareChanged;
        Computer.HardwareRemoved += HardwareChanged;
        ApplyHardwareFlagsFromSettings();
    }

    public event EventHandler? TreeRebuilt;

    public Computer Computer { get; }

    /// <summary>
    /// Lock guarding access to live sensor collections (values and active-sensor sets). Held by <see cref="UpdateAsync" />
    /// while sensors are updated; other readers (tree rebuild, the remote web server) take it to read consistently.
    /// </summary>
    public object SensorReadLock => _updateLock;

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

    public async Task OpenAsync(bool raiseTreeRebuilt = true, CancellationToken cancellationToken = default)
    {
        if (_isOpen)
            return;

        await Computer.OpenAsync(cancellationToken).ConfigureAwait(false);
        _isOpen = true;
        RebuildTree(raiseTreeRebuilt);
        await Computer.HardwareDiscoveryTask.WaitAsync(cancellationToken).ConfigureAwait(false);
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

        // Building the tree reads each hardware's live sensor set (a HashSet). Hold the update lock so it cannot run
        // concurrently with UpdateAsync()'s Computer.Accept, which mutates that set via ActivateSensor on another thread.
        lock (_updateLock)
        {
            foreach (IHardware hardware in Computer.Hardware.OrderBy(hardware => hardware.HardwareType).ThenBy(hardware => hardware.Name, StringComparer.OrdinalIgnoreCase))
                root.Children.Add(SensorTreeItemViewModel.FromHardware(hardware, Settings));
        }

        Root = root;
        if (raiseTreeRebuilt)
            TreeRebuilt?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Clear before closing so any tree-rebuild task still pending its delay bails instead of rebuilding from a
        // half-closed Computer.
        _isOpen = false;
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

    private void ApplyWinUiHardwareDefaults()
    {
        if (!Settings.Contains(DeferDimmDetectionSetting))
            Settings.SetValue(DeferDimmDetectionSetting, true);

        if (!Settings.Contains(DeferCpuInitialUpdateSetting))
            Settings.SetValue(DeferCpuInitialUpdateSetting, true);

        if (!Settings.Contains(DeferTscEstimationSetting))
            Settings.SetValue(DeferTscEstimationSetting, true);

        if (!Settings.Contains(DeferNvidiaDetectionSetting))
            Settings.SetValue(DeferNvidiaDetectionSetting, true);

        if (!Settings.Contains(DeferStorageDetectionSetting))
            Settings.SetValue(DeferStorageDetectionSetting, true);

        if (!Settings.Contains(DeferNetworkDetectionSetting))
            Settings.SetValue(DeferNetworkDetectionSetting, true);

        if (!Settings.Contains(DeferIntelGpuDetectionSetting))
            Settings.SetValue(DeferIntelGpuDetectionSetting, true);

        if (!Settings.Contains(DeferControllerDetectionSetting))
            Settings.SetValue(DeferControllerDetectionSetting, true);

        if (!Settings.Contains(DeferPsuDetectionSetting))
            Settings.SetValue(DeferPsuDetectionSetting, true);
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
        // Storage discovery is deferred, so drives usually appear after ForceDriveWakeup was applied at startup (against
        // an empty hardware list). Apply the current setting to each newly discovered drive so it actually takes effect.
        if (hardware is StorageDevice storageDevice)
            storageDevice.ForceWakeup = Settings.GetValue("forceDriveWakeupItem", false);

        if (_isOpen)
            _treeRebuildCoalescer.Request();
    }

}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware.Battery;
using LibreHardwareMonitor.Hardware.Controller.AeroCool;
using LibreHardwareMonitor.Hardware.Controller.AquaComputer;
using LibreHardwareMonitor.Hardware.Controller.Arctic;
using LibreHardwareMonitor.Hardware.Controller.Heatmaster;
using LibreHardwareMonitor.Hardware.Controller.MSI;
using LibreHardwareMonitor.Hardware.Controller.Nzxt;
using LibreHardwareMonitor.Hardware.Controller.Razer;
using LibreHardwareMonitor.Hardware.Controller.TBalancer;
using LibreHardwareMonitor.Hardware.Cpu;
using LibreHardwareMonitor.Hardware.Gpu;
using LibreHardwareMonitor.Hardware.Memory;
using LibreHardwareMonitor.Hardware.Motherboard;
using LibreHardwareMonitor.Hardware.Network;
using LibreHardwareMonitor.Hardware.PowerMonitor;
using LibreHardwareMonitor.Hardware.Psu.Corsair;
using LibreHardwareMonitor.Hardware.Psu.Msi;
using LibreHardwareMonitor.Hardware.Storage;
using static LibreHardwareMonitor.Hardware.HardwareStartupTrace;

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// Stores all hardware groups and decides which devices should be enabled and updated.
/// </summary>
public class Computer : IComputer
{
    private const string DeferNetworkDetectionEnvironmentVariable = "LHM_NETWORK_DEFER_DETECTION";
    private const string DeferNvidiaDetectionEnvironmentVariable = "LHM_NVIDIA_DEFER_DETECTION";
    private const string DeferStorageDetectionEnvironmentVariable = "LHM_STORAGE_DEFER_DETECTION";
    private const string DeferIntelGpuDetectionEnvironmentVariable = "LHM_INTEL_GPU_DEFER_DETECTION";
    private const string DeferControllerDetectionEnvironmentVariable = "LHM_CONTROLLER_DEFER_DETECTION";
    private const string DeferPsuDetectionEnvironmentVariable = "LHM_PSU_DEFER_DETECTION";

    private readonly object _deferredGroupLock = new();
    private readonly List<IGroup> _groups = new();
    private readonly object _lock = new();
    private readonly ISettings _settings;

    private bool _batteryEnabled;
    private bool _controllerEnabled;
    private bool _cpuEnabled;
    private bool _gpuEnabled;
    private bool _powerMonitorEnabled;
    private bool _memoryEnabled;
    private bool _motherboardEnabled;
    private bool _networkEnabled;
    private bool _open;
    private bool _psuEnabled;
    private SMBios _smbios;
    private bool _storageEnabled;
    private CancellationTokenSource _deferredGroupCancellationTokenSource;
    private TaskCompletionSource<object> _deferredGroupCompletionSource = CreateCompletedTaskCompletionSource();
    private List<Task> _deferredGroupTasks = [];

    // Serializes the Open/OpenAsync/Close/Reset lifecycle so an asynchronous open cannot interleave with teardown
    // (which would let a background hardware probe outlive OpCode.Close()/Mutexes.Close()) and so two concurrent
    // opens cannot double-initialize. Also serializes every access to _deferredGroupCancellationTokenSource.
    private readonly object _openLock = new();

    /// <summary>
    /// Creates a new <see cref="IComputer" /> instance with basic initial <see cref="Settings" />.
    /// </summary>
    public Computer()
    {
        _settings = new Settings();
    }

    /// <summary>
    /// Creates a new <see cref="IComputer" /> instance with additional <see cref="ISettings" />.
    /// </summary>
    /// <param name="settings">Computer settings that will be transferred to each <see cref="IHardware" />.</param>
    public Computer(ISettings settings)
    {
        _settings = settings ?? new Settings();
    }

    /// <inheritdoc />
    public event HardwareEventHandler HardwareAdded;

    /// <inheritdoc />
    public event HardwareEventHandler HardwareRemoved;

    public Task HardwareDiscoveryTask
    {
        get
        {
            lock (_deferredGroupLock)
                return _deferredGroupCompletionSource.Task;
        }
    }

    /// <inheritdoc />
    public IList<IHardware> Hardware
    {
        get
        {
            lock (_lock)
            {
                List<IHardware> list = new();

                foreach (IGroup group in _groups)
                    list.AddRange(group.Hardware);

                return list;
            }
        }
    }

    /// <inheritdoc />
    public bool IsBatteryEnabled
    {
        get { return _batteryEnabled; }
        set
        {
            if (_open && value != _batteryEnabled)
            {
                if (value)
                {
                    Add(new BatteryGroup(_settings));
                }
                else
                {
                    RemoveType<BatteryGroup>();
                }
            }

            _batteryEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsControllerEnabled
    {
        get { return _controllerEnabled; }
        set
        {
            if (_open && value != _controllerEnabled)
            {
                if (value)
                {
                    Add(new TBalancerGroup(_settings));
                    Add(new HeatmasterGroup(_settings));
                    Add(new AquaComputerGroup(_settings));
                    Add(new AeroCoolGroup(_settings));
                    Add(new NzxtGroup(_settings));
                    Add(new RazerGroup(_settings));
                    Add(new ArcticGroup(_settings));
                    Add(new MsiGroup(_settings));
                }
                else
                {
                    RemoveType<TBalancerGroup>();
                    RemoveType<HeatmasterGroup>();
                    RemoveType<AquaComputerGroup>();
                    RemoveType<AeroCoolGroup>();
                    RemoveType<NzxtGroup>();
                    RemoveType<RazerGroup>();
                    RemoveType<ArcticGroup>();
                    RemoveType<MsiGroup>();
                }
            }

            _controllerEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsCpuEnabled
    {
        get { return _cpuEnabled; }
        set
        {
            if (_open && value != _cpuEnabled)
            {
                if (value)
                    Add(new CpuGroup(_settings));
                else
                    RemoveType<CpuGroup>();
            }

            _cpuEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsGpuEnabled
    {
        get { return _gpuEnabled; }
        set
        {
            if (_open && value != _gpuEnabled)
            {
                if (value)
                {
                    Add(new AmdGpuGroup(_settings));
                    Add(new NvidiaGroup(_settings));

                    if (_cpuEnabled)
                        Add(new IntelGpuGroup(GetIntelCpus(), _settings));
                }
                else
                {
                    RemoveType<AmdGpuGroup>();
                    RemoveType<NvidiaGroup>();
                    RemoveType<IntelGpuGroup>();
                }
            }

            _gpuEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsPowerMonitorEnabled
    {
        get { return _powerMonitorEnabled; }
        set
        {
            if (_open && value != _powerMonitorEnabled)
            {
                if (value)
                    Add(new PowerMonitorGroup(_settings));
                else
                    RemoveType<PowerMonitorGroup>();
            }

            _powerMonitorEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsMemoryEnabled
    {
        get { return _memoryEnabled; }
        set
        {
            if (_open && value != _memoryEnabled)
            {
                if (value)
                    Add(new MemoryGroup(_settings));
                else
                    RemoveType<MemoryGroup>();
            }

            _memoryEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsMotherboardEnabled
    {
        get { return _motherboardEnabled; }
        set
        {
            if (_open && value != _motherboardEnabled)
            {
                if (value)
                    Add(new MotherboardGroup(_smbios, _settings));
                else
                    RemoveType<MotherboardGroup>();
            }

            _motherboardEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsNetworkEnabled
    {
        get { return _networkEnabled; }
        set
        {
            if (_open && value != _networkEnabled)
            {
                if (value)
                    Add(new NetworkGroup(_settings));
                else
                    RemoveType<NetworkGroup>();
            }

            _networkEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsPsuEnabled
    {
        get { return _psuEnabled; }
        set
        {
            if (_open && value != _psuEnabled)
            {
                if (value)
                {
                    Add(new CorsairPsuGroup(_settings));
                    Add(new MsiPsuGroup(_settings));
                }
                else
                {
                    RemoveType<CorsairPsuGroup>();
                    RemoveType<MsiPsuGroup>();
                }
            }

            _psuEnabled = value;
        }
    }

    /// <inheritdoc />
    public bool IsStorageEnabled
    {
        get { return _storageEnabled; }
        set
        {
            if (_open && value != _storageEnabled)
            {
                if (value)
                    Add(new StorageGroup(_settings));
                else
                    RemoveType<StorageGroup>();
            }

            _storageEnabled = value;
        }
    }

    /// <summary>
    /// Contains computer information table read in accordance with <see href="https://www.dmtf.org/standards/smbios">System Management BIOS (SMBIOS) Reference Specification</see>.
    /// </summary>
    public SMBios SMBios
    {
        get
        {
            if (!_open)
                throw new InvalidOperationException("SMBIOS cannot be accessed before opening.");

            return _smbios;
        }
    }

    //// <inheritdoc />
    public string GetReport()
    {
        lock (_lock)
        {
            using StringWriter w = new(CultureInfo.InvariantCulture);

            w.WriteLine();
            w.WriteLine(nameof(LibreHardwareMonitor) + " Report");
            w.WriteLine();

            Version version = typeof(Computer).Assembly.GetName().Version;

            NewSection(w);
            w.Write("Version: ");
            w.WriteLine(version.ToString());
            w.WriteLine();

            NewSection(w);
            w.Write("Common Language Runtime: ");
            w.WriteLine(Environment.Version.ToString());
            w.Write("Operating System: ");
            w.WriteLine(Environment.OSVersion.ToString());
            w.Write("Process Type: ");
            w.WriteLine(IntPtr.Size == 4 ? "32-Bit" : "64-Bit");
            w.WriteLine();

            NewSection(w);
            w.WriteLine("Sensors");
            w.WriteLine();

            foreach (IGroup group in _groups)
            {
                foreach (IHardware hardware in group.Hardware)
                    ReportHardwareSensorTree(hardware, w, string.Empty);
            }

            w.WriteLine();

            NewSection(w);
            w.WriteLine("Parameters");
            w.WriteLine();

            foreach (IGroup group in _groups)
            {
                foreach (IHardware hardware in group.Hardware)
                    ReportHardwareParameterTree(hardware, w, string.Empty);
            }

            w.WriteLine();

            foreach (IGroup group in _groups)
            {
                string report = group.GetReport();
                if (!string.IsNullOrEmpty(report))
                {
                    NewSection(w);
                    w.Write(report);
                }

                foreach (IHardware hardware in group.Hardware)
                    ReportHardware(hardware, w);
            }

            return w.ToString();
        }
    }

    /// <summary>
    /// Triggers the <see cref="IVisitor.VisitComputer" /> method for the given observer.
    /// </summary>
    /// <param name="visitor">Observer who call to devices.</param>
    public void Accept(IVisitor visitor)
    {
        if (visitor == null)
            throw new ArgumentNullException(nameof(visitor));

        visitor.VisitComputer(this);
    }

    /// <summary>
    /// Triggers the <see cref="IElement.Accept" /> method with the given visitor for each device in each group.
    /// </summary>
    /// <param name="visitor">Observer who call to devices.</param>
    public void Traverse(IVisitor visitor)
    {
        lock (_lock)
        {
            // Use a for-loop instead of foreach to avoid a collection modified exception after sleep, even though everything is under a lock.
            for (int i = 0; i < _groups.Count; i++)
            {
                IGroup group = _groups[i];

                for (int j = 0; j < group.Hardware.Count; j++)
                    group.Hardware[j].Accept(visitor);
            }
        }
    }

    private void HardwareAddedEvent(IHardware hardware)
    {
        HardwareAdded?.Invoke(hardware);
    }

    private void HardwareRemovedEvent(IHardware hardware)
    {
        HardwareRemoved?.Invoke(hardware);
    }

    private void Add(IGroup group)
    {
        AddCore(group, CancellationToken.None, null);
    }

    /// <summary>
    /// Inserts <paramref name="group" /> while holding <see cref="_lock" />. The deferred discovery paths pass the run's
    /// <paramref name="cancellationToken" /> and <paramref name="isEnabled" /> so the final eligibility check and the
    /// membership mutation are atomic with respect to <see cref="Close" />'s drain: a background task can therefore never
    /// add (and leak) a group after the computer has already been torn down. Returns whether the group was added.
    /// </summary>
    private bool AddCore(IGroup group, CancellationToken cancellationToken, Func<bool> isEnabled)
    {
        if (group == null)
            return false;

        bool added = false;
        IHardware[] initialHardware = null;
        lock (_lock)
        {
            if (!cancellationToken.IsCancellationRequested && (isEnabled == null || isEnabled()) && !_groups.Contains(group))
            {
                _groups.Add(group);

                if (group is IHardwareChanged hardwareChanged)
                {
                    hardwareChanged.HardwareAdded += HardwareAddedEvent;
                    hardwareChanged.HardwareRemoved += HardwareRemovedEvent;
                }

                // Snapshot the hardware present at subscription time. Capturing it here -- before any background
                // discovery is started below -- guarantees hardware discovered later is announced exactly once, via the
                // event we just subscribed: never missed (subscription precedes discovery) and never also replayed from
                // this snapshot (a duplicate).
                initialHardware = [.. group.Hardware];
                added = true;
            }
        }

        if (added)
        {
            // Start background discovery only now that the subscription and snapshot are in place, then track its task
            // so the run's completion (HardwareDiscoveryTask) waits for it.
            if (group is IHardwareDiscoveryTask discoveryTask)
            {
                discoveryTask.StartHardwareDiscovery();
                TrackGroupHardwareDiscoveryTask(group);
            }

            if (HardwareAdded != null)
            {
                foreach (IHardware hardware in initialHardware)
                    HardwareAdded(hardware);
            }
        }
        else if (isEnabled != null)
        {
            // Only the deferred path owns the group's lifetime here; if we declined to add it (run cancelled or the
            // category was disabled meanwhile), close it so its USB/HID/serial handles are not leaked.
            group.Close();
        }

        return added;
    }

    private void Remove(IGroup group)
    {
        lock (_lock)
        {
            if (!_groups.Contains(group))
                return;

            _groups.Remove(group);

            if (group is IHardwareChanged hardwareChanged)
            {
                hardwareChanged.HardwareAdded -= HardwareAddedEvent;
                hardwareChanged.HardwareRemoved -= HardwareRemovedEvent;
            }
        }

        if (HardwareRemoved != null)
        {
            foreach (IHardware hardware in group.Hardware)
                HardwareRemoved(hardware);
        }

        group.Close();
    }

    private void RemoveType<T>() where T : IGroup
    {
        List<T> list = [];

        lock (_lock)
        {
            foreach (IGroup group in _groups)
            {
                if (group is T t)
                    list.Add(t);
            }
        }

        foreach (T group in list)
            Remove(group);
    }

    /// <summary>
    /// If hasn't been opened before, opens <see cref="SMBios" />, <see cref="OpCode" /> and triggers the private <see cref="AddGroups" /> method depending on which categories are
    /// enabled.
    /// </summary>
    public void Open()
    {
        OpenInternal(CancellationToken.None);
    }

    /// <summary>
    /// Asynchronously performs the same work as <see cref="Open()" />. The required global setup (SMBIOS, mutexes and
    /// <see cref="OpCode" />) and hardware group discovery run on a background thread; discovery order and behavior
    /// match <see cref="Open()" />.
    /// </summary>
    /// <returns>A task that completes once all enabled hardware groups have been discovered.</returns>
    public Task OpenAsync()
    {
        return OpenAsync(CancellationToken.None);
    }

    /// <summary>
    /// Asynchronously performs the same work as <see cref="Open()" />, observing the supplied <paramref name="cancellationToken" />
    /// between initialization phases.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel initialization before it completes.</param>
    /// <returns>A task that completes once all enabled hardware groups have been discovered.</returns>
    public Task OpenAsync(CancellationToken cancellationToken)
    {
        lock (_openLock)
        {
            if (_open)
                return Task.CompletedTask;
        }

        return Task.Run(() => OpenInternal(cancellationToken), cancellationToken);
    }

    private void OpenInternal(CancellationToken cancellationToken)
    {
        // Hold _openLock for the whole open so a concurrent Close()/Reset() cannot run mid-initialization and so a
        // second Open()/OpenAsync() observes _open atomically instead of double-initializing OpCode/Mutexes/groups.
        lock (_openLock)
        {
            if (_open)
                return;

            OpenCore(cancellationToken);
        }
    }

    private void OpenCore(CancellationToken cancellationToken)
    {
        StartDeferredGroupRun();

        try
        {
            using HardwareStartupTrace startupTrace = HardwareStartupTrace.Create(_settings);

            cancellationToken.ThrowIfCancellationRequested();
            _smbios = Measure(startupTrace, "SMBios", () => new SMBios());

            if (!Software.OperatingSystem.IsUnix)
                Measure(startupTrace, "Mutexes.Open", Mutexes.Open);
            else
                startupTrace?.Skip("Mutexes.Open", "Operating system is not Windows.");

            cancellationToken.ThrowIfCancellationRequested();
            Measure(startupTrace, "OpCode.Open", OpCode.Open);

            AddGroups(startupTrace, cancellationToken);
            CompleteDeferredGroupRunWhenRegistered();

            _open = true;
        }
        catch
        {
            CancelDeferredGroupRun();
            throw;
        }
    }

    private void AddGroups(HardwareStartupTrace startupTrace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_motherboardEnabled)
            AddMeasuredGroup(startupTrace, "MotherboardGroup", () => new MotherboardGroup(_smbios, _settings));

        cancellationToken.ThrowIfCancellationRequested();
        if (_cpuEnabled)
            AddMeasuredGroup(startupTrace, "CpuGroup", () => new CpuGroup(_settings, startupTrace));

        cancellationToken.ThrowIfCancellationRequested();
        if (_memoryEnabled)
            AddMeasuredGroup(startupTrace, "MemoryGroup", () => new MemoryGroup(_settings, startupTrace));

        cancellationToken.ThrowIfCancellationRequested();
        if (_gpuEnabled)
        {
            AddMeasuredGroup(startupTrace, "AmdGpuGroup", () => new AmdGpuGroup(_settings));
            AddMeasuredGroup(startupTrace,
                             "NvidiaGroup",
                             () => new NvidiaGroup(_settings),
                             () => _gpuEnabled,
                             HardwareSettingsKeys.NvidiaDeferDetection,
                             DeferNvidiaDetectionEnvironmentVariable);

            // Intel GPU detection is the most expensive GPU probe but only depends on the (already-created) CPU group,
            // so it can be deferred to the background like the other GPU/storage/network groups.
            if (_cpuEnabled)
                AddMeasuredGroup(startupTrace,
                                 "IntelGpuGroup",
                                 () => new IntelGpuGroup(GetIntelCpus(), _settings),
                                 () => _gpuEnabled && _cpuEnabled,
                                 HardwareSettingsKeys.IntelGpuDeferDetection,
                                 DeferIntelGpuDetectionEnvironmentVariable);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_powerMonitorEnabled)
            AddMeasuredGroup(startupTrace, "PowerMonitorGroup", () => new PowerMonitorGroup(_settings));

        cancellationToken.ThrowIfCancellationRequested();
        if (_controllerEnabled)
        {
            // Controllers probe USB/serial buses (with worst-case timeouts), so the whole block can be deferred to a
            // single background task. It stays sequential there to avoid concurrent serial-port/USB scans.
            if (ShouldDeferDetection(HardwareSettingsKeys.ControllerDeferDetection, DeferControllerDetectionEnvironmentVariable))
            {
                startupTrace?.Skip("ControllerGroups", "Deferred to background.");
                AddDeferredGroups(() => _controllerEnabled,
                                  () => new TBalancerGroup(_settings),
                                  () => new HeatmasterGroup(_settings),
                                  () => new AquaComputerGroup(_settings),
                                  () => new AeroCoolGroup(_settings),
                                  () => new NzxtGroup(_settings),
                                  () => new RazerGroup(_settings),
                                  () => new ArcticGroup(_settings),
                                  () => new MsiGroup(_settings));
            }
            else
            {
                AddMeasuredGroup(startupTrace, "TBalancerGroup", () => new TBalancerGroup(_settings));
                AddMeasuredGroup(startupTrace, "HeatmasterGroup", () => new HeatmasterGroup(_settings));
                AddMeasuredGroup(startupTrace, "AquaComputerGroup", () => new AquaComputerGroup(_settings));
                AddMeasuredGroup(startupTrace, "AeroCoolGroup", () => new AeroCoolGroup(_settings));
                AddMeasuredGroup(startupTrace, "NzxtGroup", () => new NzxtGroup(_settings));
                AddMeasuredGroup(startupTrace, "RazerGroup", () => new RazerGroup(_settings));
                AddMeasuredGroup(startupTrace, "ArcticGroup", () => new ArcticGroup(_settings));
                AddMeasuredGroup(startupTrace, "MsiGroup", () => new MsiGroup(_settings));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_storageEnabled)
            AddMeasuredGroup(startupTrace,
                             "StorageGroup",
                             () => new StorageGroup(_settings),
                             () => _storageEnabled,
                             HardwareSettingsKeys.StorageDeferDetection,
                             DeferStorageDetectionEnvironmentVariable);

        cancellationToken.ThrowIfCancellationRequested();
        if (_networkEnabled)
            AddMeasuredGroup(startupTrace,
                             "NetworkGroup",
                             () => new NetworkGroup(_settings),
                             () => _networkEnabled,
                             HardwareSettingsKeys.NetworkDeferDetection,
                             DeferNetworkDetectionEnvironmentVariable);

        cancellationToken.ThrowIfCancellationRequested();
        if (_psuEnabled)
        {
            // PSU detection probes HID devices, so it contends with the deferred controllers' USB/HID scans. Defer it
            // to a background task too, keeping all HID/USB probing off the critical path.
            if (ShouldDeferDetection(HardwareSettingsKeys.PsuDeferDetection, DeferPsuDetectionEnvironmentVariable))
            {
                startupTrace?.Skip("PsuGroups", "Deferred to background.");
                AddDeferredGroups(() => _psuEnabled,
                                  () => new CorsairPsuGroup(_settings),
                                  () => new MsiPsuGroup(_settings));
            }
            else
            {
                AddMeasuredGroup(startupTrace, "CorsairPsuGroup", () => new CorsairPsuGroup(_settings));
                AddMeasuredGroup(startupTrace, "MsiPsuGroup", () => new MsiPsuGroup(_settings));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_batteryEnabled)
            AddMeasuredGroup(startupTrace, "BatteryGroup", () => new BatteryGroup(_settings));
    }

    private void AddMeasuredGroup(HardwareStartupTrace startupTrace, string phase, Func<IGroup> createGroup)
    {
        Add(Measure(startupTrace, phase, createGroup));
    }

    private void AddMeasuredGroup(HardwareStartupTrace startupTrace, string phase, Func<IGroup> createGroup, Func<bool> isEnabled, string deferSettingName, string deferEnvironmentVariable)
    {
        if (!ShouldDeferDetection(deferSettingName, deferEnvironmentVariable))
        {
            AddMeasuredGroup(startupTrace, phase, createGroup);
            return;
        }

        startupTrace?.Skip(phase, "Deferred to background.");
        AddDeferredGroup(createGroup, isEnabled);
    }

    private void AddDeferredGroup(Func<IGroup> createGroup, Func<bool> isEnabled)
    {
        // StartDeferredGroupRun (run under _openLock before AddGroups) always assigns the token source, so the deferred
        // path is the only reachable one here.
        CancellationToken cancellationToken = _deferredGroupCancellationTokenSource.Token;
        Task task = Task.Run(() =>
        {
            IGroup group = null;
            try
            {
                group = createGroup();
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                    System.Diagnostics.Debug.WriteLine($"Deferred hardware detection failed: {ex}");

                return;
            }

            if (group == null)
                return;

            AddCore(group, cancellationToken, isEnabled);
        });
        TrackDeferredGroupTask(task);
    }

    private void AddDeferredGroups(Func<bool> isEnabled, params Func<IGroup>[] createGroups)
    {
        CancellationToken cancellationToken = _deferredGroupCancellationTokenSource.Token;
        Task task = Task.Run(() =>
        {
            foreach (Func<IGroup> createGroup in createGroups)
            {
                if (cancellationToken.IsCancellationRequested || !isEnabled())
                    return;

                IGroup group = null;
                try
                {
                    group = createGroup();
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        System.Diagnostics.Debug.WriteLine($"Deferred hardware detection failed: {ex}");

                    continue;
                }

                if (group == null)
                    continue;

                // Stop the sequential block if the run was cancelled or the category was disabled while we were probing.
                if (!AddCore(group, cancellationToken, isEnabled))
                    return;
            }
        });
        TrackDeferredGroupTask(task);
    }

    private bool ShouldDeferDetection(string settingName, string environmentVariable)
    {
        return SettingsParsing.ShouldDefer(_settings, settingName, environmentVariable);
    }

    private void StartDeferredGroupRun()
    {
        CancelDeferredGroupRun();
        lock (_deferredGroupLock)
        {
            _deferredGroupCompletionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _deferredGroupTasks = [];
        }

        _deferredGroupCancellationTokenSource = new CancellationTokenSource();
    }

    private void CancelDeferredGroupRun()
    {
        CancellationTokenSource cancellationTokenSource = _deferredGroupCancellationTokenSource;
        _deferredGroupCancellationTokenSource = null;
        TaskCompletionSource<object> completionSource;
        Task[] tasks;

        lock (_deferredGroupLock)
        {
            completionSource = _deferredGroupCompletionSource;
            tasks = _deferredGroupTasks.ToArray();
            _deferredGroupTasks = [];
        }

        if (cancellationTokenSource == null)
        {
            completionSource.TrySetCanceled();
            return;
        }

        cancellationTokenSource.Cancel();

        // Mark the run cancelled before draining so the Task.WhenAll continuation in CompleteDeferredGroupRunWhenRegistered
        // cannot win the race and complete HardwareDiscoveryTask during teardown.
        completionSource.TrySetCanceled();

        // Wait for in-flight deferred construction to unwind before disposing the token source or returning to a caller
        // that is about to tear down native state. A deferred task only checks the token before createGroup() and again
        // in AddCore, so the group constructor (which probes hardware via OpCode/native APIs) always runs to completion;
        // draining here keeps it from racing OpCode.Close()/Mutexes.Close() in Close() or a fresh run from Reset()/Open().
        WaitForDeferredGroupTasks(tasks);

        cancellationTokenSource.Dispose();
    }

    private static void WaitForDeferredGroupTasks(Task[] tasks)
    {
        if (tasks.Length == 0)
            return;

        try
        {
            Task.WaitAll(tasks);
        }
        catch (AggregateException)
        {
            // Deferred discovery tasks observe and log their own exceptions; nothing actionable surfaces here.
        }
    }

    private static TaskCompletionSource<object> CreateCompletedTaskCompletionSource()
    {
        TaskCompletionSource<object> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        completionSource.SetResult(null);
        return completionSource;
    }

    private void CompleteDeferredGroupRun(TaskCompletionSource<object> completionSource)
    {
        lock (_deferredGroupLock)
        {
            if (!ReferenceEquals(completionSource, _deferredGroupCompletionSource))
                return;

            completionSource.TrySetResult(null);
        }
    }

    private void CompleteDeferredGroupRunWhenRegistered()
    {
        TaskCompletionSource<object> completionSource;
        lock (_deferredGroupLock)
            completionSource = _deferredGroupCompletionSource;

        WaitForRegisteredTasksThenComplete(completionSource, 0);
    }

    /// <summary>
    /// Completes the run once every tracked deferred task has finished. A deferred group that is itself an
    /// <see cref="IHardwareDiscoveryTask" /> registers its nested task from a background thread, so more tasks can
    /// appear after the first <c>Task.WhenAll</c>; this re-arms on the larger set (comparing against the count
    /// already awaited) until no new task has been registered, so completion never fires while discovery is still running.
    /// </summary>
    private void WaitForRegisteredTasksThenComplete(TaskCompletionSource<object> completionSource, int alreadyAwaited)
    {
        Task[] tasks;
        lock (_deferredGroupLock)
        {
            // A superseding run (Reset()/Close() started a new one) owns completion now; stop.
            if (!ReferenceEquals(completionSource, _deferredGroupCompletionSource))
                return;

            tasks = _deferredGroupTasks.ToArray();
        }

        if (tasks.Length <= alreadyAwaited)
        {
            CompleteDeferredGroupRun(completionSource);
            return;
        }

        _ = Task.WhenAll(tasks).ContinueWith(_ => WaitForRegisteredTasksThenComplete(completionSource, tasks.Length),
                                             CancellationToken.None,
                                             TaskContinuationOptions.ExecuteSynchronously,
                                             TaskScheduler.Default);
    }

    private void TrackDeferredGroupTask(Task task)
    {
        lock (_deferredGroupLock)
            _deferredGroupTasks.Add(task);
    }

    private void TrackGroupHardwareDiscoveryTask(IGroup group)
    {
        if (group is not IHardwareDiscoveryTask hardwareDiscoveryTask || hardwareDiscoveryTask.HardwareDiscoveryTask.IsCompleted)
            return;

        TrackDeferredGroupTask(hardwareDiscoveryTask.HardwareDiscoveryTask);
    }

    private static void NewSection(TextWriter writer)
    {
        for (int i = 0; i < 8; i++)
            writer.Write("----------");

        writer.WriteLine();
        writer.WriteLine();
    }

    private static int CompareSensor(ISensor a, ISensor b)
    {
        int c = a.SensorType.CompareTo(b.SensorType);
        if (c == 0)
            return a.Index.CompareTo(b.Index);

        return c;
    }

    private static void ReportHardwareSensorTree(IHardware hardware, TextWriter w, string space)
    {
        w.WriteLine("{0}|", space);
        w.WriteLine("{0}+- {1} ({2})", space, hardware.Name, hardware.Identifier);

        ISensor[] sensors = hardware.Sensors;
        Array.Sort(sensors, CompareSensor);

        foreach (ISensor sensor in sensors)
            w.WriteLine("{0}|  +- {1,-14} : {2,8:G6} {3,8:G6} {4,8:G6} ({5})", space, sensor.Name, sensor.Value, sensor.Min, sensor.Max, sensor.Identifier);

        foreach (IHardware subHardware in hardware.SubHardware)
            ReportHardwareSensorTree(subHardware, w, "|  ");
    }

    private static void ReportHardwareParameterTree(IHardware hardware, TextWriter w, string space)
    {
        w.WriteLine("{0}|", space);
        w.WriteLine("{0}+- {1} ({2})", space, hardware.Name, hardware.Identifier);

        ISensor[] sensors = hardware.Sensors;
        Array.Sort(sensors, CompareSensor);

        foreach (ISensor sensor in sensors)
        {
            string innerSpace = space + "|  ";
            if (sensor.Parameters.Count > 0)
            {
                w.WriteLine("{0}|", innerSpace);
                w.WriteLine("{0}+- {1} ({2})", innerSpace, sensor.Name, sensor.Identifier);

                foreach (IParameter parameter in sensor.Parameters)
                {
                    string innerInnerSpace = innerSpace + "|  ";
                    w.WriteLine("{0}+- {1} : {2}", innerInnerSpace, parameter.Name, string.Format(CultureInfo.InvariantCulture, "{0} : {1}", parameter.DefaultValue, parameter.Value));
                }
            }
        }

        foreach (IHardware subHardware in hardware.SubHardware)
            ReportHardwareParameterTree(subHardware, w, "|  ");
    }

    private static void ReportHardware(IHardware hardware, TextWriter w)
    {
        string hardwareReport = hardware.GetReport();
        if (!string.IsNullOrEmpty(hardwareReport))
        {
            NewSection(w);
            w.Write(hardwareReport);
        }

        foreach (IHardware subHardware in hardware.SubHardware)
            ReportHardware(subHardware, w);
    }

    /// <summary>
    /// If opened before, removes all <see cref="IGroup" /> and triggers <see cref="OpCode.Close" />.
    /// </summary>
    public void Close()
    {
        lock (_openLock)
        {
            if (!_open)
                return;

            // Cancel and DRAIN deferred discovery before tearing down native state: a deferred group constructor runs
            // to completion even after cancellation, so without waiting here a background probe could run concurrently
            // with OpCode.Close()/Mutexes.Close() and fault in native code.
            CancelDeferredGroupRun();

            lock (_lock)
            {
                while (_groups.Count > 0)
                {
                    IGroup group = _groups[_groups.Count - 1];
                    Remove(group);
                }
            }

            OpCode.Close();
            Mutexes.Close();

            _smbios = null;
            _open = false;
        }
    }

    /// <summary>
    /// If opened before, removes all <see cref="IGroup" /> and recreates it.
    /// </summary>
    public void Reset()
    {
        lock (_openLock)
        {
            if (!_open)
                return;

            StartDeferredGroupRun();
            RemoveGroups();
            AddGroups(null, CancellationToken.None);
            CompleteDeferredGroupRunWhenRegistered();
        }
    }

    private void RemoveGroups()
    {
        lock (_lock)
        {
            while (_groups.Count > 0)
            {
                IGroup group = _groups[_groups.Count - 1];
                Remove(group);
            }
        }
    }

    private List<IntelCpu> GetIntelCpus()
    {
        // Create a temporary cpu group if one has not been added.
        lock (_lock)
        {
            IGroup cpuGroup = _groups.Find(x => x is CpuGroup) ?? new CpuGroup(_settings);
            return cpuGroup.Hardware.Select(x => x as IntelCpu).ToList();
        }
    }

    /// <summary>
    /// <see cref="Computer" /> specific additional settings passed to its <see cref="IHardware" />.
    /// </summary>
    private class Settings : ISettings
    {
        public bool Contains(string name)
        {
            return false;
        }

        public void SetValue(string name, string value)
        { }

        public string GetValue(string name, string value)
        {
            return value;
        }

        public void Remove(string name)
        { }
    }
}

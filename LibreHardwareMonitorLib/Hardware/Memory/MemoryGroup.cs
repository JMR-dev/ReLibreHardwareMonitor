// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RAMSPDToolkit.I2CSMBus;
using RAMSPDToolkit.SPD;
using RAMSPDToolkit.SPD.Enums;
using RAMSPDToolkit.SPD.Interop.Shared;
using RAMSPDToolkit.Windows.Driver;
using static LibreHardwareMonitor.Hardware.HardwareStartupTrace;

namespace LibreHardwareMonitor.Hardware.Memory;

internal class MemoryGroup : IGroup, IHardwareChanged, IHardwareDiscoveryTask
{
    private const string DeferDimmDetectionEnvironmentVariable = "LHM_MEMORY_DEFER_DIMM_DETECTION";
    private static readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(2.5);
    private static readonly object _lock = new();
    private List<Hardware> _hardware = [];

    private CancellationTokenSource _cancellationTokenSource;
    private Task _hardwareDiscoveryTask = Task.CompletedTask;
    private Exception _lastException;
    private bool _disposed = false;
    private readonly ISettings _settings;
    private bool _hasPendingDimmDetection;
    private TimeSpan _pendingDimmDetectionDelay;

    public MemoryGroup(ISettings settings)
        : this(settings, null)
    { }

    internal MemoryGroup(ISettings settings, HardwareStartupTrace startupTrace)
    {
        _settings = settings;

        Measure(startupTrace, "MemoryGroup.Driver.Configure", () =>
        {
            if (DriverManager.Driver is null || !DriverManager.Driver.IsOpen)
            {
                // Assign implementation of IDriver.
                DriverManager.Driver = new RAMSPDToolkitDriver();
                SMBusManager.UseWMI = false;
            }
        });

        _hardware.Add(Measure(startupTrace, "MemoryGroup.VirtualMemory", () => new VirtualMemory(settings)));
        _hardware.Add(Measure(startupTrace, "MemoryGroup.TotalMemory", () => new TotalMemory(settings)));

        if (!Measure(startupTrace, "MemoryGroup.Driver.LoadDriver", () => DriverManager.Driver != null && DriverManager.LoadDriver(), loaded => loaded ? "Loaded" : "Unavailable"))
        {
            return;
        }

        if (ShouldDeferDimmDetection(settings))
        {
            // Defer the start to StartHardwareDiscovery so Computer subscribes before any DIMM is announced.
            startupTrace?.Skip("MemoryGroup.DimmDetection", "Deferred to background.");
            _pendingDimmDetectionDelay = TimeSpan.Zero;
            _hasPendingDimmDetection = true;
        }
        else if (!TryAddDimms(settings, startupTrace))
        {
            // Synchronous detection found nothing yet; retry in the background, also started from StartHardwareDiscovery.
            _pendingDimmDetectionDelay = _retryInterval;
            _hasPendingDimmDetection = true;
        }
    }

#pragma warning disable 67
    public event HardwareEventHandler HardwareAdded;
    public event HardwareEventHandler HardwareRemoved;
#pragma warning restore 67

    public IReadOnlyList<IHardware> Hardware => _hardware;

    public Task HardwareDiscoveryTask => _hardwareDiscoveryTask;

    public void StartHardwareDiscovery()
    {
        if (!_hasPendingDimmDetection)
            return;

        _hasPendingDimmDetection = false;
        StartDimmDetectionTask(_settings, _pendingDimmDetectionDelay);
    }

    public string GetReport()
    {
        StringBuilder report = new();
        report.AppendLine("Memory Report:");
        if (_lastException != null)
        {
            report.AppendLine($"Error while detecting memory: {_lastException.Message}");
        }

        foreach (Hardware hardware in _hardware)
        {
            report.AppendLine($"{hardware.Name} ({hardware.Identifier}):");
            report.AppendLine();
            foreach (ISensor sensor in hardware.Sensors)
            {
                report.AppendLine($"{sensor.Name}: {sensor.Value?.ToString() ?? "No value"}");
            }
        }

        return report.ToString();
    }

    public void Close()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        lock (_lock)
        {
            foreach (Hardware ram in _hardware)
                ram.Close();

            _hardware = [];
            _disposed = true;
        }
    }

    private bool TryAddDimms(ISettings settings)
    {
        return TryAddDimms(settings, null);
    }

    private bool TryAddDimms(ISettings settings, HardwareStartupTrace startupTrace)
    {
        try
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return false;
                }

                List<SPDAccessor> accessors = [];
                bool detected = Measure(startupTrace,
                                        "MemoryGroup.DetectThermalSensors",
                                        () => DetectThermalSensors(out accessors, startupTrace),
                                        result => result ? $"{accessors.Count} DIMM accessor(s)" : "No DIMM accessors");
                if (detected)
                {
                    Measure(startupTrace, "MemoryGroup.AddDimms", () => AddDimms(accessors, settings, startupTrace));
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _lastException = ex;
            Debug.Assert(false, "Exception while detecting RAM: " + ex.Message);
        }

        return false;
    }

    private void StartDimmDetectionTask(ISettings settings, TimeSpan initialDelay)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = _cancellationTokenSource.Token;

        _hardwareDiscoveryTask = Task.Run(async () =>
        {
            try
            {
                int retryRemaining = 5;
                TimeSpan delay = initialDelay;

                while (!cancellationToken.IsCancellationRequested && retryRemaining-- > 0)
                {
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                    if (TryAddDimms(settings))
                        break;

                    delay = _retryInterval;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }, cancellationToken);
    }

    private static bool DetectThermalSensors(out List<SPDAccessor> accessors)
    {
        return DetectThermalSensors(out accessors, null);
    }

    private static bool DetectThermalSensors(out List<SPDAccessor> accessors, HardwareStartupTrace startupTrace)
    {
        accessors = [];

        bool ramDetected = false;

        Measure(startupTrace, "MemoryGroup.SMBus.DetectSMBuses", SMBusManager.DetectSMBuses);

        //Go through detected SMBuses
        int busIndex = 0;
        foreach (SMBusInterface smbus in SMBusManager.RegisteredSMBuses)
        {
            string busName = $"{smbus.GetType().Name}[{busIndex}]";

            //Go through possible RAM slots
            for (byte i = SPDConstants.SPD_BEGIN; i <= SPDConstants.SPD_END; ++i)
            {
                //Detect type of RAM, if available
                string address = "0x" + i.ToString("X2", CultureInfo.InvariantCulture);
                SPDDetector detector = Measure(startupTrace,
                                               $"MemoryGroup.SPDDetector.{busName}.{address}",
                                               () => new SPDDetector(smbus, i),
                                               result => result.Accessor != null ? $"Detected {result.Accessor.GetType().Name} index {result.Accessor.Index}" : "No RAM");

                //RAM available and detected
                if (detector.Accessor != null)
                {
                    //Add all detected modules
                    accessors.Add(detector.Accessor);

                    ramDetected = true;
                }
            }

            busIndex++;
        }

        return ramDetected;
    }

    private static bool ShouldDeferDimmDetection(ISettings settings)
    {
        return SettingsParsing.ShouldDefer(settings, HardwareSettingsKeys.MemoryDeferDimmDetection, DeferDimmDetectionEnvironmentVariable);
    }

    private void AddDimms(List<SPDAccessor> accessors, ISettings settings, HardwareStartupTrace startupTrace)
    {
        List<Hardware> additions = [];

        foreach (SPDAccessor ram in accessors)
        {
            //Default value
            string name = $"DIMM #{ram.Index}";
            string phasePrefix = $"MemoryGroup.DIMM{ram.Index}";

            //Check if we can switch to the correct page
            if (Measure(startupTrace, $"{phasePrefix}.ChangePage", () => ram.ChangePage(PageData.ModulePartNumber), changed => changed ? "Module part number page selected" : "Module part number page unavailable"))
                name = Measure(startupTrace, $"{phasePrefix}.ReadName", () => $"{ram.GetModuleManufacturerString()} - {ram.ModulePartNumber()} (#{ram.Index})", result => result);

            DimmMemory memory = Measure(startupTrace,
                                        $"{phasePrefix}.CreateHardware",
                                        () => new DimmMemory(ram, name, new Identifier("memory", "dimm", $"{ram.Index}"), settings),
                                        memory => $"{memory.Sensors.Length} sensor(s)");
            additions.Add(memory);
        }

        _hardware = [.. _hardware, .. additions];
        foreach (Hardware hardware in additions)
            HardwareAdded?.Invoke(hardware);
    }
}

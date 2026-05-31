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

namespace LibreHardwareMonitor.Hardware.Memory;

internal class MemoryGroup : IGroup, IHardwareChanged, IHardwareDiscoveryTask
{
    private const string DeferDimmDetectionEnvironmentVariable = "LHM_MEMORY_DEFER_DIMM_DETECTION";
    private const string DeferDimmDetectionSetting = "memory.deferDimmDetection";
    private static readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(2.5);
    private static readonly object _lock = new();
    private List<Hardware> _hardware = [];

    private CancellationTokenSource _cancellationTokenSource;
    private Task _hardwareDiscoveryTask = Task.CompletedTask;
    private Exception _lastException;
    private bool _disposed = false;

    public MemoryGroup(ISettings settings)
        : this(settings, null)
    { }

    internal MemoryGroup(ISettings settings, HardwareStartupTrace startupTrace)
    {
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
            startupTrace?.Skip("MemoryGroup.DimmDetection", "Deferred to background.");
            StartDimmDetectionTask(settings, TimeSpan.Zero);
        }
        else if (!TryAddDimms(settings, startupTrace))
        {
            StartRetryTask(settings);
        }
    }

#pragma warning disable 67
    public event HardwareEventHandler HardwareAdded;
    public event HardwareEventHandler HardwareRemoved;
#pragma warning restore 67

    public IReadOnlyList<IHardware> Hardware => _hardware;

    public Task HardwareDiscoveryTask => _hardwareDiscoveryTask;

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

    private void StartRetryTask(ISettings settings)
    {
        StartDimmDetectionTask(settings, _retryInterval);
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
        string environmentValue = Environment.GetEnvironmentVariable(DeferDimmDetectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return IsTruthy(environmentValue);

        return IsTruthy(settings.GetValue(DeferDimmDetectionSetting, "false"));
    }

    private static bool IsTruthy(string value)
    {
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Equals("on", StringComparison.OrdinalIgnoreCase);
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

    private static void Measure(HardwareStartupTrace startupTrace, string phase, Action action)
    {
        if (startupTrace != null)
            startupTrace.Measure(phase, action);
        else
            action();
    }

    private static T Measure<T>(HardwareStartupTrace startupTrace, string phase, Func<T> action)
    {
        return startupTrace != null ? startupTrace.Measure(phase, action) : action();
    }

    private static T Measure<T>(HardwareStartupTrace startupTrace, string phase, Func<T> action, Func<T, string> getDetail)
    {
        return startupTrace != null ? startupTrace.Measure(phase, action, getDetail) : action();
    }
}

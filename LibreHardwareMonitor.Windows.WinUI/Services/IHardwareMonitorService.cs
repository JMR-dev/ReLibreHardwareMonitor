// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

/// <summary>
/// Owns the <see cref="Hardware.Computer" /> lifecycle and the sensor tree, exposing the surface the
/// view model needs. Extracted as an interface so the view model can be unit-tested with a fake.
/// </summary>
public interface IHardwareMonitorService : IDisposable
{
    event EventHandler? TreeRebuilt;

    Computer Computer { get; }

    object SensorReadLock { get; }

    SensorTreeItemViewModel Root { get; }

    bool IsMotherboardEnabled { get; set; }

    bool IsCpuEnabled { get; set; }

    bool IsMemoryEnabled { get; set; }

    bool IsGpuEnabled { get; set; }

    bool IsPowerMonitorEnabled { get; set; }

    bool IsControllerEnabled { get; set; }

    bool IsStorageEnabled { get; set; }

    bool IsNetworkEnabled { get; set; }

    bool IsPsuEnabled { get; set; }

    bool IsBatteryEnabled { get; set; }

    bool ForceDriveWakeup { get; set; }

    Task OpenAsync(bool raiseTreeRebuilt = true, CancellationToken cancellationToken = default);

    void Reset();

    Task UpdateAsync();

    void ResetMinMax();

    void ClearSensorValues();
}

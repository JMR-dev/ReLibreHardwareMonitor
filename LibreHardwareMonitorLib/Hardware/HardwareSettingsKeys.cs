// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// Setting keys understood by the hardware library.
/// </summary>
public static class HardwareSettingsKeys
{
    /// <summary>Defers DIMM SPD/thermal-sensor detection until after initial memory hardware is registered.</summary>
    public const string MemoryDeferDimmDetection = "memory.deferDimmDetection";

    /// <summary>Defers the first CPU update until the regular update loop.</summary>
    public const string CpuDeferInitialUpdate = "cpu.deferInitialUpdate";

    /// <summary>Defers the CPU timestamp-counter frequency estimate to the background.</summary>
    public const string CpuDeferTscEstimation = "cpu.deferTscEstimation";

    /// <summary>Defers Nvidia GPU detection to the background.</summary>
    public const string NvidiaDeferDetection = "nvidia.deferDetection";

    /// <summary>Defers storage device detection to the background.</summary>
    public const string StorageDeferDetection = "storage.deferDetection";

    /// <summary>Defers network adapter detection to the background.</summary>
    public const string NetworkDeferDetection = "network.deferDetection";

    /// <summary>Defers Intel integrated GPU detection to the background.</summary>
    public const string IntelGpuDeferDetection = "gpu.deferIntelDetection";

    /// <summary>Defers controller detection to the background.</summary>
    public const string ControllerDeferDetection = "controller.deferDetection";

    /// <summary>Defers PSU detection to the background.</summary>
    public const string PsuDeferDetection = "psu.deferDetection";
}

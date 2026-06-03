// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware;

internal interface IHardwareDiscoveryTask
{
    /// <summary>
    /// Starts the group's background hardware discovery. <see cref="Computer" /> calls this only after it has
    /// subscribed to the group's <see cref="IHardwareChanged.HardwareAdded" /> event and snapshotted the
    /// already-present hardware, so each discovered piece of hardware is announced exactly once: never before the
    /// subscription exists (which would drop the notification) and never also via the initial snapshot (a duplicate).
    /// </summary>
    void StartHardwareDiscovery();

    Task HardwareDiscoveryTask { get; }
}

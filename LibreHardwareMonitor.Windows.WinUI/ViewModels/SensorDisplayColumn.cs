// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

[Flags]
internal enum SensorDisplayColumn
{
    None = 0,
    Sensor = 1,
    Value = 2,
    Min = 4,
    Max = 8,
    All = Sensor | Value | Min | Max
}

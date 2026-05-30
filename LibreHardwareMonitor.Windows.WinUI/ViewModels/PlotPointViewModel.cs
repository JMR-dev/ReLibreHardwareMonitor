// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

public sealed class PlotPointViewModel
{
    public PlotPointViewModel(DateTime timestamp, double value)
    {
        Timestamp = timestamp;
        Value = value;
    }

    public DateTime Timestamp { get; }

    public double Value { get; }
}

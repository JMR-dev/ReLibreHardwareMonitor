// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System.Collections.ObjectModel;
using Windows.UI;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

public sealed class PlotSeriesViewModel
{
    public PlotSeriesViewModel(string sensorIdentifier, string name, Color color)
    {
        SensorIdentifier = sensorIdentifier;
        Name = name;
        Color = color;
    }

    public Color Color { get; set; }

    public string Name { get; }

    public ObservableCollection<PlotPointViewModel> Points { get; } = [];

    public string SensorIdentifier { get; }
}

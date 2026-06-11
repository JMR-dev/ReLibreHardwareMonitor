// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using Windows.UI;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

public sealed class PlotSeriesViewModel
{
    public PlotSeriesViewModel(string sensorIdentifier, string hardwareName, string name, SensorType sensorType, string unit, Color color)
    {
        SensorIdentifier = sensorIdentifier;
        HardwareName = hardwareName;
        Name = name;
        SensorType = sensorType;
        Unit = unit;
        Color = color;
    }

    public Color Color { get; set; }

    public string HardwareName { get; private set; }

    public string Name { get; private set; }

    public List<PlotPointViewModel> Points { get; } = [];

    public string SensorIdentifier { get; }

    public SensorType SensorType { get; private set; }

    public string Title => string.IsNullOrWhiteSpace(HardwareName) ? Name : $"{HardwareName} {Name}";

    public string Unit { get; private set; }

    public void ReplacePoints(IEnumerable<PlotPointViewModel> points)
    {
        Points.Clear();
        Points.AddRange(points);
    }

    public void UpdateMetadata(string hardwareName, string name, SensorType sensorType, string unit)
    {
        HardwareName = hardwareName;
        Name = name;
        SensorType = sensorType;
        Unit = unit;
    }
}

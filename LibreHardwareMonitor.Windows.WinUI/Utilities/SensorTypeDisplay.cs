// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.WinUI.Utilities;

public static class SensorTypeDisplay
{
    public static string GetText(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Voltage => "Voltages",
            SensorType.Current => "Currents",
            SensorType.Clock => "Clocks",
            SensorType.Load => "Load",
            SensorType.Temperature => "Temperatures",
            SensorType.Fan => "Fans",
            SensorType.Flow => "Flows",
            SensorType.Control => "Controls",
            SensorType.Level => "Levels",
            SensorType.Power => "Powers",
            SensorType.Data => "Data",
            SensorType.SmallData => "Data",
            SensorType.Factor => "Factors",
            SensorType.Frequency => "Frequencies",
            SensorType.Throughput => "Throughput",
            SensorType.TimeSpan => "Times",
            SensorType.Timing => "Timings",
            SensorType.Energy => "Capacities",
            SensorType.Noise => "Noise Levels",
            SensorType.Conductivity => "Conductivities",
            SensorType.Humidity => "Humidity Levels",
            _ => sensorType.ToString()
        };
    }

    public static string GetGlyph(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Voltage => "\uE945",
            SensorType.Current => "\uE945",
            SensorType.Clock => "\uE916",
            SensorType.Load => "\uE9D9",
            SensorType.Temperature => "\uE9CA",
            SensorType.Fan => "\uE9F3",
            SensorType.Flow => "\uE9D5",
            SensorType.Control => "\uE713",
            SensorType.Level => "\uE9D2",
            SensorType.Power => "\uE7E8",
            SensorType.Data => "\uE8AB",
            SensorType.SmallData => "\uE8AB",
            SensorType.Factor => "\uE9D2",
            SensorType.Frequency => "\uE916",
            SensorType.Throughput => "\uE9D5",
            SensorType.TimeSpan => "\uE121",
            SensorType.Timing => "\uE121",
            SensorType.Energy => "\uEBAA",
            SensorType.Noise => "\uE767",
            SensorType.Conductivity => "\uE945",
            SensorType.Humidity => "\uE9CA",
            _ => "\uE9D9"
        };
    }

    public static string GetHardwareGlyph(HardwareType hardwareType)
    {
        return hardwareType switch
        {
            HardwareType.Motherboard => "\uE950",
            HardwareType.SuperIO => "\uE950",
            HardwareType.Cpu => "\uE950",
            HardwareType.Memory => "\uE8AB",
            HardwareType.GpuNvidia => "\uE7F4",
            HardwareType.GpuAmd => "\uE7F4",
            HardwareType.GpuIntel => "\uE7F4",
            HardwareType.Storage => "\uEDA2",
            HardwareType.Network => "\uE968",
            HardwareType.Cooler => "\uE9F3",
            HardwareType.EmbeddedController => "\uE950",
            HardwareType.Psu => "\uE7E8",
            HardwareType.Battery => "\uEBAA",
            _ => "\uE950"
        };
    }
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Globalization;
using System.Text;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;

namespace LibreHardwareMonitor.Windows.WinUI.Utilities;

public static class SensorFormatter
{
    public static string GetFormatString(ISensor sensor)
    {
        return sensor.SensorType switch
        {
            SensorType.Voltage => "{0:F3} V",
            SensorType.Current => "{0:F3} A",
            SensorType.Clock => "{0:F1} MHz",
            SensorType.Load => "{0:F1} %",
            SensorType.Temperature => "{0:F1} \u00B0C",
            SensorType.Fan => "{0:F0} RPM",
            SensorType.Flow => "{0:F1} L/h",
            SensorType.Control => "{0:F1} %",
            SensorType.Level => "{0:F1} %",
            SensorType.Power => "{0:F1} W",
            SensorType.Data => "{0:F1} GB",
            SensorType.SmallData => "{0:F1} MB",
            SensorType.Factor => "{0:F3}",
            SensorType.Frequency => "{0:F1} Hz",
            SensorType.Throughput => "{0:F1} B/s",
            SensorType.TimeSpan => "{0:g}",
            SensorType.Timing => "{0:F3} ns",
            SensorType.Energy => "{0:F0} mWh",
            SensorType.Noise => "{0:F0} dBA",
            SensorType.Conductivity => "{0:F1} \u00B5S/cm",
            SensorType.Humidity => "{0:F0} %",
            _ => "{0:F1}"
        };
    }

    public static string FormatValue(ISensor sensor, float? value, TemperatureUnit temperatureUnit)
    {
        if (!value.HasValue)
            return "-";

        return sensor.SensorType switch
        {
            SensorType.Voltage => $"{value:F3} V",
            SensorType.Current => $"{value:F3} A",
            SensorType.Clock => $"{value:F1} MHz",
            SensorType.Load => $"{value:F1} %",
            SensorType.Temperature when temperatureUnit == TemperatureUnit.Fahrenheit => $"{CelsiusToFahrenheit(value):F1} \u00B0F",
            SensorType.Temperature => $"{value:F1} \u00B0C",
            SensorType.Fan => $"{value:F0} RPM",
            SensorType.Flow => $"{value:F1} L/h",
            SensorType.Control => $"{value:F1} %",
            SensorType.Level => $"{value:F1} %",
            SensorType.Power => $"{value:F1} W",
            SensorType.Data => $"{value:F1} GB",
            SensorType.SmallData => $"{value:F1} MB",
            SensorType.Factor => $"{value:F3}",
            SensorType.Frequency => $"{value:F1} Hz",
            SensorType.Throughput => FormatThroughput(sensor, value.Value),
            SensorType.TimeSpan => string.Format(CultureInfo.CurrentCulture, "{0:g}", TimeSpan.FromSeconds(value.Value)),
            SensorType.Timing => $"{value:F3} ns",
            SensorType.Energy => $"{value:F0} mWh",
            SensorType.Noise => $"{value:F0} dBA",
            SensorType.Conductivity => $"{value:F1} \u00B5S/cm",
            SensorType.Humidity => $"{value:F0} %",
            _ => value.Value.ToString("F1", CultureInfo.CurrentCulture)
        };
    }

    public static double? GetPlotValue(ISensor sensor, TemperatureUnit temperatureUnit)
    {
        return GetPlotValue(sensor, sensor.Value, temperatureUnit);
    }

    public static double? GetPlotValue(ISensor sensor, float? value, TemperatureUnit temperatureUnit)
    {
        if (!value.HasValue)
            return null;

        if (sensor.SensorType == SensorType.Temperature && temperatureUnit == TemperatureUnit.Fahrenheit)
            return CelsiusToFahrenheit(value.Value);

        return value.Value;
    }

    public static string GetPlotUnit(SensorType sensorType, TemperatureUnit temperatureUnit)
    {
        return sensorType switch
        {
            SensorType.Voltage => "V",
            SensorType.Current => "A",
            SensorType.Clock => "MHz",
            SensorType.Load => "%",
            SensorType.Temperature when temperatureUnit == TemperatureUnit.Fahrenheit => "\u00B0F",
            SensorType.Temperature => "\u00B0C",
            SensorType.Fan => "RPM",
            SensorType.Flow => "L/h",
            SensorType.Control => "%",
            SensorType.Level => "%",
            SensorType.Power => "W",
            SensorType.Data => "GB",
            SensorType.SmallData => "MB",
            SensorType.Factor => "1",
            SensorType.Frequency => "Hz",
            SensorType.Throughput => "B/s",
            SensorType.TimeSpan => "s",
            SensorType.Timing => "ns",
            SensorType.Energy => "mWh",
            SensorType.Noise => "dBA",
            SensorType.Conductivity => "\u00B5S/cm",
            SensorType.Humidity => "%",
            _ => ""
        };
    }

    public static string GetToolTip(ISensor sensor, TemperatureUnit temperatureUnit)
    {
        StringBuilder builder = new();

        if (sensor is ICriticalSensorLimits criticalSensorLimits)
            OptionallyAppendRange(builder, sensor, temperatureUnit, criticalSensorLimits.CriticalLowLimit, criticalSensorLimits.CriticalHighLimit, "critical");

        if (sensor is ISensorLimits sensorLimits)
            OptionallyAppendRange(builder, sensor, temperatureUnit, sensorLimits.LowLimit, sensorLimits.HighLimit, "normal");

        return builder.ToString();
    }

    private static void OptionallyAppendRange(StringBuilder builder, ISensor sensor, TemperatureUnit temperatureUnit, float? min, float? max, string kind)
    {
        if (min.HasValue)
        {
            builder.AppendLine(max.HasValue
                                   ? $"{CultureInfo.CurrentUICulture.TextInfo.ToTitleCase(kind)} range: {FormatValue(sensor, min, temperatureUnit)} to {FormatValue(sensor, max, temperatureUnit)}."
                                   : $"Minimal {kind} value: {FormatValue(sensor, min, temperatureUnit)}.");
        }
        else if (max.HasValue)
        {
            builder.AppendLine($"Maximal {kind} value: {FormatValue(sensor, max, temperatureUnit)}.");
        }
    }

    private static string FormatThroughput(ISensor sensor, float value)
    {
        if (sensor.Name == "Connection Speed")
        {
            return value switch
            {
                100000000 => "100Mbps",
                1000000000 => "1Gbps",
                _ when value < 1024 => $"{value:F0} bps",
                _ when value < 1048576 => $"{value / 1024:F1} Kbps",
                _ when value < 1073741824 => $"{value / 1048576:F1} Mbps",
                _ => $"{value / 1073741824:F1} Gbps"
            };
        }

        const int oneMegabyte = 1048576;
        return value < oneMegabyte ? $"{value / 1024:F1} KB/s" : $"{value / oneMegabyte:F1} MB/s";
    }

    private static float? CelsiusToFahrenheit(float? valueInCelsius)
    {
        return valueInCelsius * 1.8f + 32;
    }

    private static float CelsiusToFahrenheit(float valueInCelsius)
    {
        return valueInCelsius * 1.8f + 32;
    }
}

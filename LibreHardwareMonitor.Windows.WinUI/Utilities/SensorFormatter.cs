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
        return GetFormat(sensor.SensorType).Format;
    }

    public static string FormatValue(ISensor sensor, float? value, TemperatureUnit temperatureUnit)
    {
        if (!value.HasValue)
            return "-";

        // Most types just apply their format string to the value. A few are special: temperature can be converted to
        // Fahrenheit, and throughput/time span have bespoke formatting that the format string alone can't express.
        switch (sensor.SensorType)
        {
            case SensorType.Temperature when temperatureUnit == TemperatureUnit.Fahrenheit:
                return $"{CelsiusToFahrenheit(value.Value):F1} \u00B0F";
            case SensorType.Throughput:
                return FormatThroughput(sensor, value.Value);
            case SensorType.TimeSpan:
                return string.Format(CultureInfo.CurrentCulture, "{0:g}", TimeSpan.FromSeconds(value.Value));
            default:
                return string.Format(CultureInfo.CurrentCulture, GetFormat(sensor.SensorType).Format, value.Value);
        }
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
        if (sensorType == SensorType.Temperature && temperatureUnit == TemperatureUnit.Fahrenheit)
            return "\u00B0F";

        return GetFormat(sensorType).PlotUnit;
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

    private static float CelsiusToFahrenheit(float valueInCelsius)
    {
        return valueInCelsius * 1.8f + 32;
    }

    // Single source of truth for each sensor type's display format and plot-axis unit. GetFormatString, FormatValue,
    // and GetPlotUnit all derive from this so the format string and unit can never drift apart across the three.
    private static SensorTypeFormat GetFormat(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Voltage => new SensorTypeFormat("{0:F3} V", "V"),
            SensorType.Current => new SensorTypeFormat("{0:F3} A", "A"),
            SensorType.Clock => new SensorTypeFormat("{0:F1} MHz", "MHz"),
            SensorType.Load => new SensorTypeFormat("{0:F1} %", "%"),
            SensorType.Temperature => new SensorTypeFormat("{0:F1} °C", "°C"),
            SensorType.Fan => new SensorTypeFormat("{0:F0} RPM", "RPM"),
            SensorType.Flow => new SensorTypeFormat("{0:F1} L/h", "L/h"),
            SensorType.Control => new SensorTypeFormat("{0:F1} %", "%"),
            SensorType.Level => new SensorTypeFormat("{0:F1} %", "%"),
            SensorType.Power => new SensorTypeFormat("{0:F1} W", "W"),
            SensorType.Data => new SensorTypeFormat("{0:F1} GB", "GB"),
            SensorType.SmallData => new SensorTypeFormat("{0:F1} MB", "MB"),
            SensorType.Factor => new SensorTypeFormat("{0:F3}", "1"),
            SensorType.Frequency => new SensorTypeFormat("{0:F1} Hz", "Hz"),
            SensorType.Throughput => new SensorTypeFormat("{0:F1} B/s", "B/s"),
            SensorType.TimeSpan => new SensorTypeFormat("{0:g}", "s"),
            SensorType.Timing => new SensorTypeFormat("{0:F3} ns", "ns"),
            SensorType.Energy => new SensorTypeFormat("{0:F0} mWh", "mWh"),
            SensorType.Noise => new SensorTypeFormat("{0:F0} dBA", "dBA"),
            SensorType.Conductivity => new SensorTypeFormat("{0:F1} µS/cm", "µS/cm"),
            SensorType.Humidity => new SensorTypeFormat("{0:F0} %", "%"),
            _ => new SensorTypeFormat("{0:F1}", "")
        };
    }

    private readonly record struct SensorTypeFormat(string Format, string PlotUnit);
}

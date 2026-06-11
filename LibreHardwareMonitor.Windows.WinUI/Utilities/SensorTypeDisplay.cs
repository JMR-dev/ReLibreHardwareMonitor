// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.WinUI.Utilities;

public static class SensorTypeDisplay
{
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();

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

    public static string GetImageFile(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Voltage => "voltage.png",
            SensorType.Current => "current.png",
            SensorType.Clock => "clock.png",
            SensorType.Load => "load.png",
            SensorType.Temperature => "temperature.png",
            SensorType.Fan => "fan.png",
            SensorType.Flow => "flow.png",
            SensorType.Control => "control.png",
            SensorType.Level => "level.png",
            SensorType.Power => "power.png",
            SensorType.Data => "data.png",
            SensorType.SmallData => "data.png",
            SensorType.Factor => "factor.png",
            SensorType.Frequency => "clock.png",
            SensorType.Throughput => "throughput.png",
            SensorType.TimeSpan => "time.png",
            SensorType.Timing => "time.png",
            SensorType.Energy => "power.png",
            SensorType.Noise => "loudspeaker.png",
            SensorType.Conductivity => "voltage.png",
            SensorType.Humidity => "humidity.png",
            _ => "power.png"
        };
    }

    public static string GetHardwareImageFile(HardwareType hardwareType)
    {
        return hardwareType switch
        {
            HardwareType.Motherboard => "mainboard.png",
            HardwareType.SuperIO => "chip.png",
            HardwareType.Cpu => "cpu.png",
            HardwareType.Memory => "ram.png",
            HardwareType.GpuNvidia => "nvidia.png",
            HardwareType.GpuAmd => "amd.png",
            HardwareType.GpuIntel => "intel.png",
            HardwareType.Storage => "hdd.png",
            HardwareType.Network => "nic.png",
            HardwareType.Cooler => "fan.png",
            HardwareType.EmbeddedController => "chip.png",
            HardwareType.Psu => "power-supply.png",
            HardwareType.Battery => "battery.png",
            HardwareType.PowerMonitor => "powermonitor.png",
            _ => "cpu.png"
        };
    }

    public static ImageSource? GetImageByFilename(string? filename)
    {
        if (string.IsNullOrEmpty(filename))
            return null;

        if (_imageCache.TryGetValue(filename, out var cached))
            return cached;

        try
        {
            var assembly = typeof(SensorTypeDisplay).Assembly;
            using var stream = assembly.GetManifestResourceStream("LibreHardwareMonitor.Windows.WinUI.Resources." + filename);
            if (stream == null)
                return null;

            var bitmapImage = new BitmapImage();
            var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writeStream = randomAccessStream.AsStreamForWrite())
            {
                stream.CopyTo(writeStream);
                writeStream.Flush();
                randomAccessStream.Seek(0);
                bitmapImage.SetSource(randomAccessStream);
            }

            _imageCache[filename] = bitmapImage;
            return bitmapImage;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "icon_error.txt"), ex.ToString());
            }
            catch {}
            return null;
        }
    }
}

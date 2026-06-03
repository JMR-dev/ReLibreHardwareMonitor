// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Globalization;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using static LibreHardwareMonitor.Windows.WinUI.Services.TrayIconInterop;
using WinUIColor = Windows.UI.Color;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

/// <summary>
/// Renders a sensor's current value into a 16x16 GDI icon (solid background tinted by the sensor's tray color, with the
/// value drawn on top) for display in the system tray. Extracted from <see cref="TrayIconService" /> to keep the GDI
/// drawing separate from the tray orchestration.
/// </summary>
internal sealed class SensorIconRenderer
{
    private const int IconSize = 16;

    private readonly AppSettings _settings;
    private readonly Func<TemperatureUnit> _temperatureUnitProvider;

    public SensorIconRenderer(AppSettings settings, Func<TemperatureUnit> temperatureUnitProvider)
    {
        _settings = settings;
        _temperatureUnitProvider = temperatureUnitProvider;
    }

    /// <summary>Creates an icon handle for the sensor. The caller owns the handle and must DestroyIcon() it.</summary>
    public IntPtr CreateIcon(ISensor sensor)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(hdc);
        IntPtr bitmap = IntPtr.Zero;
        IntPtr mask = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        IntPtr font = IntPtr.Zero;
        IntPtr oldFont = IntPtr.Zero;
        IntPtr brush = IntPtr.Zero;

        try
        {
            BitmapInfo bitmapInfo = BitmapInfo.Create(IconSize, IconSize);
            bitmap = CreateDIBSection(hdc, ref bitmapInfo, 0, out _, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero)
                return IntPtr.Zero; // never return the shared main icon: callers DestroyIcon() the returned handle

            oldBitmap = SelectObject(memoryDc, bitmap);
            WinUIColor color = GetSensorTrayColor(sensor);
            brush = CreateSolidBrush(ToColorRef(color));
            Rect fill = new() { Left = 0, Top = 0, Right = IconSize, Bottom = IconSize };
            FillRect(memoryDc, ref fill, brush);

            font = CreateFont(-11, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 0, 0, "Segoe UI");
            oldFont = SelectObject(memoryDc, font);
            SetBkMode(memoryDc, 1);
            SetTextColor(memoryDc, ToColorRef(WinUIColor.FromArgb(255, 255, 255, 255)));

            string text = GetSensorIconText(sensor);
            Rect textRect = new() { Left = 0, Top = 0, Right = IconSize, Bottom = IconSize };
            DrawText(memoryDc, text, text.Length, ref textRect, 0x00000001 | 0x00000004 | 0x00000020);

            mask = CreateBitmap(IconSize, IconSize, 1, 1, IntPtr.Zero);
            IconInfo iconInfo = new()
            {
                IsIcon = true,
                ColorBitmap = bitmap,
                MaskBitmap = mask
            };

            return CreateIconIndirect(ref iconInfo);
        }
        finally
        {
            if (oldFont != IntPtr.Zero)
                SelectObject(memoryDc, oldFont);
            if (oldBitmap != IntPtr.Zero)
                SelectObject(memoryDc, oldBitmap);
            if (font != IntPtr.Zero)
                DeleteObject(font);
            if (brush != IntPtr.Zero)
                DeleteObject(brush);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (mask != IntPtr.Zero)
                DeleteObject(mask);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            if (hdc != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// Returns a key identifying the pixels <see cref="CreateIcon" /> would produce (the drawn text and the background
    /// color). When it is unchanged the previously created icon can be reused instead of re-rendering a new GDI icon.
    /// </summary>
    public string GetRenderKey(ISensor sensor)
    {
        WinUIColor color = GetSensorTrayColor(sensor);
        return $"{GetSensorIconText(sensor)}|{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private WinUIColor GetSensorTrayColor(ISensor sensor)
    {
        WinUIColor defaultColor = sensor.SensorType is SensorType.Load or SensorType.Control or SensorType.Level
            ? WinUIColor.FromArgb(255, 0x70, 0x8c, 0xf1)
            : WinUIColor.FromArgb(255, 0x00, 0x78, 0xd4);

        return _settings.GetValue(SensorSelectionService.GetSensorSettingName(sensor, "traycolor"), defaultColor);
    }

    private string GetSensorIconText(ISensor sensor)
    {
        if (!sensor.Value.HasValue)
            return "-";

        float value = sensor.Value.Value;
        if (sensor.SensorType == SensorType.Temperature && _temperatureUnitProvider() == TemperatureUnit.Fahrenheit)
            value = value * 1.8f + 32;

        return sensor.SensorType switch
        {
            SensorType.TimeSpan => TimeSpan.FromSeconds(value).ToString("g", CultureInfo.CurrentCulture),
            SensorType.Timing => value.ToString("F1", CultureInfo.CurrentCulture),
            SensorType.Clock or SensorType.Fan or SensorType.Flow => (value * 1e-3f).ToString("F1", CultureInfo.CurrentCulture),
            SensorType.Voltage or SensorType.Current or SensorType.SmallData or SensorType.Factor or SensorType.Throughput or SensorType.Conductivity => value.ToString("F1", CultureInfo.CurrentCulture),
            _ => value.ToString("F0", CultureInfo.CurrentCulture)
        };
    }

    private static uint ToColorRef(WinUIColor color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }
}

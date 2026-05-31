// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using WinUIColor = Windows.UI.Color;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x42;
    private const uint MainIconId = 1;
    private const uint SensorIconIdStart = 1000;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint MfSeparator = 0x00000800;
    private const uint MfString = 0x00000000;
    private const uint TpmReturNcmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const int IconSize = 16;

    private readonly Dictionary<string, SensorTrayIcon> _sensorIconsByIdentifier = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, SensorTrayIcon> _sensorIconsById = new();
    private readonly Func<TemperatureUnit> _temperatureUnitProvider;
    private readonly IntPtr _windowHandle;
    private readonly SubclassProc _subclassProc;
    private readonly AppSettings _settings;
    private bool _disposed;
    private bool _destroyMainIconHandle = true;
    private bool _isMainIconEnabled;
    private bool _isMainIconVisible;
    private IntPtr _mainIconHandle;
    private uint _nextSensorIconId = SensorIconIdStart;

    public TrayIconService(IntPtr windowHandle, AppSettings settings, Func<TemperatureUnit> temperatureUnitProvider)
    {
        _windowHandle = windowHandle;
        _settings = settings;
        _temperatureUnitProvider = temperatureUnitProvider;
        _subclassProc = WindowSubclassProc;
        SetWindowSubclass(_windowHandle, _subclassProc, MainIconId, 0);
        _mainIconHandle = LoadApplicationIcon();
    }

    public event EventHandler? HideShowRequested;

    public event EventHandler? ExitRequested;

    public bool IsMainIconEnabled
    {
        get => _isMainIconEnabled;
        set
        {
            if (_isMainIconEnabled == value)
                return;

            _isMainIconEnabled = value;
            UpdateMainIconVisibility();
        }
    }

    public void Add(ISensor sensor, bool persist = true)
    {
        string identifier = sensor.Identifier.ToString();
        if (_sensorIconsByIdentifier.TryGetValue(identifier, out SensorTrayIcon? existing))
        {
            existing.Sensor = sensor;
            Update(existing);
            return;
        }

        SensorTrayIcon icon = new(_nextSensorIconId++, sensor);
        icon.IconHandle = CreateSensorIcon(sensor);
        _sensorIconsByIdentifier[identifier] = icon;
        _sensorIconsById[icon.Id] = icon;
        AddNotifyIcon(icon.Id, icon.IconHandle, GetSensorToolTip(sensor));

        if (persist)
            _settings.SetValue(GetSensorSettingName(sensor, "tray"), true);

        UpdateMainIconVisibility();
    }

    public bool Contains(ISensor sensor)
    {
        return _sensorIconsByIdentifier.ContainsKey(sensor.Identifier.ToString());
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (SensorTrayIcon icon in _sensorIconsByIdentifier.Values.ToArray())
            DeleteSensorIcon(icon, deleteSettings: false);

        SetMainIconVisible(false);
        if (_destroyMainIconHandle && _mainIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_mainIconHandle);
            _mainIconHandle = IntPtr.Zero;
        }

        RemoveWindowSubclass(_windowHandle, _subclassProc, MainIconId);
    }

    public void Remove(ISensor sensor, bool deleteSettings = true)
    {
        if (_sensorIconsByIdentifier.TryGetValue(sensor.Identifier.ToString(), out SensorTrayIcon? icon))
            DeleteSensorIcon(icon, deleteSettings);
    }

    public void RestoreSelectedSensors(IEnumerable<SensorTreeItemViewModel> sensorItems)
    {
        Dictionary<string, ISensor> selected = sensorItems
            .Where(item => item.Sensor != null && _settings.GetValue(GetSensorSettingName(item.Sensor, "tray"), false))
            .Select(item => item.Sensor!)
            .ToDictionary(sensor => sensor.Identifier.ToString(), StringComparer.OrdinalIgnoreCase);

        foreach (SensorTrayIcon icon in _sensorIconsByIdentifier.Values.ToArray())
        {
            if (!selected.ContainsKey(icon.Sensor.Identifier.ToString()))
                DeleteSensorIcon(icon, deleteSettings: false);
        }

        foreach (ISensor sensor in selected.Values)
            Add(sensor, persist: false);

        UpdateMainIconVisibility();
    }

    public void Update()
    {
        foreach (SensorTrayIcon icon in _sensorIconsByIdentifier.Values)
            Update(icon);
    }

    private void Update(SensorTrayIcon icon)
    {
        IntPtr previousIcon = icon.IconHandle;
        icon.IconHandle = CreateSensorIcon(icon.Sensor);
        ModifyNotifyIcon(icon.Id, icon.IconHandle, GetSensorToolTip(icon.Sensor));
        if (previousIcon != IntPtr.Zero)
            DestroyIcon(previousIcon);
    }

    private void DeleteSensorIcon(SensorTrayIcon icon, bool deleteSettings)
    {
        DeleteNotifyIcon(icon.Id);
        _sensorIconsByIdentifier.Remove(icon.Sensor.Identifier.ToString());
        _sensorIconsById.Remove(icon.Id);
        if (icon.IconHandle != IntPtr.Zero)
        {
            DestroyIcon(icon.IconHandle);
            icon.IconHandle = IntPtr.Zero;
        }

        if (deleteSettings)
        {
            _settings.Remove(GetSensorSettingName(icon.Sensor, "tray"));
            _settings.Remove(GetSensorSettingName(icon.Sensor, "traycolor"));
        }

        UpdateMainIconVisibility();
    }

    private void UpdateMainIconVisibility()
    {
        SetMainIconVisible(_isMainIconEnabled && _sensorIconsByIdentifier.Count == 0);
    }

    private void SetMainIconVisible(bool visible)
    {
        if (_isMainIconVisible == visible)
            return;

        _isMainIconVisible = visible;
        if (visible)
            AddNotifyIcon(MainIconId, _mainIconHandle, "Libre Hardware Monitor");
        else
            DeleteNotifyIcon(MainIconId);
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, nuint subclassId, nuint refData)
    {
        if (message == CallbackMessage)
        {
            // With NOTIFYICON_VERSION_4 the event is packed into lParam: LOWORD = the mouse/keyboard message,
            // HIWORD = the icon id. (wParam carries the cursor coordinates, which we don't use.)
            uint packed = unchecked((uint)lParam.ToInt64());
            uint mouseMessage = packed & 0xFFFF;
            uint iconId = (packed >> 16) & 0xFFFF;
            HandleTrayCallback(iconId, mouseMessage);
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void HandleTrayCallback(uint iconId, uint mouseMessage)
    {
        if (mouseMessage == WmLButtonDblClk)
        {
            HideShowRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (mouseMessage != WmRButtonUp)
            return;

        if (iconId == MainIconId)
        {
            ShowMainContextMenu();
            return;
        }

        if (_sensorIconsById.TryGetValue(iconId, out SensorTrayIcon? icon))
            ShowSensorContextMenu(icon);
    }

    private void ShowMainContextMenu()
    {
        const uint hideShowCommand = 1;
        const uint exitCommand = 2;

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            AppendMenu(menu, MfString, hideShowCommand, "Hide/Show");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, exitCommand, "Exit");

            uint command = TrackMenu(menu);
            if (command == hideShowCommand)
                HideShowRequested?.Invoke(this, EventArgs.Empty);
            else if (command == exitCommand)
                ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void ShowSensorContextMenu(SensorTrayIcon icon)
    {
        const uint hideShowCommand = 1;
        const uint removeCommand = 2;
        const uint exitCommand = 3;

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            AppendMenu(menu, MfString, hideShowCommand, "Hide/Show");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, removeCommand, "Remove Sensor");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, exitCommand, "Exit");

            uint command = TrackMenu(menu);
            if (command == hideShowCommand)
                HideShowRequested?.Invoke(this, EventArgs.Empty);
            else if (command == removeCommand)
                DeleteSensorIcon(icon, deleteSettings: true);
            else if (command == exitCommand)
                ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private uint TrackMenu(IntPtr menu)
    {
        GetCursorPos(out Point point);
        SetForegroundWindow(_windowHandle);
        return (uint)TrackPopupMenuEx(menu, TpmReturNcmd | TpmRightButton, point.X, point.Y, _windowHandle, IntPtr.Zero);
    }

    private void AddNotifyIcon(uint id, IntPtr icon, string tip)
    {
        NotifyIconData data = CreateNotifyIconData(id, icon, tip);
        Shell_NotifyIcon(NimAdd, ref data);
        data.uTimeoutOrVersion = NotifyIconVersion4;
        Shell_NotifyIcon(NimSetVersion, ref data);
    }

    private void ModifyNotifyIcon(uint id, IntPtr icon, string tip)
    {
        NotifyIconData data = CreateNotifyIconData(id, icon, tip);
        Shell_NotifyIcon(NimModify, ref data);
    }

    private void DeleteNotifyIcon(uint id)
    {
        NotifyIconData data = CreateNotifyIconData(id, IntPtr.Zero, "");
        Shell_NotifyIcon(NimDelete, ref data);
    }

    private NotifyIconData CreateNotifyIconData(uint id, IntPtr icon, string tip)
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = id,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = CallbackMessage,
            hIcon = icon,
            szTip = TrimForNotifyIcon(tip, 127),
            szInfo = "",
            szInfoTitle = ""
        };
    }

    private IntPtr LoadApplicationIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico");
        if (File.Exists(iconPath))
        {
            IntPtr icon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x00000010);
            if (icon != IntPtr.Zero)
                return icon;
        }

        _destroyMainIconHandle = false;
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    private IntPtr CreateSensorIcon(ISensor sensor)
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

    private WinUIColor GetSensorTrayColor(ISensor sensor)
    {
        WinUIColor defaultColor = sensor.SensorType is SensorType.Load or SensorType.Control or SensorType.Level
            ? WinUIColor.FromArgb(255, 0x70, 0x8c, 0xf1)
            : WinUIColor.FromArgb(255, 0x00, 0x78, 0xd4);

        return _settings.GetValue(GetSensorSettingName(sensor, "traycolor"), defaultColor);
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

    private string GetSensorToolTip(ISensor sensor)
    {
        string value = SensorFormatter.FormatValue(sensor, sensor.Value, _temperatureUnitProvider());
        string text = $"{sensor.Hardware.Name}{Environment.NewLine}{sensor.Name}: {value}";
        return TrimForNotifyIcon(text, 127);
    }

    private static uint ToColorRef(WinUIColor color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }

    private static string TrimForNotifyIcon(string text, int maximumLength)
    {
        return text.Length <= maximumLength ? text : text[..maximumLength];
    }

    private static string GetSensorSettingName(ISensor sensor, string suffix)
    {
        return new Identifier(sensor.Identifier, suffix).ToString();
    }

    private sealed class SensorTrayIcon(uint id, ISensor sensor)
    {
        public uint Id { get; } = id;

        public IntPtr IconHandle { get; set; }

        public ISensor Sensor { get; set; } = sensor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public int XHotspot;
        public int YHotspot;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;

        public static BitmapInfo Create(int width, int height)
        {
            return new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32
                }
            };
        }
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, nuint subclassId, nuint refData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc subclassProc, nuint subclassId, nuint refData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc subclassProc, nuint subclassId);

    [DllImport("Comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int desiredWidth, int desiredHeight, uint load);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint newItemId, string? newItem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr windowHandle, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref BitmapInfo bitmapInfo, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, ref Rect rect, IntPtr brush);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr deviceContext, uint color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr deviceContext, string text, int count, ref Rect rect, uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo iconInfo);
}

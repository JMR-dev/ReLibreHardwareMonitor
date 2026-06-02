// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using static LibreHardwareMonitor.Windows.WinUI.Services.TrayIconInterop;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

// Orchestrates the system-tray icons: a main app icon (shown only when no per-sensor icons are present) plus one icon
// per user-selected sensor, their context menus, and routing of the shell callback messages. Win32 interop lives in
// TrayIconInterop; the per-sensor icon bitmaps are drawn by SensorIconRenderer.
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

    private readonly Dictionary<string, SensorTrayIcon> _sensorIconsByIdentifier = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, SensorTrayIcon> _sensorIconsById = new();
    private readonly Func<TemperatureUnit> _temperatureUnitProvider;
    private readonly SensorIconRenderer _iconRenderer;
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
        _iconRenderer = new SensorIconRenderer(settings, temperatureUnitProvider);
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
        icon.IconHandle = _iconRenderer.CreateIcon(sensor);
        _sensorIconsByIdentifier[identifier] = icon;
        _sensorIconsById[icon.Id] = icon;
        AddNotifyIcon(icon.Id, icon.IconHandle, GetSensorToolTip(sensor));

        if (persist)
            _settings.SetValue(SensorSelectionService.GetSensorSettingName(sensor, "tray"), true);

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
            .Where(item => item.Sensor != null && _settings.GetValue(SensorSelectionService.GetSensorSettingName(item.Sensor, "tray"), false))
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
        icon.IconHandle = _iconRenderer.CreateIcon(icon.Sensor);
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
            _settings.Remove(SensorSelectionService.GetSensorSettingName(icon.Sensor, "tray"));
            _settings.Remove(SensorSelectionService.GetSensorSettingName(icon.Sensor, "traycolor"));
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

    private string GetSensorToolTip(ISensor sensor)
    {
        string value = SensorFormatter.FormatValue(sensor, sensor.Value, _temperatureUnitProvider());
        string text = $"{sensor.Hardware.Name}{Environment.NewLine}{sensor.Name}: {value}";
        return TrimForNotifyIcon(text, 127);
    }

    private static string TrimForNotifyIcon(string text, int maximumLength)
    {
        return text.Length <= maximumLength ? text : text[..maximumLength];
    }

    private sealed class SensorTrayIcon(uint id, ISensor sensor)
    {
        public uint Id { get; } = id;

        public IntPtr IconHandle { get; set; }

        public ISensor Sensor { get; set; } = sensor;
    }
}

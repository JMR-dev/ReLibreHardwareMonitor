// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

internal sealed class WindowPlacementService
{
    private readonly AppWindow _appWindow;
    private readonly AppSettings _settings;
    private readonly IntPtr _hwnd;
    private readonly string _settingPrefix;
    private readonly int _defaultLogicalMinWidth;
    private readonly int _defaultLogicalMinHeight;
    private readonly int _defaultLogicalWidth;
    private readonly int _defaultLogicalHeight;

    public WindowPlacementService(
        AppWindow appWindow,
        AppSettings settings,
        IntPtr hwnd,
        string settingPrefix = "mainForm.",
        int defaultLogicalMinWidth = 470,
        int defaultLogicalMinHeight = 640,
        int defaultLogicalWidth = 760,
        int defaultLogicalHeight = 680)
    {
        _appWindow = appWindow;
        _settings = settings;
        _hwnd = hwnd;
        _settingPrefix = settingPrefix;
        _defaultLogicalMinWidth = defaultLogicalMinWidth;
        _defaultLogicalMinHeight = defaultLogicalMinHeight;
        _defaultLogicalWidth = defaultLogicalWidth;
        _defaultLogicalHeight = defaultLogicalHeight;
    }

    public double GetWindowScale()
    {
        uint dpi = GetDpiForWindow(_hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    public void Restore()
    {
        // AppWindow.Resize takes physical pixels and the app is PerMonitorV2 DPI-aware, so the logical default/minimum
        // sizes must be scaled by the window's DPI. Without this, on a high-DPI display (e.g. a 200% laptop panel) the
        // window — and any SW_RESTORE from the tray, which un-maximizes to this size — came out at half size.
        double scale = GetWindowScale();
        int minWidth = (int)Math.Round(_defaultLogicalMinWidth * scale);
        int minHeight = (int)Math.Round(_defaultLogicalMinHeight * scale);
        int width = Math.Max(minWidth, _settings.GetValue(_settingPrefix + "Width", (int)Math.Round(_defaultLogicalWidth * scale)));
        int height = Math.Max(minHeight, _settings.GetValue(_settingPrefix + "Height", (int)Math.Round(_defaultLogicalHeight * scale)));
        _appWindow.Resize(new SizeInt32(width, height));

        int x = _settings.GetValue(_settingPrefix + "Location.X", int.MinValue);
        int y = _settings.GetValue(_settingPrefix + "Location.Y", int.MinValue);
        if (x != int.MinValue && y != int.MinValue)
            _appWindow.Move(new PointInt32(x, y));
    }

    public void Maximize()
    {
        if (_settings.GetValue(_settingPrefix + "Maximized", false) && _appWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();
    }

    public void Save()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            bool maximized = presenter.State == OverlappedPresenterState.Maximized;
            _settings.SetValue(_settingPrefix + "Maximized", maximized);

            if (presenter.State == OverlappedPresenterState.Restored)
            {
                _settings.SetValue(_settingPrefix + "Location.X", _appWindow.Position.X);
                _settings.SetValue(_settingPrefix + "Location.Y", _appWindow.Position.Y);
                _settings.SetValue(_settingPrefix + "Width", _appWindow.Size.Width);
                _settings.SetValue(_settingPrefix + "Height", _appWindow.Size.Height);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);
}

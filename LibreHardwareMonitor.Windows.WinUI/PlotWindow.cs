// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using LibreHardwareMonitor.Windows.WinUI.Controls;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace LibreHardwareMonitor.Windows.WinUI;

public sealed class PlotWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AppWindow _appWindow;
    private readonly PlotView _plotView;
    private bool _closingFromOwner;

    internal PlotWindow(AppSettings settings, MainWindowViewModel viewModel)
    {
        _settings = settings;

        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.Title = "Sensor Plot";

        _plotView = new PlotView(viewModel);
        Content = _plotView;

        RestoreBounds();
        Closed += PlotWindow_Closed;
    }

    public event EventHandler? UserClosed;

    public void ApplyTheme(AppThemeMode themeMode)
    {
        _plotView.ApplyTheme(themeMode);
    }

    public void RedrawPlot()
    {
        _plotView.Redraw();
    }

    public void CloseFromOwner()
    {
        _closingFromOwner = true;
        Close();
    }

    private void RestoreBounds()
    {
        int width = Math.Max(320, _settings.GetValue("plotForm.Width", 600));
        int height = Math.Max(220, _settings.GetValue("plotForm.Height", 400));
        _appWindow.Resize(new SizeInt32(width, height));

        int x = _settings.GetValue("plotForm.Location.X", int.MinValue);
        int y = _settings.GetValue("plotForm.Location.Y", int.MinValue);
        if (x != int.MinValue && y != int.MinValue)
            _appWindow.Move(new PointInt32(x, y));
    }

    private void PlotWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveBounds();
        if (!_closingFromOwner)
            UserClosed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveBounds()
    {
        _settings.SetValue("plotForm.Location.X", _appWindow.Position.X);
        _settings.SetValue("plotForm.Location.Y", _appWindow.Position.Y);
        _settings.SetValue("plotForm.Width", _appWindow.Size.Width);
        _settings.SetValue("plotForm.Height", _appWindow.Size.Height);
    }
}

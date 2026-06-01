// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace LibreHardwareMonitor.Windows.WinUI;

public sealed class PlotWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AppWindow _appWindow;
    private readonly Grid _root;
    private bool _closingFromOwner;

    public PlotWindow(AppSettings settings)
    {
        _settings = settings;

        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.Title = "Sensor Plot";

        _root = new Grid();
        PlotCanvas = new Canvas
        {
            MinWidth = 320,
            MinHeight = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        PlotCanvas.SizeChanged += (_, _) => PlotSizeChanged?.Invoke(this, EventArgs.Empty);
        _root.Children.Add(PlotCanvas);
        Content = _root;

        RestoreBounds();
        Closed += PlotWindow_Closed;
    }

    public event EventHandler? PlotSizeChanged;

    public event EventHandler? UserClosed;

    public Canvas PlotCanvas { get; }

    public void ApplyTheme(AppThemeMode themeMode)
    {
        _root.RequestedTheme = themeMode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark or AppThemeMode.Black => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
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

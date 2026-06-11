// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Services.Tracing;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace LibreHardwareMonitor.Windows.WinUI.Composition;

/// <summary>
/// The runtime/window-tied services that can only be built once the window exists (their dependencies
/// — HWND, AppWindow, dispatcher, XamlRoot, and window-method closures — are not available at the time
/// the container graph is built).
/// </summary>
internal sealed record MainWindowRuntime(
    AppWindow AppWindow,
    WindowChromeManager ChromeManager,
    WindowPlacementService PlacementService,
    TrayIconService TrayIconService,
    DialogService DialogService,
    SecondaryWindowCoordinator SecondaryWindows,
    SensorColumnMeasurer ColumnMeasurer);

/// <summary>
/// Builds the runtime/window-tied services for a constructed <see cref="Window" />. This is the explicit
/// two-phase boundary between the container-resolved graph and the values that only exist after the
/// window is created.
/// </summary>
internal interface IMainWindowRuntimeFactory
{
    MainWindowRuntime Create(
        Window window,
        MainWindowViewModel viewModel,
        Func<XamlRoot?> xamlRootProvider,
        Action hideShowMainWindow);
}

internal sealed class MainWindowRuntimeFactory : IMainWindowRuntimeFactory
{
    private readonly AppSettings _settings;
    private readonly IStartupTracer _startupTrace;

    public MainWindowRuntimeFactory(AppSettings settings, IStartupTracer startupTrace)
    {
        _settings = settings;
        _startupTrace = startupTrace;
    }

    public MainWindowRuntime Create(
        Window window,
        MainWindowViewModel viewModel,
        Func<XamlRoot?> xamlRootProvider,
        Action hideShowMainWindow)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(window);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Title = "Libre Hardware Monitor";

        WindowChromeManager chromeManager = new(hwnd);
        WindowPlacementService placementService = new(appWindow, _settings, hwnd);
        TrayIconService trayIconService = new(hwnd, _settings, () => viewModel.TemperatureUnit);
        DialogService dialogService = new(xamlRootProvider, viewModel, hwnd);
        SecondaryWindowCoordinator secondaryWindows = new(viewModel, hideShowMainWindow);
        SensorColumnMeasurer columnMeasurer = new(window.DispatcherQueue, viewModel, _startupTrace);

        return new MainWindowRuntime(
            appWindow,
            chromeManager,
            placementService,
            trayIconService,
            dialogService,
            secondaryWindows,
            columnMeasurer);
    }
}

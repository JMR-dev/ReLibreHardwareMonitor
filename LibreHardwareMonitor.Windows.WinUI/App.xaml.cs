// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.IO;
using System.Threading.Tasks;
using LibreHardwareMonitor.Windows.WinUI.Composition;
using LibreHardwareMonitor.Windows.WinUI.Services.Tracing;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using IOPath = System.IO.Path;

namespace LibreHardwareMonitor.Windows.WinUI;

public partial class App : Application
{
    private bool _launchStarted;
    private readonly IStartupTracer _startupTrace;
    private ServiceProvider? _serviceProvider;
    private Window? _window;

    public App()
    {
        _startupTrace = StartupTracer.Create();
        _startupTrace.Mark("App.Constructor.Begin");
        _startupTrace.Measure("App.WireExceptionHandlers", () =>
        {
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        });
        _startupTrace.Measure("App.InitializeComponent", InitializeComponent);
        _startupTrace.Mark("App.Constructor.Complete");
        _startupTrace.Flush();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _startupTrace.Measure("App.OnLaunched", LaunchMainWindow);
    }

    private void LaunchMainWindow()
    {
        if (_launchStarted)
            return;

        _launchStarted = true;
        try
        {
            _startupTrace.Mark("App.LaunchMainWindow.Begin");
            _serviceProvider = _startupTrace.Measure("App.BuildServiceProvider", () =>
                new ServiceCollection().AddAppServices(_startupTrace).BuildServiceProvider());
            MainWindow mainWindow = _startupTrace.Measure("App.CreateMainWindow", () => new MainWindow(
                _serviceProvider.GetRequiredService<MainWindowViewModel>(),
                _startupTrace,
                _serviceProvider.GetRequiredService<IMainWindowRuntimeFactory>(),
                _serviceProvider));
            _window = mainWindow;
            _startupTrace.Measure("App.ActivateWindow", mainWindow.Activate);
            _startupTrace.Measure("App.StartMonitoringAfterActivation", mainWindow.StartMonitoringAfterActivation);
            _startupTrace.Mark("App.LaunchMainWindow.Complete");
            _startupTrace.Flush();
        }
        catch (Exception ex)
        {
            _startupTrace.Mark("App.LaunchMainWindow.Exception", $"{ex.GetType().FullName}: {ex.Message}");
            _startupTrace.Flush();
            WriteExceptionLog("Window launch failed", ex);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        WriteExceptionLog("WinUI unhandled exception", args.Exception);
    }

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            WriteExceptionLog("AppDomain unhandled exception", exception);
        else
            WriteMessageLog($"AppDomain unhandled exception: {args.ExceptionObject}");
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        WriteExceptionLog("Unobserved task exception", args.Exception);
    }

    private static void WriteExceptionLog(string context, Exception exception)
    {
        WriteMessageLog($"{context}{Environment.NewLine}{exception}");
    }

    private static void WriteMessageLog(string message)
    {
        string logPath = IOPath.Combine(AppContext.BaseDirectory, "LibreHardwareMonitor.Windows.WinUI.startup.log");
        File.AppendAllText(logPath, $"{DateTimeOffset.Now:u} {message}{Environment.NewLine}{Environment.NewLine}");
    }
}

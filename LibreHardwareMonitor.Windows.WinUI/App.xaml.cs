// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using IOPath = System.IO.Path;

namespace LibreHardwareMonitor.Windows.WinUI;

public partial class App : Application
{
    private bool _launchStarted;
    private Window? _window;

    public App()
    {
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LaunchMainWindow();
    }

    private void LaunchMainWindow()
    {
        if (_launchStarted)
            return;

        _launchStarted = true;
        try
        {
            MainWindow mainWindow = new();
            _window = mainWindow;
            _window.Activate();
            mainWindow.StartMonitoringAfterActivation();
        }
        catch (Exception ex)
        {
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

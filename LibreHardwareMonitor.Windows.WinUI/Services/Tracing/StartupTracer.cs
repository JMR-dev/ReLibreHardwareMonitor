// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.WinUI.Services.Tracing;

/// <summary>
/// Factory that selects the appropriate <see cref="IStartupTracer"/> based on environment
/// configuration. Returns a <see cref="FileStartupTracer"/> when timing is enabled, otherwise
/// the shared <see cref="NoOpStartupTracer"/>.
/// </summary>
internal static class StartupTracer
{
    private const string EnabledEnvironmentVariable = "LHM_WINUI_STARTUP_TIMING";
    private const string PathEnvironmentVariable = "LHM_WINUI_STARTUP_TIMING_PATH";

    public static IStartupTracer Create()
    {
        return IsEnabled() ? new FileStartupTracer(GetLogFileName()) : NoOpStartupTracer.Instance;
    }

    private static string GetLogFileName()
    {
        string? configuredPath = Environment.GetEnvironmentVariable(PathEnvironmentVariable);

        return StartupTraceLogSupport.GetLogFileName("LibreHardwareMonitor.WinUIStartupTiming", configuredPath);
    }

    private static bool IsEnabled()
    {
        string environmentValue = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable) ?? "";
        return SettingsParsing.IsTruthy(environmentValue);
    }
}

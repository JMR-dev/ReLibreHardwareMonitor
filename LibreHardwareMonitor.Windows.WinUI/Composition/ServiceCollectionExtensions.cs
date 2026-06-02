// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Services.Tracing;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace LibreHardwareMonitor.Windows.WinUI.Composition;

/// <summary>
/// Single composition root for the application's service graph. Registers configuration, startup
/// tracing, the domain-tier services, and the main view model. Runtime/window-tied services are
/// produced separately by <c>IMainWindowRuntimeFactory</c> once the window exists.
/// </summary>
internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IStartupTracer startupTracer)
    {
        // Configuration root and the (already-created) startup tracer instance.
        services.AddSingleton(startupTracer);
        services.AddSingleton(_ => AppSettings.LoadDefault());

        // Domain tier.
        services.AddSingleton<IHardwareMonitorService, HardwareMonitorService>();
        services.AddSingleton<IComputer>(sp => sp.GetRequiredService<IHardwareMonitorService>().Computer);
        services.AddSingleton<ILogger, Logger>();
        services.AddSingleton<SensorSelectionService>();
        services.AddSingleton<PlotTrackingService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<IRemoteWebServer>(sp =>
        {
            AppSettings settings = sp.GetRequiredService<AppSettings>();
            IHardwareMonitorService hardwareMonitor = sp.GetRequiredService<IHardwareMonitorService>();
            return new RemoteWebServer(
                hardwareMonitor.Computer,
                hardwareMonitor.SensorReadLock,
                settings.GetValue("listenerIp", "?"),
                settings.GetValue("listenerPort", 8085),
                settings.GetValue("authenticationEnabled", false),
                settings.GetValue("authenticationUserName", ""),
                settings.GetValue("authenticationPassword", ""));
        });

        // View model (explicit factory so the injected internal constructor is used, never the
        // transitional convenience constructor).
        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<IHardwareMonitorService>(),
            sp.GetRequiredService<ILogger>(),
            sp.GetRequiredService<SensorSelectionService>(),
            sp.GetRequiredService<IRemoteWebServer>(),
            sp.GetRequiredService<PlotTrackingService>(),
            sp.GetRequiredService<StartupService>(),
            sp.GetRequiredService<IStartupTracer>(),
            DispatcherQueue.GetForCurrentThread()));

        // Runtime/window-tied service factory. The window itself is constructed by App (kept out of the
        // container so the XAML-tied Window is not a container-managed singleton).
        services.AddSingleton<IMainWindowRuntimeFactory, MainWindowRuntimeFactory>();

        return services;
    }
}

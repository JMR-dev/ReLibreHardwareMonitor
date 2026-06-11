// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System.Linq;
using LibreHardwareMonitor.Windows.WinUI.Composition;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Services.Tracing;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Composition;

// Guards the single composition root: every dependency referenced by a registered service must
// itself be registered, otherwise the app fails to resolve its graph at launch. ValidateOnBuild
// builds the dependency call sites without instantiating the services, so this stays off the real
// hardware-monitor / kernel-driver path while still catching missing registrations.
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAppServices_BuildsProviderWithoutMissingRegistrations()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddAppServices(NoOpStartupTracer.Instance)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.NotNull(provider);
        provider.Dispose();
    }

    [Theory]
    [InlineData(typeof(AppSettings))]
    [InlineData(typeof(IStartupTracer))]
    [InlineData(typeof(IHardwareMonitorService))]
    [InlineData(typeof(ILogger))]
    [InlineData(typeof(IRemoteWebServer))]
    [InlineData(typeof(SensorSelectionService))]
    [InlineData(typeof(PlotTrackingService))]
    [InlineData(typeof(StartupService))]
    [InlineData(typeof(MainWindowViewModel))]
    [InlineData(typeof(IMainWindowRuntimeFactory))]
    public void AddAppServices_RegistersExpectedService(System.Type serviceType)
    {
        IServiceCollection services = new ServiceCollection().AddAppServices(NoOpStartupTracer.Instance);

        Assert.Contains(services, descriptor => descriptor.ServiceType == serviceType);
    }
}

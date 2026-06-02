// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Services.Tracing;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Moq;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.ViewModels;

// Demonstrates the headline win of the DI refactor: MainWindowViewModel can now be constructed with
// faked domain services (no real hardware monitor / kernel driver), and it reads its settings and
// wires the web-server root provider during construction.
public class MainWindowViewModelTests
{
    [Fact]
    public void Constructor_WithFakedServices_ReadsSettingsAndWiresWebServerRoot()
    {
        AppSettings settings = AppSettings.LoadDefault();
        var hardwareMonitor = new Mock<IHardwareMonitorService>();
        var logger = new Mock<ILogger>();
        var webServer = new Mock<IRemoteWebServer>();

        using var viewModel = new MainWindowViewModel(
            settings,
            hardwareMonitor.Object,
            logger.Object,
            new SensorSelectionService(settings),
            webServer.Object,
            new PlotTrackingService(),
            new StartupService(),
            NoOpStartupTracer.Instance);

        Assert.True(viewModel.ShowValueColumn);
        Assert.True(viewModel.ShowMaxColumn);
        webServer.Verify(s => s.SetRootProvider(It.IsAny<Func<SensorTreeItemViewModel?>>()), Times.Once);
    }
}

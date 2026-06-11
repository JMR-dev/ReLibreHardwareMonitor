// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Moq;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.ViewModels;

public class SensorSelectionServiceTests
{
    [Fact]
    public void SetSensorInTray_PersistsReadsBack_AndRaisesEvent()
    {
        (SensorSelectionService service, _, SensorTreeItemViewModel item) = Build();
        int raised = 0;
        service.TraySensorsChanged += (_, _) => raised++;

        service.SetSensorInTray(item, true);
        Assert.True(service.IsSensorInTray(item));

        service.SetSensorInTray(item, false);
        Assert.False(service.IsSensorInTray(item));

        Assert.Equal(2, raised);
    }

    [Fact]
    public void SetSensorInGadget_PersistsReadsBack_AndRaisesEvent()
    {
        (SensorSelectionService service, _, SensorTreeItemViewModel item) = Build();
        int raised = 0;
        service.GadgetSensorsChanged += (_, _) => raised++;

        service.SetSensorInGadget(item, true);
        Assert.True(service.IsSensorInGadget(item));

        service.SetSensorInGadget(item, false);
        Assert.False(service.IsSensorInGadget(item));

        Assert.Equal(2, raised);
    }

    [Fact]
    public void GetTraySensorItems_ReturnsOnlyTrayFlaggedSensors()
    {
        (SensorSelectionService service, SensorTreeItemViewModel root, SensorTreeItemViewModel item) = Build();

        service.SetSensorInTray(item, true);
        Assert.Contains(item, service.GetTraySensorItems(root));

        service.SetSensorInTray(item, false);
        Assert.DoesNotContain(item, service.GetTraySensorItems(root));
    }

    private static (SensorSelectionService Service, SensorTreeItemViewModel Root, SensorTreeItemViewModel SensorItem) Build()
    {
        AppSettings settings = AppSettings.LoadDefault();

        var hardwareMock = new Mock<IHardware>();
        var sensorMock = new Mock<ISensor>();
        sensorMock.Setup(s => s.Name).Returns("Sensor");
        sensorMock.Setup(s => s.SensorType).Returns(SensorType.Temperature);
        sensorMock.Setup(s => s.Index).Returns(0);
        sensorMock.Setup(s => s.Identifier).Returns(new Identifier("cpu", "0", "temperature", "0"));

        hardwareMock.Setup(h => h.Name).Returns("CPU");
        hardwareMock.Setup(h => h.HardwareType).Returns(HardwareType.Cpu);
        hardwareMock.Setup(h => h.Identifier).Returns(new Identifier("cpu", "0"));
        hardwareMock.Setup(h => h.Sensors).Returns([sensorMock.Object]);
        hardwareMock.Setup(h => h.SubHardware).Returns([]);

        var root = SensorTreeItemViewModel.CreateRoot("HOST");
        root.Children.Add(SensorTreeItemViewModel.FromHardware(hardwareMock.Object, settings));
        SensorTreeItemViewModel sensorItem = root.EnumerateSensors().First();

        return (new SensorSelectionService(settings), root, sensorItem);
    }
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Moq;
using Windows.UI;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.ViewModels;

public class PlotTrackingServiceTests
{
    [Fact]
    public void Track_AddsSeriesForPlottedSensor()
    {
        (PlotTrackingService service, SensorTreeItemViewModel root, _, Mock<ISensor> sensorMock) = Plotted(SensorType.Load, currentValue: 50f);

        service.Track(root, TemperatureUnit.Celsius);

        Assert.Single(service.Series);
        Assert.Equal(sensorMock.Object.Identifier.ToString(), service.Series[0].SensorIdentifier);
    }

    [Fact]
    public void Track_IgnoresSensorsNotFlaggedForPlotting()
    {
        (PlotTrackingService service, SensorTreeItemViewModel root, SensorTreeItemViewModel sensorItem, _) = Plotted(SensorType.Load, currentValue: 50f);
        sensorItem.Plot = false;

        service.Track(root, TemperatureUnit.Celsius);

        Assert.Empty(service.Series);
    }

    [Fact]
    public void Track_RemovesSeriesWhenSensorDeselected()
    {
        (PlotTrackingService service, SensorTreeItemViewModel root, SensorTreeItemViewModel sensorItem, _) = Plotted(SensorType.Load, currentValue: 50f);

        service.Track(root, TemperatureUnit.Celsius);
        Assert.Single(service.Series);

        sensorItem.Plot = false;
        service.Track(root, TemperatureUnit.Celsius);
        Assert.Empty(service.Series);
    }

    [Fact]
    public void Track_IncludesStoredHistoryThenCurrentValue()
    {
        DateTime now = DateTime.UtcNow;
        SensorValue[] history = [new SensorValue(10f, now.AddSeconds(-3)), new SensorValue(20f, now.AddSeconds(-2))];
        (PlotTrackingService service, SensorTreeItemViewModel root, _, _) = Plotted(SensorType.Load, currentValue: 30f, values: history);

        service.Track(root, TemperatureUnit.Celsius);

        double[] values = service.Series[0].Points.Select(point => point.Value).ToArray();
        Assert.Equal([10d, 20d, 30d], values);
    }

    [Fact]
    public void Track_DeduplicatesPointsWithSameTimestamp()
    {
        DateTime time = DateTime.UtcNow.AddSeconds(-5);
        SensorValue[] history = [new SensorValue(10f, time), new SensorValue(99f, time)];
        (PlotTrackingService service, SensorTreeItemViewModel root, _, _) = Plotted(SensorType.Load, currentValue: null, values: history);

        service.Track(root, TemperatureUnit.Celsius);

        PlotPointViewModel point = Assert.Single(service.Series[0].Points);
        Assert.Equal(99d, point.Value); // the last value sharing that timestamp wins
    }

    [Fact]
    public void Track_AppliesFahrenheitConversion()
    {
        (PlotTrackingService service, SensorTreeItemViewModel root, _, _) = Plotted(SensorType.Temperature, currentValue: 100f);

        service.Track(root, TemperatureUnit.Fahrenheit);

        PlotPointViewModel point = Assert.Single(service.Series[0].Points);
        Assert.Equal(212d, point.Value, 1); // 100 °C == 212 °F
        Assert.Equal("°F", service.Series[0].Unit);
    }

    [Fact]
    public void Reset_ClearsSeriesAndAllowsReTracking()
    {
        (PlotTrackingService service, SensorTreeItemViewModel root, _, _) = Plotted(SensorType.Load, currentValue: 50f);
        service.Track(root, TemperatureUnit.Celsius);

        service.Reset();
        Assert.Empty(service.Series);

        service.Track(root, TemperatureUnit.Celsius);
        Assert.Single(service.Series);
    }

    [Fact]
    public void RefreshSeriesColor_AppliesUserPenColor()
    {
        (PlotTrackingService service, SensorTreeItemViewModel root, SensorTreeItemViewModel sensorItem, _) = Plotted(SensorType.Load, currentValue: 50f);
        service.Track(root, TemperatureUnit.Celsius);

        Color userColor = Color.FromArgb(255, 1, 2, 3);
        sensorItem.PenColor = userColor;
        service.RefreshSeriesColor(sensorItem);

        Assert.Equal(userColor, service.Series[0].Color);
    }

    private static (PlotTrackingService Service, SensorTreeItemViewModel Root, SensorTreeItemViewModel SensorItem, Mock<ISensor> SensorMock)
        Plotted(SensorType sensorType, float? currentValue, params SensorValue[] values)
    {
        var hardwareMock = new Mock<IHardware>();
        var sensorMock = new Mock<ISensor>();

        sensorMock.Setup(s => s.Name).Returns("Sensor");
        sensorMock.Setup(s => s.SensorType).Returns(sensorType);
        sensorMock.Setup(s => s.Index).Returns(0);
        sensorMock.Setup(s => s.Identifier).Returns(new Identifier("cpu", "0", sensorType.ToString().ToLowerInvariant(), "0"));
        sensorMock.Setup(s => s.Hardware).Returns(hardwareMock.Object);
        sensorMock.Setup(s => s.Value).Returns(currentValue);
        sensorMock.Setup(s => s.Values).Returns(values);

        hardwareMock.Setup(h => h.Name).Returns("CPU");
        hardwareMock.Setup(h => h.HardwareType).Returns(HardwareType.Cpu);
        hardwareMock.Setup(h => h.Identifier).Returns(new Identifier("cpu", "0"));
        hardwareMock.Setup(h => h.Sensors).Returns([sensorMock.Object]);
        hardwareMock.Setup(h => h.SubHardware).Returns([]);

        var root = SensorTreeItemViewModel.CreateRoot("HOST");
        root.Children.Add(SensorTreeItemViewModel.FromHardware(hardwareMock.Object, AppSettings.LoadDefault()));
        SensorTreeItemViewModel sensorItem = root.EnumerateSensors().First();
        sensorItem.Plot = true;

        return (new PlotTrackingService(), root, sensorItem, sensorMock);
    }
}

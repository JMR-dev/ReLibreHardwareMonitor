using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Moq;
using Xunit;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.ViewModels;

public class SensorTreeItemViewModelTests
{
    private AppSettings CreateMockSettings()
    {
        return AppSettings.LoadDefault();
    }

    [Fact]
    public void CreateRoot_ReturnsRootKind()
    {
        var vm = SensorTreeItemViewModel.CreateRoot("Root Node");
        Assert.Equal(SensorTreeItemKind.Root, vm.Kind);
        Assert.Equal("Root Node", vm.Text);
        Assert.Null(vm.Sensor);
        Assert.Null(vm.Hardware);
        Assert.Equal("\uE7F4", vm.IconGlyph);
        Assert.True(vm.IsExpanded);
        Assert.Empty(vm.Children);
    }

    [Fact]
    public void Text_CanBeSetForRoot()
    {
        var vm = SensorTreeItemViewModel.CreateRoot("Root");
        vm.Text = "New Root";
        Assert.Equal("New Root", vm.Text);
    }

    [Fact]
    public void FromHardware_CreatesHierarchy()
    {
        var mockHardware = new Mock<IHardware>();
        mockHardware.Setup(h => h.Name).Returns("My Hardware");
        mockHardware.Setup(h => h.HardwareType).Returns(HardwareType.Cpu);
        mockHardware.Setup(h => h.Identifier).Returns(new Identifier("cpu", "0"));

        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.Name).Returns("Core 0");
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Temperature);
        mockSensor.Setup(s => s.Index).Returns(0);
        mockSensor.Setup(s => s.Identifier).Returns(new Identifier("cpu", "0", "temperature", "0"));
        
        mockHardware.Setup(h => h.Sensors).Returns(new[] { mockSensor.Object });
        mockHardware.Setup(h => h.SubHardware).Returns(Array.Empty<IHardware>());

        var settings = CreateMockSettings();
        var vm = SensorTreeItemViewModel.FromHardware(mockHardware.Object, settings);

        Assert.Equal(SensorTreeItemKind.Hardware, vm.Kind);
        Assert.Equal("My Hardware", vm.Text);
        Assert.Same(mockHardware.Object, vm.Hardware);
        Assert.Single(vm.Children); // One SensorType group
        
        var typeGroup = vm.Children[0];
        Assert.Equal(SensorTreeItemKind.SensorType, typeGroup.Kind);
        Assert.Equal("Temperatures", typeGroup.Text);
        Assert.Single(typeGroup.Children); // One sensor

        var sensorVm = typeGroup.Children[0];
        Assert.Equal(SensorTreeItemKind.Sensor, sensorVm.Kind);
        Assert.Equal("Core 0", sensorVm.Text);
        Assert.Same(mockSensor.Object, sensorVm.Sensor);
    }

    [Fact]
    public void Plot_SetsPlotPropertyAndSettings()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.Identifier).Returns(new Identifier("cpu", "0"));
        
        var mockHardware = new Mock<IHardware>();
        mockHardware.Setup(h => h.Identifier).Returns(new Identifier("hw"));
        mockHardware.Setup(h => h.Sensors).Returns(new[] { mockSensor.Object });
        mockHardware.Setup(h => h.SubHardware).Returns(Array.Empty<IHardware>());

        var settings = CreateMockSettings();
        var vm = SensorTreeItemViewModel.FromHardware(mockHardware.Object, settings);
        
        // Root has no sensor, so Plot won't do anything
        vm.Plot = true;
        Assert.False(vm.Plot);

        // Get SensorVM
        var sensorVm = vm.Children.First().Children.First();
        sensorVm.Plot = true;
        Assert.True(sensorVm.Plot);
    }

    [Fact]
    public void PenColor_SetsPropertyAndSettings()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.Identifier).Returns(new Identifier("cpu", "0"));

        var mockHardware = new Mock<IHardware>();
        mockHardware.Setup(h => h.Sensors).Returns(new[] { mockSensor.Object });
        mockHardware.Setup(h => h.Identifier).Returns(new Identifier("hw"));
        mockHardware.Setup(h => h.SubHardware).Returns(Array.Empty<IHardware>());

        var settings = CreateMockSettings();
        var vm = SensorTreeItemViewModel.FromHardware(mockHardware.Object, settings);
        var sensorVm = vm.Children.First().Children.First();

        Assert.Null(sensorVm.PenColor);

        var color = Color.FromArgb(255, 100, 100, 100);
        sensorVm.PenColor = color;
        Assert.Equal(color, sensorVm.PenColor);

        sensorVm.PenColor = null;
        Assert.Null(sensorVm.PenColor);
    }

    [Fact]
    public void SetShowHiddenSensors_UpdatesVisibility()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.Identifier).Returns(new Identifier("cpu", "0"));
        
        var mockHardware = new Mock<IHardware>();
        mockHardware.Setup(h => h.Sensors).Returns(new[] { mockSensor.Object });
        mockHardware.Setup(h => h.Identifier).Returns(new Identifier("hw"));
        mockHardware.Setup(h => h.SubHardware).Returns(Array.Empty<IHardware>());

        var settings = CreateMockSettings();
        var vm = SensorTreeItemViewModel.FromHardware(mockHardware.Object, settings);
        var typeGroup = vm.Children.First();
        var sensorVm = typeGroup.Children.First();

        sensorVm.IsVisible = false;
        Assert.Equal(Visibility.Collapsed, sensorVm.RowVisibility);

        // Hiding the group's only visible sensor now collapses the parent group too (no empty group header left behind).
        Assert.Equal(Visibility.Collapsed, typeGroup.RowVisibility);

        vm.SetShowHiddenSensors(true);

        Assert.Equal(Visibility.Visible, sensorVm.RowVisibility);
        Assert.Equal(Visibility.Visible, typeGroup.RowVisibility);
    }

    [Fact]
    public void SetColumnVisibility_UpdatesAllChildren()
    {
        var root = SensorTreeItemViewModel.CreateRoot("Root");
        
        root.SetColumnVisibility(false, true, true);
        
        Assert.Equal(Visibility.Collapsed, root.ValueColumnVisibility);
        Assert.Equal(Visibility.Visible, root.MinColumnVisibility);
        Assert.Equal(Visibility.Visible, root.MaxColumnVisibility);
    }

    [Fact]
    public void RefreshValues_UpdatesProperties()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.Identifier).Returns(new Identifier("cpu", "0"));
        mockSensor.Setup(s => s.Value).Returns(50f);

        var mockHardware = new Mock<IHardware>();
        mockHardware.Setup(h => h.Sensors).Returns(new[] { mockSensor.Object });
        mockHardware.Setup(h => h.Identifier).Returns(new Identifier("hw"));
        mockHardware.Setup(h => h.SubHardware).Returns(Array.Empty<IHardware>());

        var vm = SensorTreeItemViewModel.FromHardware(mockHardware.Object, CreateMockSettings());
        var sensorVm = vm.Children.First().Children.First();
        
        mockSensor.Setup(s => s.Value).Returns(60f); // change value
        sensorVm.RefreshValues(); // This triggers PropertyChanged

        Assert.Contains("60.0", sensorVm.Value);
    }

    [Fact]
    public void RefreshValues_RaisesDisplayColumnChangeOnlyForChangedValue()
    {
        float? value = 50f;
        SensorTreeItemViewModel sensorVm = CreateTemperatureSensorViewModel(() => value, () => 10f, () => 100f);
        sensorVm.RefreshValues();
        SensorDisplayColumn changedColumns = SensorDisplayColumn.None;
        sensorVm.DisplayColumnsChanged += (_, columns) => changedColumns |= columns;

        value = 60f;
        sensorVm.RefreshValues();

        Assert.Equal(SensorDisplayColumn.Value, changedColumns);
    }

    [Fact]
    public void RefreshValues_DoesNotRaiseDisplayColumnChangeWhenFormattedValuesAreUnchanged()
    {
        SensorTreeItemViewModel sensorVm = CreateTemperatureSensorViewModel(() => 50f, () => 10f, () => 100f);
        sensorVm.RefreshValues();
        SensorDisplayColumn changedColumns = SensorDisplayColumn.None;
        sensorVm.DisplayColumnsChanged += (_, columns) => changedColumns |= columns;

        sensorVm.RefreshValues();

        Assert.Equal(SensorDisplayColumn.None, changedColumns);
    }

    [Fact]
    public void Text_RaisesSensorDisplayColumnChange()
    {
        SensorTreeItemViewModel sensorVm = CreateTemperatureSensorViewModel(() => 50f, () => 10f, () => 100f);
        SensorDisplayColumn changedColumns = SensorDisplayColumn.None;
        sensorVm.DisplayColumnsChanged += (_, columns) => changedColumns |= columns;

        sensorVm.Text = "Renamed Core";

        Assert.Equal(SensorDisplayColumn.Sensor, changedColumns);
    }

    [Fact]
    public void SetTemperatureUnit_RaisesValueMinAndMaxDisplayColumnChanges()
    {
        SensorTreeItemViewModel sensorVm = CreateTemperatureSensorViewModel(() => 50f, () => 10f, () => 100f);
        sensorVm.RefreshValues();
        SensorDisplayColumn changedColumns = SensorDisplayColumn.None;
        sensorVm.DisplayColumnsChanged += (_, columns) => changedColumns |= columns;

        sensorVm.SetTemperatureUnit(TemperatureUnit.Fahrenheit);

        Assert.Equal(SensorDisplayColumn.Value | SensorDisplayColumn.Min | SensorDisplayColumn.Max, changedColumns);
    }

    private SensorTreeItemViewModel CreateTemperatureSensorViewModel(Func<float?> value, Func<float?> min, Func<float?> max)
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.SetupProperty(s => s.Name, "Core 0");
        mockSensor.Setup(s => s.Identifier).Returns(new Identifier("cpu", "0", "temperature", "0"));
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Temperature);
        mockSensor.Setup(s => s.Index).Returns(0);
        mockSensor.Setup(s => s.Value).Returns(value);
        mockSensor.Setup(s => s.Min).Returns(min);
        mockSensor.Setup(s => s.Max).Returns(max);

        var mockHardware = new Mock<IHardware>();
        mockHardware.Setup(s => s.Name).Returns("CPU");
        mockHardware.Setup(h => h.HardwareType).Returns(HardwareType.Cpu);
        mockHardware.Setup(h => h.Identifier).Returns(new Identifier("cpu", "0"));
        mockHardware.Setup(h => h.Sensors).Returns(new[] { mockSensor.Object });
        mockHardware.Setup(h => h.SubHardware).Returns(Array.Empty<IHardware>());

        return SensorTreeItemViewModel.FromHardware(mockHardware.Object, CreateMockSettings()).Children.First().Children.First();
    }
}

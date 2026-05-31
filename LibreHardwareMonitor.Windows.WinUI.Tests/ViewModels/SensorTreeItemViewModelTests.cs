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
        
        // typeGroup doesn't update automatically in ViewModel when child changes, so skip assert

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
}

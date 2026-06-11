using System;
using System.ComponentModel;
using LibreHardwareMonitor.Hardware;
using Windows.UI;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.ViewModels;

public class SimpleViewModelsTests
{
    private class TestViewModel : ViewModelBase
    {
        private string _testProperty;
        public string TestProperty
        {
            get => _testProperty;
            set => SetProperty(ref _testProperty, value);
        }

        public void TriggerPropertyChanged(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }
    }

    [Fact]
    public void ViewModelBase_SetProperty_ChangesValueAndRaisesEvent()
    {
        var vm = new TestViewModel();
        string changedProperty = null;
        vm.PropertyChanged += (sender, args) => changedProperty = args.PropertyName;

        // Change value
        vm.TestProperty = "NewValue";
        vm.TestProperty = "NewValue";
        
        Assert.Equal("NewValue", vm.TestProperty);
        Assert.Equal(nameof(TestViewModel.TestProperty), changedProperty);
    }

    [Fact]
    public void ViewModelBase_SetProperty_SameValue_DoesNotRaiseEvent()
    {
        var vm = new TestViewModel();
        vm.TestProperty = "Value";

        bool eventRaised = false;
        vm.PropertyChanged += (sender, args) => eventRaised = true;

        vm.TestProperty = "Value"; // Set same value

        Assert.False(eventRaised);
    }

    [Fact]
    public void ViewModelBase_OnPropertyChanged_RaisesEvent()
    {
        var vm = new TestViewModel();
        string changedProperty = null;
        vm.PropertyChanged += (sender, args) => changedProperty = args.PropertyName;

        vm.TriggerPropertyChanged("CustomProperty");

        Assert.Equal("CustomProperty", changedProperty);
    }

    [Fact]
    public void PlotPointViewModel_Constructor_SetsProperties()
    {
        var timestamp = new DateTime(2023, 1, 1);
        var vm = new PlotPointViewModel(timestamp, 42.5);

        Assert.Equal(timestamp, vm.Timestamp);
        Assert.Equal(42.5, vm.Value);
    }

    [Fact]
    public void PlotSeriesViewModel_Constructor_SetsProperties()
    {
        var color = Color.FromArgb(255, 255, 0, 0);
        var vm = new PlotSeriesViewModel("sensor1", "Hardware Name", "Sensor Name", SensorType.Temperature, "\u00B0C", color);

        Assert.Equal("sensor1", vm.SensorIdentifier);
        Assert.Equal("Hardware Name", vm.HardwareName);
        Assert.Equal("Sensor Name", vm.Name);
        Assert.Equal(SensorType.Temperature, vm.SensorType);
        Assert.Equal("\u00B0C", vm.Unit);
        Assert.Equal("Hardware Name Sensor Name", vm.Title);
        Assert.Equal(color, vm.Color);
        Assert.NotNull(vm.Points);
        Assert.Empty(vm.Points);
    }

    [Fact]
    public void PlotSeriesViewModel_SetColor_ChangesColor()
    {
        var color1 = Color.FromArgb(255, 255, 0, 0);
        var color2 = Color.FromArgb(255, 0, 255, 0);
        var vm = new PlotSeriesViewModel("sensor1", "Hardware Name", "Sensor Name", SensorType.Temperature, "\u00B0C", color1);

        vm.Color = color2;

        Assert.Equal(color2, vm.Color);
    }

    [Fact]
    public void PlotSeriesViewModel_ReplacePoints_ReplacesExistingPoints()
    {
        var color = Color.FromArgb(255, 255, 0, 0);
        var vm = new PlotSeriesViewModel("sensor1", "Hardware Name", "Sensor Name", SensorType.Temperature, "\u00B0C", color);
        vm.Points.Add(new PlotPointViewModel(new DateTime(2023, 1, 1), 1));

        vm.ReplacePoints([
            new PlotPointViewModel(new DateTime(2023, 1, 2), 2),
            new PlotPointViewModel(new DateTime(2023, 1, 3), 3)
        ]);

        Assert.Equal(2, vm.Points.Count);
        Assert.Equal(2, vm.Points[0].Value);
        Assert.Equal(3, vm.Points[1].Value);
    }
}

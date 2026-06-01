using System;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Moq;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Utilities;

public class SensorFormatterTests
{
    [Fact]
    public void GetFormatString_ReturnsCorrectFormat_ForAllSensorTypes()
    {
        var mockSensor = new Mock<ISensor>();

        // Test a few specific types
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Voltage);
        Assert.Equal("{0:F3} V", SensorFormatter.GetFormatString(mockSensor.Object));

        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Load);
        Assert.Equal("{0:F1} %", SensorFormatter.GetFormatString(mockSensor.Object));

        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Temperature);
        Assert.Equal("{0:F1} \u00B0C", SensorFormatter.GetFormatString(mockSensor.Object));

        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Fan);
        Assert.Equal("{0:F0} RPM", SensorFormatter.GetFormatString(mockSensor.Object));

        // Test default case
        mockSensor.Setup(s => s.SensorType).Returns((SensorType)999);
        Assert.Equal("{0:F1}", SensorFormatter.GetFormatString(mockSensor.Object));
    }

    [Fact]
    public void FormatValue_NullValue_ReturnsDash()
    {
        var mockSensor = new Mock<ISensor>();
        var result = SensorFormatter.FormatValue(mockSensor.Object, null, TemperatureUnit.Celsius);
        Assert.Equal("-", result);
    }

    [Fact]
    public void FormatValue_Celsius_ReturnsCelsiusString()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Temperature);

        var result = SensorFormatter.FormatValue(mockSensor.Object, 25.5f, TemperatureUnit.Celsius);
        Assert.Equal("25.5 \u00B0C", result);
    }

    [Fact]
    public void FormatValue_Fahrenheit_ReturnsFahrenheitString()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Temperature);

        var result = SensorFormatter.FormatValue(mockSensor.Object, 25.5f, TemperatureUnit.Fahrenheit);
        // 25.5 * 1.8 + 32 = 77.9
        Assert.Equal("77.9 \u00B0F", result);
    }

    [Fact]
    public void FormatValue_Throughput_ReturnsCorrectString()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Throughput);
        mockSensor.Setup(s => s.Name).Returns("Other");

        // Below 1MB
        var result1 = SensorFormatter.FormatValue(mockSensor.Object, 512f, TemperatureUnit.Celsius);
        Assert.Equal("0.5 KB/s", result1);

        // Above 1MB
        var result2 = SensorFormatter.FormatValue(mockSensor.Object, 2097152f, TemperatureUnit.Celsius);
        Assert.Equal("2.0 MB/s", result2);
    }

    [Fact]
    public void FormatValue_Throughput_ConnectionSpeed_ReturnsCorrectString()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Throughput);
        mockSensor.Setup(s => s.Name).Returns("Connection Speed");

        Assert.Equal("100Mbps", SensorFormatter.FormatValue(mockSensor.Object, 100000000f, TemperatureUnit.Celsius));
        Assert.Equal("1Gbps", SensorFormatter.FormatValue(mockSensor.Object, 1000000000f, TemperatureUnit.Celsius));
        Assert.Equal("500 bps", SensorFormatter.FormatValue(mockSensor.Object, 500f, TemperatureUnit.Celsius));
        Assert.Equal("2.0 Kbps", SensorFormatter.FormatValue(mockSensor.Object, 2048f, TemperatureUnit.Celsius));
        Assert.Equal("2.0 Mbps", SensorFormatter.FormatValue(mockSensor.Object, 2097152f, TemperatureUnit.Celsius));
        Assert.Equal("2.0 Gbps", SensorFormatter.FormatValue(mockSensor.Object, 2147483648f, TemperatureUnit.Celsius));
    }

    [Fact]
    public void FormatValue_TimeSpan_ReturnsCorrectString()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.TimeSpan);

        var result = SensorFormatter.FormatValue(mockSensor.Object, 3600f, TemperatureUnit.Celsius);
        Assert.Equal("1:00:00", result); // g format for 1 hour
    }

    [Fact]
    public void FormatValue_Default_ReturnsFormattedFloat()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns((SensorType)999);

        var result = SensorFormatter.FormatValue(mockSensor.Object, 12.34f, TemperatureUnit.Celsius);
        Assert.Equal("12.3", result);
    }

    [Fact]
    public void GetPlotValue_NullValue_ReturnsNull()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.Value).Returns((float?)null);

        var result = SensorFormatter.GetPlotValue(mockSensor.Object, TemperatureUnit.Celsius);
        Assert.Null(result);
    }

    [Fact]
    public void GetPlotValue_TemperatureFahrenheit_ReturnsConvertedValue()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.Value).Returns(25.5f);
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Temperature);

        var result = SensorFormatter.GetPlotValue(mockSensor.Object, TemperatureUnit.Fahrenheit);
        Assert.Equal(77.9, result.Value, 1);
    }

    [Fact]
    public void GetPlotValue_OtherSensor_ReturnsRawValue()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.Value).Returns(25.5f);
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Voltage);

        var result = SensorFormatter.GetPlotValue(mockSensor.Object, TemperatureUnit.Celsius);
        Assert.Equal(25.5f, result);
    }

    [Fact]
    public void GetPlotValue_HistoricalTemperatureFahrenheit_ReturnsConvertedValue()
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Temperature);

        var result = SensorFormatter.GetPlotValue(mockSensor.Object, 25.5f, TemperatureUnit.Fahrenheit);

        Assert.Equal(77.9, result.Value, 1);
    }

    [Fact]
    public void GetPlotUnit_TemperatureFahrenheit_ReturnsFahrenheitUnit()
    {
        Assert.Equal("\u00B0F", SensorFormatter.GetPlotUnit(SensorType.Temperature, TemperatureUnit.Fahrenheit));
    }

    [Fact]
    public void GetPlotUnit_Load_ReturnsPercentUnit()
    {
        Assert.Equal("%", SensorFormatter.GetPlotUnit(SensorType.Load, TemperatureUnit.Celsius));
    }

    // A mock interface combining ISensor and ICriticalSensorLimits for testing
    public interface ICriticalSensorMock : ISensor, ICriticalSensorLimits, ISensorLimits
    {
    }

    [Fact]
    public void GetToolTip_WithLimits_ReturnsFormattedToolTip()
    {
        var mockSensor = new Mock<ICriticalSensorMock>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Temperature);
        mockSensor.Setup(s => s.CriticalLowLimit).Returns(10f);
        mockSensor.Setup(s => s.CriticalHighLimit).Returns(90f);
        mockSensor.Setup(s => s.LowLimit).Returns(20f);
        mockSensor.Setup(s => s.HighLimit).Returns(80f);

        var result = SensorFormatter.GetToolTip(mockSensor.Object, TemperatureUnit.Celsius);

        Assert.Contains("Critical range: 10.0 \u00B0C to 90.0 \u00B0C.", result);
        Assert.Contains("Normal range: 20.0 \u00B0C to 80.0 \u00B0C.", result);
    }

    [Fact]
    public void GetToolTip_WithOnlyMinLimit_ReturnsFormattedToolTip()
    {
        var mockSensor = new Mock<ICriticalSensorMock>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Voltage);
        mockSensor.Setup(s => s.CriticalLowLimit).Returns(1.0f);
        mockSensor.Setup(s => s.CriticalHighLimit).Returns((float?)null);

        var result = SensorFormatter.GetToolTip(mockSensor.Object, TemperatureUnit.Celsius);

        Assert.Contains("Minimal critical value: 1.000 V.", result);
    }

    [Fact]
    public void GetToolTip_WithOnlyMaxLimit_ReturnsFormattedToolTip()
    {
        var mockSensor = new Mock<ICriticalSensorMock>();
        mockSensor.Setup(s => s.SensorType).Returns(SensorType.Voltage);
        mockSensor.Setup(s => s.CriticalLowLimit).Returns((float?)null);
        mockSensor.Setup(s => s.CriticalHighLimit).Returns(2.0f);

        var result = SensorFormatter.GetToolTip(mockSensor.Object, TemperatureUnit.Celsius);

        Assert.Contains("Maximal critical value: 2.000 V.", result);
    }
}

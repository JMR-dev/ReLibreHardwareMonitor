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

    // Characterization: pins the GetFormatString mapping for every SensorType so the
    // three parallel SensorType switches can be safely collapsed into one lookup table.
    [Theory]
    [InlineData(SensorType.Voltage, "{0:F3} V")]
    [InlineData(SensorType.Current, "{0:F3} A")]
    [InlineData(SensorType.Clock, "{0:F1} MHz")]
    [InlineData(SensorType.Load, "{0:F1} %")]
    [InlineData(SensorType.Temperature, "{0:F1} °C")]
    [InlineData(SensorType.Fan, "{0:F0} RPM")]
    [InlineData(SensorType.Flow, "{0:F1} L/h")]
    [InlineData(SensorType.Control, "{0:F1} %")]
    [InlineData(SensorType.Level, "{0:F1} %")]
    [InlineData(SensorType.Power, "{0:F1} W")]
    [InlineData(SensorType.Data, "{0:F1} GB")]
    [InlineData(SensorType.SmallData, "{0:F1} MB")]
    [InlineData(SensorType.Factor, "{0:F3}")]
    [InlineData(SensorType.Frequency, "{0:F1} Hz")]
    [InlineData(SensorType.Throughput, "{0:F1} B/s")]
    [InlineData(SensorType.TimeSpan, "{0:g}")]
    [InlineData(SensorType.Timing, "{0:F3} ns")]
    [InlineData(SensorType.Energy, "{0:F0} mWh")]
    [InlineData(SensorType.Noise, "{0:F0} dBA")]
    [InlineData(SensorType.Conductivity, "{0:F1} µS/cm")]
    [InlineData(SensorType.Humidity, "{0:F0} %")]
    [InlineData((SensorType)999, "{0:F1}")]
    public void GetFormatString_AllSensorTypes(SensorType sensorType, string expected)
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns(sensorType);
        Assert.Equal(expected, SensorFormatter.GetFormatString(mockSensor.Object));
    }

    // Characterization: pins the GetPlotUnit mapping for every SensorType (Celsius).
    [Theory]
    [InlineData(SensorType.Voltage, "V")]
    [InlineData(SensorType.Current, "A")]
    [InlineData(SensorType.Clock, "MHz")]
    [InlineData(SensorType.Load, "%")]
    [InlineData(SensorType.Temperature, "°C")]
    [InlineData(SensorType.Fan, "RPM")]
    [InlineData(SensorType.Flow, "L/h")]
    [InlineData(SensorType.Control, "%")]
    [InlineData(SensorType.Level, "%")]
    [InlineData(SensorType.Power, "W")]
    [InlineData(SensorType.Data, "GB")]
    [InlineData(SensorType.SmallData, "MB")]
    [InlineData(SensorType.Factor, "1")]
    [InlineData(SensorType.Frequency, "Hz")]
    [InlineData(SensorType.Throughput, "B/s")]
    [InlineData(SensorType.TimeSpan, "s")]
    [InlineData(SensorType.Timing, "ns")]
    [InlineData(SensorType.Energy, "mWh")]
    [InlineData(SensorType.Noise, "dBA")]
    [InlineData(SensorType.Conductivity, "µS/cm")]
    [InlineData(SensorType.Humidity, "%")]
    [InlineData((SensorType)999, "")]
    public void GetPlotUnit_AllSensorTypes_Celsius(SensorType sensorType, string expected)
    {
        Assert.Equal(expected, SensorFormatter.GetPlotUnit(sensorType, TemperatureUnit.Celsius));
    }

    // Characterization: pins FormatValue for the "regular" {0:Fn} unit types (Celsius).
    // Throughput, TimeSpan, and Temperature-Fahrenheit have their own dedicated tests.
    [Theory]
    [InlineData(SensorType.Voltage, 1.234f, "1.234 V")]
    [InlineData(SensorType.Current, 2.5f, "2.500 A")]
    [InlineData(SensorType.Clock, 3500f, "3500.0 MHz")]
    [InlineData(SensorType.Load, 42.5f, "42.5 %")]
    [InlineData(SensorType.Temperature, 50.5f, "50.5 °C")]
    [InlineData(SensorType.Fan, 1200f, "1200 RPM")]
    [InlineData(SensorType.Flow, 10.5f, "10.5 L/h")]
    [InlineData(SensorType.Control, 75.5f, "75.5 %")]
    [InlineData(SensorType.Level, 60.5f, "60.5 %")]
    [InlineData(SensorType.Power, 95.5f, "95.5 W")]
    [InlineData(SensorType.Data, 8.5f, "8.5 GB")]
    [InlineData(SensorType.SmallData, 256.5f, "256.5 MB")]
    [InlineData(SensorType.Factor, 1.234f, "1.234")]
    [InlineData(SensorType.Frequency, 60.5f, "60.5 Hz")]
    [InlineData(SensorType.Timing, 1.234f, "1.234 ns")]
    [InlineData(SensorType.Energy, 1500f, "1500 mWh")]
    [InlineData(SensorType.Noise, 45f, "45 dBA")]
    [InlineData(SensorType.Conductivity, 12.5f, "12.5 µS/cm")]
    [InlineData(SensorType.Humidity, 55f, "55 %")]
    [InlineData((SensorType)999, 12.34f, "12.3")]
    public void FormatValue_RegularTypes_Celsius(SensorType sensorType, float value, string expected)
    {
        var mockSensor = new Mock<ISensor>();
        mockSensor.Setup(s => s.SensorType).Returns(sensorType);
        Assert.Equal(expected, SensorFormatter.FormatValue(mockSensor.Object, value, TemperatureUnit.Celsius));
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
        Assert.NotNull(result);
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

        Assert.NotNull(result);
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

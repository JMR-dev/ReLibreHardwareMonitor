using System;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Utilities;

public class SensorTypeDisplayTests
{
    [Fact]
    public void GetText_ReturnsCorrectString_ForAllKnownTypes()
    {
        Assert.Equal("Voltages", SensorTypeDisplay.GetText(SensorType.Voltage));
        Assert.Equal("Currents", SensorTypeDisplay.GetText(SensorType.Current));
        Assert.Equal("Clocks", SensorTypeDisplay.GetText(SensorType.Clock));
        Assert.Equal("Load", SensorTypeDisplay.GetText(SensorType.Load));
        Assert.Equal("Temperatures", SensorTypeDisplay.GetText(SensorType.Temperature));
        Assert.Equal("Fans", SensorTypeDisplay.GetText(SensorType.Fan));
        Assert.Equal("Flows", SensorTypeDisplay.GetText(SensorType.Flow));
        Assert.Equal("Controls", SensorTypeDisplay.GetText(SensorType.Control));
        Assert.Equal("Levels", SensorTypeDisplay.GetText(SensorType.Level));
        Assert.Equal("Powers", SensorTypeDisplay.GetText(SensorType.Power));
        Assert.Equal("Data", SensorTypeDisplay.GetText(SensorType.Data));
        Assert.Equal("Data", SensorTypeDisplay.GetText(SensorType.SmallData));
        Assert.Equal("Factors", SensorTypeDisplay.GetText(SensorType.Factor));
        Assert.Equal("Frequencies", SensorTypeDisplay.GetText(SensorType.Frequency));
        Assert.Equal("Throughput", SensorTypeDisplay.GetText(SensorType.Throughput));
        Assert.Equal("Times", SensorTypeDisplay.GetText(SensorType.TimeSpan));
        Assert.Equal("Timings", SensorTypeDisplay.GetText(SensorType.Timing));
        Assert.Equal("Capacities", SensorTypeDisplay.GetText(SensorType.Energy));
        Assert.Equal("Noise Levels", SensorTypeDisplay.GetText(SensorType.Noise));
        Assert.Equal("Conductivities", SensorTypeDisplay.GetText(SensorType.Conductivity));
        Assert.Equal("Humidity Levels", SensorTypeDisplay.GetText(SensorType.Humidity));
    }

    [Fact]
    public void GetText_ReturnsToString_ForUnknownType()
    {
        SensorType unknownType = (SensorType)999;
        Assert.Equal("999", SensorTypeDisplay.GetText(unknownType));
    }

    [Fact]
    public void GetGlyph_ReturnsCorrectGlyph_ForAllKnownTypes()
    {
        Assert.Equal("\uE945", SensorTypeDisplay.GetGlyph(SensorType.Voltage));
        Assert.Equal("\uE945", SensorTypeDisplay.GetGlyph(SensorType.Current));
        Assert.Equal("\uE916", SensorTypeDisplay.GetGlyph(SensorType.Clock));
        Assert.Equal("\uE9D9", SensorTypeDisplay.GetGlyph(SensorType.Load));
        Assert.Equal("\uE9CA", SensorTypeDisplay.GetGlyph(SensorType.Temperature));
        Assert.Equal("\uE9F3", SensorTypeDisplay.GetGlyph(SensorType.Fan));
        Assert.Equal("\uE9D5", SensorTypeDisplay.GetGlyph(SensorType.Flow));
        Assert.Equal("\uE713", SensorTypeDisplay.GetGlyph(SensorType.Control));
        Assert.Equal("\uE9D2", SensorTypeDisplay.GetGlyph(SensorType.Level));
        Assert.Equal("\uE7E8", SensorTypeDisplay.GetGlyph(SensorType.Power));
        Assert.Equal("\uE8AB", SensorTypeDisplay.GetGlyph(SensorType.Data));
        Assert.Equal("\uE8AB", SensorTypeDisplay.GetGlyph(SensorType.SmallData));
        Assert.Equal("\uE9D2", SensorTypeDisplay.GetGlyph(SensorType.Factor));
        Assert.Equal("\uE916", SensorTypeDisplay.GetGlyph(SensorType.Frequency));
        Assert.Equal("\uE9D5", SensorTypeDisplay.GetGlyph(SensorType.Throughput));
        Assert.Equal("\uE121", SensorTypeDisplay.GetGlyph(SensorType.TimeSpan));
        Assert.Equal("\uE121", SensorTypeDisplay.GetGlyph(SensorType.Timing));
        Assert.Equal("\uEBAA", SensorTypeDisplay.GetGlyph(SensorType.Energy));
        Assert.Equal("\uE767", SensorTypeDisplay.GetGlyph(SensorType.Noise));
        Assert.Equal("\uE945", SensorTypeDisplay.GetGlyph(SensorType.Conductivity));
        Assert.Equal("\uE9CA", SensorTypeDisplay.GetGlyph(SensorType.Humidity));
    }

    [Fact]
    public void GetGlyph_ReturnsDefault_ForUnknownType()
    {
        SensorType unknownType = (SensorType)999;
        Assert.Equal("\uE9D9", SensorTypeDisplay.GetGlyph(unknownType));
    }

    [Fact]
    public void GetHardwareGlyph_ReturnsCorrectGlyph_ForAllKnownTypes()
    {
        Assert.Equal("\uE950", SensorTypeDisplay.GetHardwareGlyph(HardwareType.Motherboard));
        Assert.Equal("\uE950", SensorTypeDisplay.GetHardwareGlyph(HardwareType.SuperIO));
        Assert.Equal("\uE950", SensorTypeDisplay.GetHardwareGlyph(HardwareType.Cpu));
        Assert.Equal("\uE8AB", SensorTypeDisplay.GetHardwareGlyph(HardwareType.Memory));
        Assert.Equal("\uE7F4", SensorTypeDisplay.GetHardwareGlyph(HardwareType.GpuNvidia));
        Assert.Equal("\uE7F4", SensorTypeDisplay.GetHardwareGlyph(HardwareType.GpuAmd));
        Assert.Equal("\uE7F4", SensorTypeDisplay.GetHardwareGlyph(HardwareType.GpuIntel));
        Assert.Equal("\uEDA2", SensorTypeDisplay.GetHardwareGlyph(HardwareType.Storage));
        Assert.Equal("\uE968", SensorTypeDisplay.GetHardwareGlyph(HardwareType.Network));
        Assert.Equal("\uE9F3", SensorTypeDisplay.GetHardwareGlyph(HardwareType.Cooler));
        Assert.Equal("\uE950", SensorTypeDisplay.GetHardwareGlyph(HardwareType.EmbeddedController));
        Assert.Equal("\uE7E8", SensorTypeDisplay.GetHardwareGlyph(HardwareType.Psu));
        Assert.Equal("\uEBAA", SensorTypeDisplay.GetHardwareGlyph(HardwareType.Battery));
    }

    [Fact]
    public void GetHardwareGlyph_ReturnsDefault_ForUnknownType()
    {
        HardwareType unknownType = (HardwareType)999;
        Assert.Equal("\uE950", SensorTypeDisplay.GetHardwareGlyph(unknownType));
    }
}

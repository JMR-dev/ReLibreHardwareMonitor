using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Windows.WinUI.Controls;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Controls;

public class PlotViewTests
{
    [Fact]
    public void DecimatePoints_ReturnsOriginalList_WhenPointsCountSmall()
    {
        var points = new List<PlotPointViewModel>
        {
            new(DateTime.UtcNow, 1.0),
            new(DateTime.UtcNow.AddSeconds(1), 2.0),
            new(DateTime.UtcNow.AddSeconds(2), 3.0),
        };

        var result = PlotView.DecimatePoints(points, 10);

        Assert.Equal(points.Count, result.Count);
        Assert.Equal(points, result);
    }

    [Fact]
    public void DecimatePoints_DecimatesAndMaintainsEnvelope_WhenPointsCountLarge()
    {
        // 1000 points, decimate to targetWidth = 100
        var points = new List<PlotPointViewModel>();
        var baseTime = DateTime.UtcNow;
        for (int i = 0; i < 1000; i++)
        {
            // Create a sine wave with some noise/spikes
            double val = Math.Sin(i * 0.1);
            if (i == 505) val = 100.0; // extreme max spike
            if (i == 510) val = -100.0; // extreme min spike
            points.Add(new PlotPointViewModel(baseTime.AddSeconds(i), val));
        }

        var result = PlotView.DecimatePoints(points, 100);

        // Result should be decimated (less than original, but bounds preserved)
        Assert.True(result.Count < points.Count);
        Assert.True(result.Count >= 100);

        // Extreme spike values must be preserved in the output
        Assert.Contains(result, p => p.Value == 100.0);
        Assert.Contains(result, p => p.Value == -100.0);

        // Rightmost point (latest sample) must be exactly preserved
        Assert.Equal(points[^1].Timestamp, result[^1].Timestamp);
        Assert.Equal(points[^1].Value, result[^1].Value);
    }
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using Windows.UI;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

/// <summary>
/// Owns the plot's series collection and reconciles it with the currently selected sensors on each update. Extracted
/// from <see cref="MainWindowViewModel" /> so the (subtle) point-retention/merge logic can be unit-tested in isolation.
/// </summary>
internal sealed class PlotTrackingService
{
    private static readonly TimeSpan MaximumPlotPointRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaximumSyntheticPlotPointRetention = TimeSpan.FromMinutes(5);

    private static readonly Color[] PlotColors =
    [
        Color.FromArgb(255, 0x00, 0x78, 0xD4),
        Color.FromArgb(255, 0xE8, 0x11, 0x23),
        Color.FromArgb(255, 0x10, 0x7C, 0x10),
        Color.FromArgb(255, 0xF7, 0x63, 0x0C),
        Color.FromArgb(255, 0x88, 0x17, 0x98),
        Color.FromArgb(255, 0x00, 0xB7, 0xC3),
        Color.FromArgb(255, 0x49, 0x8B, 0x00),
        Color.FromArgb(255, 0xA8, 0x00, 0x00)
    ];

    private readonly Dictionary<string, PlotSeriesViewModel> _seriesByIdentifier = new();

    /// <summary>The live series collection, bound by the plot view.</summary>
    public ObservableCollection<PlotSeriesViewModel> Series { get; } = [];

    /// <summary>
    /// Reconciles <see cref="Series" /> with the sensors under <paramref name="root" /> that are flagged for plotting:
    /// adds/updates a series per selected sensor (merging stored history, retained synthetic points, and the current
    /// value, de-duplicated by timestamp and pruned to the retention window) and removes series no longer selected.
    /// </summary>
    public void Track(SensorTreeItemViewModel? root, TemperatureUnit temperatureUnit)
    {
        if (root == null)
            return;

        DateTime now = DateTime.UtcNow;
        HashSet<string> selectedIdentifiers = new();
        foreach (SensorTreeItemViewModel sensorItem in root.EnumerateSensors().Where(sensorItem => sensorItem.Plot && sensorItem.Sensor != null))
        {
            ISensor sensor = sensorItem.Sensor!;
            string identifier = sensor.Identifier.ToString();
            selectedIdentifiers.Add(identifier);

            if (!_seriesByIdentifier.TryGetValue(identifier, out PlotSeriesViewModel? series))
            {
                series = new PlotSeriesViewModel(
                    identifier,
                    sensor.Hardware.Name,
                    sensor.Name,
                    sensor.SensorType,
                    SensorFormatter.GetPlotUnit(sensor.SensorType, temperatureUnit),
                    GetPlotColor(sensorItem));
                _seriesByIdentifier[identifier] = series;
                Series.Add(series);
            }
            else
            {
                series.UpdateMetadata(sensor.Hardware.Name, sensor.Name, sensor.SensorType, SensorFormatter.GetPlotUnit(sensor.SensorType, temperatureUnit));

                // Only honor an explicit user pen color for an existing series; keep the auto-assigned color stable.
                // (Recomputing it from the live series count made existing lines shift/collide colors every tick.)
                if (sensorItem.PenColor.HasValue)
                    series.Color = sensorItem.PenColor.Value;
            }

            List<PlotPointViewModel> points = [];
            foreach (SensorValue sensorValue in sensor.Values.OrderBy(value => value.Time))
            {
                double? displayedValue = SensorFormatter.GetPlotValue(sensor, sensorValue.Value, temperatureUnit);
                if (displayedValue is not { } pointValue || !double.IsFinite(pointValue))
                    continue;

                points.Add(new PlotPointViewModel(sensorValue.Time, pointValue));
            }

            DateTime? latestHistoryTimestamp = points.Count > 0 ? points[^1].Timestamp : null;
            foreach (PlotPointViewModel existingPoint in series.Points)
            {
                if (now - existingPoint.Timestamp > MaximumSyntheticPlotPointRetention)
                    continue;

                if (!latestHistoryTimestamp.HasValue || existingPoint.Timestamp > latestHistoryTimestamp.Value)
                    points.Add(existingPoint);
            }

            double? currentValue = SensorFormatter.GetPlotValue(sensor, temperatureUnit);
            if (currentValue is { } currentPointValue && double.IsFinite(currentPointValue))
                points.Add(new PlotPointViewModel(now, currentPointValue));

            DateTime cutoff = now - MaximumPlotPointRetention;
            points = points
                .Where(point => point.Timestamp >= cutoff)
                .GroupBy(point => point.Timestamp.Ticks)
                .Select(group => group.Last())
                .OrderBy(point => point.Timestamp)
                .ToList();

            if (points.Count == 0 && currentValue is { } fallbackPointValue && double.IsFinite(fallbackPointValue))
            {
                points.Add(new PlotPointViewModel(now, fallbackPointValue));
            }

            series.ReplacePoints(points);
        }

        foreach (string identifier in _seriesByIdentifier.Keys.Where(identifier => !selectedIdentifiers.Contains(identifier)).ToArray())
        {
            PlotSeriesViewModel series = _seriesByIdentifier[identifier];
            _seriesByIdentifier.Remove(identifier);
            Series.Remove(series);
        }
    }

    /// <summary>Clears all tracked series (used when resetting the plot).</summary>
    public void Reset()
    {
        _seriesByIdentifier.Clear();
        Series.Clear();
    }

    /// <summary>Re-applies the color for an existing series after its sensor's pen color changed.</summary>
    public void RefreshSeriesColor(SensorTreeItemViewModel sensorItem)
    {
        if (sensorItem.Sensor == null)
            return;

        if (_seriesByIdentifier.TryGetValue(sensorItem.Sensor.Identifier.ToString(), out PlotSeriesViewModel? series))
            series.Color = GetPlotColor(sensorItem);
    }

    private Color GetPlotColor(SensorTreeItemViewModel sensorItem)
    {
        return sensorItem.PenColor ?? PlotColors[_seriesByIdentifier.Count % PlotColors.Length];
    }
}

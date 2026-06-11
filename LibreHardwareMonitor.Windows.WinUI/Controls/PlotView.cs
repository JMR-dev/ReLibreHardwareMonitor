// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LibreHardwareMonitor.Windows.WinUI.Controls;

public sealed class PlotView : Grid
{
    internal static readonly (string Label, int Value)[] PlotTimeWindowOptions =
    [
        ("Auto", 0),
        ("5 min", 1),
        ("10 min", 2),
        ("20 min", 3),
        ("30 min", 4),
        ("45 min", 5),
        ("1 h", 6),
        ("1.5 h", 7),
        ("2 h", 8),
        ("3 h", 9),
        ("6 h", 10),
        ("12 h", 11),
        ("24 h", 12)
    ];

    private const double PlotAxisLabelFontSize = 11;
    private const double PlotBottomMargin = 28;
    private const double PlotLeftMargin = 86;
    private const double PlotPointMarkerRadius = 3;
    private const double PlotRightMargin = 12;
    private const double PlotTopMargin = 10;

    private MainWindowViewModel? _viewModel;
    private readonly Canvas _canvas;
    private double _plotValueZoomFactor = 1;

    public PlotView()
    {
        _canvas = new Canvas
        {
            MinWidth = 320,
            MinHeight = 160,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["SystemControlBackgroundAltHighBrush"]
        };
        _canvas.SizeChanged += (_, _) => Redraw();
        _canvas.PointerWheelChanged += PlotCanvas_PointerWheelChanged;
        Children.Add(_canvas);
    }

    public PlotView(MainWindowViewModel viewModel) : this()
    {
        AttachViewModel(viewModel);
    }

    public void AttachViewModel(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _canvas.ContextFlyout = BuildPlotContextMenu();
    }

    private MainWindowViewModel VM => _viewModel ?? throw new InvalidOperationException("PlotView used before AttachViewModel.");

    public void ApplyTheme(AppThemeMode themeMode)
    {
        RequestedTheme = themeMode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark or AppThemeMode.Black => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        Redraw();
    }

    public void ResetZoom()
    {
        _plotValueZoomFactor = 1;
        Redraw();
    }

    public void Clear()
    {
        _canvas.Children.Clear();
    }

    public void Redraw()
    {
        if (_viewModel == null)
            return;

        _canvas.Children.Clear();
        _canvas.Background = new SolidColorBrush(GetPlotBackgroundColor());
        if (!VM.ShowPlot)
            return;

        double width = GetPlotCanvasWidth(_canvas);
        double height = GetPlotCanvasHeight(_canvas);
        if (width <= 4 || height <= 4)
            return;

        PlotBounds bounds = GetPlotBounds(width, height);
        DrawPlotFrame(_canvas, bounds);

        PlotSeriesViewModel[] plotSeriesWithPoints = VM.PlotSeries
            .Where(plotSeries => plotSeries.Points.Count > 0)
            .ToArray();
        if (plotSeriesWithPoints.Length == 0)
        {
            DrawPlotMessage(_canvas, bounds, VM.PlotSeries.Count == 0 ? "No sensors selected for plot" : "Waiting for sensor samples...");
            return;
        }

        DateTime minTimestamp = DateTime.MaxValue;
        DateTime maxTimestamp = DateTime.MinValue;

        foreach (PlotSeriesViewModel plotSeries in plotSeriesWithPoints)
        {
            foreach (PlotPointViewModel point in plotSeries.Points)
            {
                minTimestamp = point.Timestamp < minTimestamp ? point.Timestamp : minTimestamp;
                maxTimestamp = point.Timestamp > maxTimestamp ? point.Timestamp : maxTimestamp;
            }
        }

        if (minTimestamp == DateTime.MaxValue || maxTimestamp == DateTime.MinValue)
            return;

        if (VM.PlotTimeWindow.HasValue)
            minTimestamp = maxTimestamp - VM.PlotTimeWindow.Value;

        if (maxTimestamp <= minTimestamp)
            maxTimestamp = minTimestamp.AddSeconds(1);

        List<PlotSeriesSample> visibleSeries = [];
        foreach (PlotSeriesViewModel plotSeries in plotSeriesWithPoints)
        {
            PlotPointViewModel[] points = plotSeries.Points
                .Where(point => point.Timestamp >= minTimestamp && point.Timestamp <= maxTimestamp)
                .OrderBy(point => point.Timestamp)
                .ToArray();
            if (points.Length > 0)
                visibleSeries.Add(new PlotSeriesSample(plotSeries, points));
        }

        if (visibleSeries.Count == 0)
        {
            DrawPlotMessage(_canvas, bounds, "No samples in the selected time range");
            return;
        }

        AdjustPlotTimeRangeForSparseSamples(visibleSeries, ref minTimestamp, ref maxTimestamp);
        visibleSeries = visibleSeries
            .Select(sample => new PlotSeriesSample(
                sample.Series,
                sample.Points.Where(point => point.Timestamp >= minTimestamp && point.Timestamp <= maxTimestamp).ToArray()))
            .Where(sample => sample.Points.Count > 0)
            .ToList();
        if (visibleSeries.Count == 0)
        {
            DrawPlotMessage(_canvas, bounds, "Waiting for sensor samples...");
            return;
        }

        DrawTimeAxis(_canvas, bounds, minTimestamp, maxTimestamp);

        IGrouping<SensorType, PlotSeriesSample>[] groups = visibleSeries
            .GroupBy(sample => sample.Series.SensorType)
            .OrderBy(group => group.Key)
            .ToArray();

        double axisHeight = VM.PlotStackedAxes ? bounds.Height / groups.Length : bounds.Height;
        for (int i = 0; i < groups.Length; i++)
        {
            IGrouping<SensorType, PlotSeriesSample> group = groups[i];
            PlotSeriesSample[] samples = group.ToArray();
            GetValueRange(samples, out double minValue, out double maxValue);
            ExpandPlotValueRange(ref minValue, ref maxValue);
            ApplyPlotValueZoom(ref minValue, ref maxValue);

            PlotAxisLayout axis = new(
                group.Key,
                samples.First().Series.Unit,
                VM.PlotStackedAxes ? bounds.Top + axisHeight * i : bounds.Top,
                axisHeight,
                minValue,
                maxValue);

            DrawValueAxis(_canvas, bounds, axis, drawGrid: VM.PlotStackedAxes || i == 0, titleIndex: i);
            foreach (PlotSeriesSample sample in samples)
                DrawPlotSeries(_canvas, bounds, axis, minTimestamp, maxTimestamp, sample);
        }

        if (VM.ShowPlotLegend)
            DrawLegend(_canvas, bounds, visibleSeries);
    }

    private MenuFlyout BuildPlotContextMenu()
    {
        MenuFlyout flyout = new();
        flyout.Items.Add(CreateToggleSettingItem("Stacked Axes", () => VM.PlotStackedAxes, value => VM.PlotStackedAxes = value));
        flyout.Items.Add(CreateToggleSettingItem("Show Axes Labels", () => VM.ShowPlotAxisLabels, value => VM.ShowPlotAxisLabels = value));
        flyout.Items.Add(CreateToggleSettingItem("Show Legend", () => VM.ShowPlotLegend, value => VM.ShowPlotLegend = value));

        MenuFlyoutSubItem timeAxis = new() { Text = "Time Axis" };
        timeAxis.Items.Add(CreateToggleSettingItem("Enable Zoom", () => VM.PlotTimeAxisZoomEnabled, value => VM.PlotTimeAxisZoomEnabled = value));
        timeAxis.Items.Add(new MenuFlyoutSeparator());
        foreach ((string label, int value) in PlotTimeWindowOptions)
        {
            ToggleMenuFlyoutItem item = new()
            {
                Text = label,
                IsChecked = VM.PlotTimeWindowIndex == value,
                Tag = value
            };
            item.Click += (_, _) =>
            {
                VM.PlotTimeWindowIndex = value;
                foreach (ToggleMenuFlyoutItem sibling in timeAxis.Items.OfType<ToggleMenuFlyoutItem>())
                    sibling.IsChecked = Equals(sibling.Tag, value);
            };
            timeAxis.Items.Add(item);
        }

        flyout.Items.Add(timeAxis);

        MenuFlyoutSubItem valueAxes = new() { Text = "Value Axes" };
        valueAxes.Items.Add(CreateToggleSettingItem("Enable Zoom", () => VM.PlotValueAxesZoomEnabled, value => VM.PlotValueAxesZoomEnabled = value));
        valueAxes.Items.Add(CreateMenuItem("Autoscale All", (_, _) => ResetZoom()));
        flyout.Items.Add(valueAxes);

        return flyout;
    }

    private static MenuFlyoutItem CreateMenuItem(string text, RoutedEventHandler handler)
    {
        MenuFlyoutItem item = new() { Text = text };
        item.Click += handler;
        return item;
    }

    private static ToggleMenuFlyoutItem CreateToggleSettingItem(string text, Func<bool> getter, Action<bool> setter)
    {
        ToggleMenuFlyoutItem item = new() { Text = text, IsChecked = getter() };
        item.Click += (sender, _) =>
        {
            if (sender is ToggleMenuFlyoutItem toggle)
                setter(toggle.IsChecked);
        };
        return item;
    }

    private void PlotCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Canvas canvas)
            return;

        PointerPoint pointerPoint = e.GetCurrentPoint(canvas);
        int wheelDelta = pointerPoint.Properties.MouseWheelDelta;
        if (wheelDelta == 0)
            return;

        PlotBounds bounds = GetPlotBounds(GetPlotCanvasWidth(canvas), GetPlotCanvasHeight(canvas));
        if (VM.PlotValueAxesZoomEnabled && ShouldDrawPlotAxisLabels(bounds) && pointerPoint.Position.X <= bounds.Left)
        {
            ZoomPlotValueAxes(wheelDelta);
            e.Handled = true;
            return;
        }

        if (VM.PlotTimeAxisZoomEnabled)
        {
            ZoomPlotTimeAxis(wheelDelta);
            e.Handled = true;
        }
    }

    private void ZoomPlotValueAxes(int wheelDelta)
    {
        _plotValueZoomFactor = Math.Clamp(_plotValueZoomFactor * (wheelDelta > 0 ? 0.8 : 1.25), 0.05, 20);
        Redraw();
    }

    private void ZoomPlotTimeAxis(int wheelDelta)
    {
        int lastIndex = PlotTimeWindowOptions[^1].Value;
        int index = VM.PlotTimeWindowIndex;
        if (wheelDelta > 0)
            index = index == 0 ? lastIndex : Math.Max(1, index - 1);
        else
            index = index >= lastIndex ? 0 : index + 1;

        VM.PlotTimeWindowIndex = index;
    }

    private static double GetPlotCanvasWidth(Canvas canvas)
    {
        return Math.Max(canvas.ActualWidth, canvas.MinWidth);
    }

    private static double GetPlotCanvasHeight(Canvas canvas)
    {
        return Math.Max(canvas.ActualHeight, canvas.MinHeight);
    }

    private PlotBounds GetPlotBounds(double width, double height)
    {
        double left = VM.ShowPlotAxisLabels ? PlotLeftMargin : 0;
        double top = VM.ShowPlotAxisLabels ? PlotTopMargin : 0;
        double right = VM.ShowPlotAxisLabels ? PlotRightMargin : 0;
        double bottom = VM.ShowPlotAxisLabels ? PlotBottomMargin : 0;

        if (width <= left + right + 32 || height <= top + bottom + 24)
            return new PlotBounds(0, 0, Math.Max(1, width), Math.Max(1, height));

        return new PlotBounds(left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom));
    }

    private void DrawPointMarker(Canvas canvas, global::Windows.Foundation.Point point, Brush fill)
    {
        Ellipse marker = new()
        {
            Width = PlotPointMarkerRadius * 2,
            Height = PlotPointMarkerRadius * 2,
            Fill = fill
        };
        Canvas.SetLeft(marker, point.X - PlotPointMarkerRadius);
        Canvas.SetTop(marker, point.Y - PlotPointMarkerRadius);
        canvas.Children.Add(marker);
    }

    private void DrawPlotFrame(Canvas canvas, PlotBounds bounds)
    {
        SolidColorBrush border = new(GetPlotBorderColor());
        DrawLine(canvas, bounds.Left, bounds.Top, bounds.Right, bounds.Top, border, 1);
        DrawLine(canvas, bounds.Left, bounds.Bottom, bounds.Right, bounds.Bottom, border, 1);
        DrawLine(canvas, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom, border, 1);
        DrawLine(canvas, bounds.Right, bounds.Top, bounds.Right, bounds.Bottom, border, 1);
    }

    private void DrawValueAxis(Canvas canvas, PlotBounds bounds, PlotAxisLayout axis, bool drawGrid, int titleIndex)
    {
        SolidColorBrush gridBrush = new(GetPlotGridColor());
        SolidColorBrush textBrush = new(GetPlotTextColor());
        SolidColorBrush borderBrush = new(GetPlotBorderColor());
        bool drawLabels = ShouldDrawPlotAxisLabels(bounds);

        for (int i = 0; i <= 4; i++)
        {
            double y = axis.Top + axis.Height * i / 4;
            if (drawGrid)
                DrawLine(canvas, bounds.Left, y, bounds.Right, y, gridBrush, i is 0 or 4 ? 1 : 0.75);

            if (drawLabels)
            {
                double value = axis.MaxValue - (axis.MaxValue - axis.MinValue) * i / 4;
                AddPlotLabel(canvas, FormatPlotAxisValue(value), 4, y - 8, textBrush, PlotAxisLabelFontSize);
            }
        }

        if (VM.PlotStackedAxes)
            DrawLine(canvas, bounds.Left, axis.Top, bounds.Right, axis.Top, borderBrush, 1);

        if (drawLabels)
        {
            string unit = string.IsNullOrWhiteSpace(axis.Unit) ? "" : $" ({axis.Unit})";
            AddPlotLabel(canvas, $"{SensorTypeDisplay.GetText(axis.SensorType)}{unit}", 4, axis.Top + 2 + (VM.PlotStackedAxes ? 0 : titleIndex * 15), textBrush, PlotAxisLabelFontSize, 600);
        }
    }

    private void DrawTimeAxis(Canvas canvas, PlotBounds bounds, DateTime minTimestamp, DateTime maxTimestamp)
    {
        TimeSpan range = maxTimestamp - minTimestamp;
        if (range <= TimeSpan.Zero)
            range = TimeSpan.FromSeconds(1);

        SolidColorBrush gridBrush = new(GetPlotGridColor());
        SolidColorBrush textBrush = new(GetPlotTextColor());
        bool drawLabels = ShouldDrawPlotAxisLabels(bounds);
        for (int i = 0; i <= 4; i++)
        {
            double x = bounds.Left + bounds.Width * i / 4;
            DrawLine(canvas, x, bounds.Top, x, bounds.Bottom, gridBrush, i is 0 or 4 ? 1 : 0.75);
            if (!drawLabels)
                continue;

            TimeSpan age = TimeSpan.FromTicks((long)Math.Round(range.Ticks * (4 - i) / 4.0));
            TextBlock label = CreatePlotLabel(FormatPlotAge(age), textBrush, PlotAxisLabelFontSize);
            label.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double maxLeft = Math.Max(0, bounds.Right - label.DesiredSize.Width);
            Canvas.SetLeft(label, Math.Clamp(x - label.DesiredSize.Width / 2, 0, maxLeft));
            Canvas.SetTop(label, bounds.Bottom + 4);
            canvas.Children.Add(label);
        }
    }

    private static void AdjustPlotTimeRangeForSparseSamples(IReadOnlyList<PlotSeriesSample> visibleSeries, ref DateTime minTimestamp, ref DateTime maxTimestamp)
    {
        DateTime dataMin = DateTime.MaxValue;
        DateTime dataMax = DateTime.MinValue;
        foreach (PlotSeriesSample sample in visibleSeries)
        {
            foreach (PlotPointViewModel point in sample.Points)
            {
                dataMin = point.Timestamp < dataMin ? point.Timestamp : dataMin;
                dataMax = point.Timestamp > dataMax ? point.Timestamp : dataMax;
            }
        }

        if (dataMin == DateTime.MaxValue || dataMax == DateTime.MinValue)
            return;

        TimeSpan selectedRange = maxTimestamp - minTimestamp;
        TimeSpan dataRange = dataMax - dataMin;
        if (selectedRange <= TimeSpan.FromSeconds(30) || dataRange >= TimeSpan.FromSeconds(30))
            return;

        maxTimestamp = dataMax;
        minTimestamp = maxTimestamp - TimeSpan.FromSeconds(30);
    }

    private bool ShouldDrawPlotAxisLabels(PlotBounds bounds)
    {
        return VM.ShowPlotAxisLabels && bounds.Left >= PlotLeftMargin && bounds.Height > 24 && bounds.Width > 32;
    }

    private void DrawPlotSeries(
        Canvas canvas,
        PlotBounds bounds,
        PlotAxisLayout axis,
        DateTime minTimestamp,
        DateTime maxTimestamp,
        PlotSeriesSample sample)
    {
        long rangeTicks = Math.Max(1, (maxTimestamp - minTimestamp).Ticks);
        SolidColorBrush stroke = new(GetVisiblePlotColor(sample.Series.Color));
        Polyline line = new()
        {
            Stroke = stroke,
            StrokeThickness = VM.PlotStrokeThickness,
            StrokeLineJoin = PenLineJoin.Round
        };

        IReadOnlyList<PlotPointViewModel> decimatedPoints = DecimatePoints(sample.Points, (int)Math.Max(100, bounds.Width));

        foreach (PlotPointViewModel point in decimatedPoints)
        {
            double x = bounds.Left + (point.Timestamp - minTimestamp).Ticks / (double)rangeTicks * bounds.Width;
            double y = axis.Bottom - ((point.Value - axis.MinValue) / (axis.MaxValue - axis.MinValue) * axis.Height);
            line.Points.Add(new global::Windows.Foundation.Point(x, y));
        }

        canvas.Children.Add(line);
        if (line.Points.Count > 0)
            DrawPointMarker(canvas, line.Points[^1], stroke);
    }

    internal static IReadOnlyList<PlotPointViewModel> DecimatePoints(IReadOnlyList<PlotPointViewModel> points, int targetWidth)
    {
        if (points.Count <= targetWidth * 2)
            return points;

        List<PlotPointViewModel> result = new();
        int bucketSize = points.Count / targetWidth;
        if (bucketSize <= 1)
            return points;

        for (int i = 0; i < targetWidth; i++)
        {
            int start = i * bucketSize;
            int end = (i == targetWidth - 1) ? points.Count : (i + 1) * bucketSize;
            if (start >= end)
                break;

            int minIdx = start;
            int maxIdx = start;
            for (int j = start + 1; j < end; j++)
            {
                if (points[j].Value < points[minIdx].Value)
                    minIdx = j;
                if (points[j].Value > points[maxIdx].Value)
                    maxIdx = j;
            }

            if (minIdx == maxIdx)
            {
                result.Add(points[minIdx]);
            }
            else if (minIdx < maxIdx)
            {
                result.Add(points[minIdx]);
                result.Add(points[maxIdx]);
            }
            else
            {
                result.Add(points[maxIdx]);
                result.Add(points[minIdx]);
            }
        }

        if (result.Count > 0 && result[^1].Timestamp != points[^1].Timestamp)
        {
            result.Add(points[^1]);
        }

        return result;
    }

    private void DrawPlotMessage(Canvas canvas, PlotBounds bounds, string message)
    {
        TextBlock label = CreatePlotLabel(message, new SolidColorBrush(GetPlotTextColor()), 13, 600);
        label.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, bounds.Left + Math.Max(0, (bounds.Width - label.DesiredSize.Width) / 2));
        Canvas.SetTop(label, bounds.Top + Math.Max(0, (bounds.Height - label.DesiredSize.Height) / 2));
        canvas.Children.Add(label);
    }

    private const int LegendMaxRows = 12;
    private const double LegendMinPlotWidth = 200;
    private const double LegendSwatchSize = 12;
    private const double LegendRowSpacing = 4;
    private const double LegendColumnSpacing = 10;
    private const double LegendPadding = 8;
    private const double LegendInset = 8;
    private const double LegendFontSize = 11;

    private void DrawLegend(Canvas canvas, PlotBounds bounds, IReadOnlyList<PlotSeriesSample> visibleSeries)
    {
        if (visibleSeries.Count == 0 || bounds.Width < LegendMinPlotWidth)
            return;

        PlotSeriesSample[] shown = visibleSeries.Take(LegendMaxRows).ToArray();
        int overflow = visibleSeries.Count - shown.Length;
        SolidColorBrush textBrush = new(GetPlotTextColor());

        TextBlock[] labelBlocks = new TextBlock[shown.Length];
        TextBlock[] valueBlocks = new TextBlock[shown.Length];
        double[] labelDesiredWidths = new double[shown.Length];
        double valueColumnWidth = 0;
        double rowHeight = 0;

        for (int i = 0; i < shown.Length; i++)
        {
            PlotSeriesSample sample = shown[i];
            TextBlock label = CreatePlotLabel(sample.Series.Title, textBrush, LegendFontSize);
            label.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            labelBlocks[i] = label;
            labelDesiredWidths[i] = label.DesiredSize.Width;

            string valueText = FormatLegendValue(sample.Points[^1].Value, sample.Series.Unit);
            TextBlock value = CreatePlotLabel(valueText, textBrush, LegendFontSize, 600);
            value.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            valueBlocks[i] = value;
            valueColumnWidth = Math.Max(valueColumnWidth, value.DesiredSize.Width);

            rowHeight = Math.Max(rowHeight, Math.Max(label.DesiredSize.Height, value.DesiredSize.Height));
        }

        TextBlock? overflowBlock = null;
        if (overflow > 0)
        {
            overflowBlock = CreatePlotLabel($"+{overflow} more", textBrush, LegendFontSize);
            overflowBlock.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, overflowBlock.DesiredSize.Height);
        }

        rowHeight = Math.Max(rowHeight, LegendSwatchSize);

        double fixedWidth = LegendPadding * 2 + LegendSwatchSize + LegendColumnSpacing + LegendColumnSpacing + valueColumnWidth;
        double maxLabelColumnWidth = Math.Max(40, bounds.Width - LegendInset * 2 - fixedWidth);

        double labelColumnWidth = 0;
        for (int i = 0; i < shown.Length; i++)
            labelColumnWidth = Math.Max(labelColumnWidth, Math.Min(labelDesiredWidths[i], maxLabelColumnWidth));

        bool clamped = false;
        for (int i = 0; i < shown.Length; i++)
        {
            if (labelDesiredWidths[i] > labelColumnWidth + 0.5)
            {
                clamped = true;
                break;
            }
        }
        if (clamped)
        {
            foreach (TextBlock label in labelBlocks)
                label.TextTrimming = TextTrimming.CharacterEllipsis;
        }

        int totalRows = shown.Length + (overflowBlock != null ? 1 : 0);
        double panelWidth = fixedWidth + labelColumnWidth;
        double panelHeight = LegendPadding * 2 + totalRows * rowHeight + Math.Max(0, totalRows - 1) * LegendRowSpacing;

        if (panelHeight > bounds.Height - LegendInset * 2)
            return;

        double panelLeft = bounds.Right - panelWidth - LegendInset;
        double panelTop = bounds.Top + LegendInset;

        Rectangle background = new()
        {
            Width = panelWidth,
            Height = panelHeight,
            RadiusX = 4,
            RadiusY = 4,
            Fill = new SolidColorBrush(GetLegendBackgroundColor()),
            Stroke = new SolidColorBrush(GetPlotBorderColor()),
            StrokeThickness = 1
        };
        Canvas.SetLeft(background, panelLeft);
        Canvas.SetTop(background, panelTop);
        canvas.Children.Add(background);

        double rowTop = panelTop + LegendPadding;
        double swatchLeft = panelLeft + LegendPadding;
        double labelLeft = swatchLeft + LegendSwatchSize + LegendColumnSpacing;
        double valueRight = panelLeft + panelWidth - LegendPadding;

        for (int i = 0; i < shown.Length; i++)
        {
            PlotSeriesSample sample = shown[i];
            global::Windows.UI.Color seriesColor = GetVisiblePlotColor(sample.Series.Color);
            Rectangle swatch = new()
            {
                Width = LegendSwatchSize,
                Height = LegendSwatchSize,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(seriesColor)
            };
            Canvas.SetLeft(swatch, swatchLeft);
            Canvas.SetTop(swatch, rowTop + Math.Max(0, (rowHeight - LegendSwatchSize) / 2));
            canvas.Children.Add(swatch);

            TextBlock label = labelBlocks[i];
            label.MaxWidth = labelColumnWidth;
            Canvas.SetLeft(label, labelLeft);
            Canvas.SetTop(label, rowTop + Math.Max(0, (rowHeight - label.DesiredSize.Height) / 2));
            canvas.Children.Add(label);

            TextBlock value = valueBlocks[i];
            Canvas.SetLeft(value, valueRight - value.DesiredSize.Width);
            Canvas.SetTop(value, rowTop + Math.Max(0, (rowHeight - value.DesiredSize.Height) / 2));
            canvas.Children.Add(value);

            rowTop += rowHeight + LegendRowSpacing;
        }

        if (overflowBlock != null)
        {
            Canvas.SetLeft(overflowBlock, labelLeft);
            Canvas.SetTop(overflowBlock, rowTop + Math.Max(0, (rowHeight - overflowBlock.DesiredSize.Height) / 2));
            canvas.Children.Add(overflowBlock);
        }
    }

    private global::Windows.UI.Color GetLegendBackgroundColor()
    {
        global::Windows.UI.Color background = GetPlotBackgroundColor();
        return global::Windows.UI.Color.FromArgb(220, background.R, background.G, background.B);
    }

    private static string FormatLegendValue(double value, string unit)
    {
        string formatted = FormatPlotAxisValue(value);
        return string.IsNullOrEmpty(unit) ? formatted : $"{formatted} {unit}";
    }

    private static void GetValueRange(IReadOnlyList<PlotSeriesSample> samples, out double minValue, out double maxValue)
    {
        minValue = double.MaxValue;
        maxValue = double.MinValue;

        foreach (PlotSeriesSample sample in samples)
        {
            foreach (PlotPointViewModel point in sample.Points)
            {
                minValue = Math.Min(minValue, point.Value);
                maxValue = Math.Max(maxValue, point.Value);
            }
        }
    }

    private static void ExpandPlotValueRange(ref double minValue, ref double maxValue)
    {
        if (!double.IsFinite(minValue) || !double.IsFinite(maxValue) || minValue == double.MaxValue || maxValue == double.MinValue)
        {
            minValue = 0;
            maxValue = 1;
            return;
        }

        double range = maxValue - minValue;
        if (Math.Abs(range) < double.Epsilon)
        {
            double delta = Math.Max(Math.Abs(maxValue) * 0.05, 1);
            minValue -= delta;
            maxValue += delta;
            return;
        }

        double padding = range * 0.05;
        minValue -= padding;
        maxValue += padding;
    }

    private void ApplyPlotValueZoom(ref double minValue, ref double maxValue)
    {
        if (Math.Abs(_plotValueZoomFactor - 1) < 0.0001)
            return;

        double center = (minValue + maxValue) / 2;
        double halfRange = (maxValue - minValue) * _plotValueZoomFactor / 2;
        minValue = center - halfRange;
        maxValue = center + halfRange;
    }

    private static void DrawLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            X2 = x2,
            Y1 = y1,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness
        });
    }

    private static void AddPlotLabel(Canvas canvas, string text, double left, double top, Brush foreground, double fontSize, ushort fontWeight = 400)
    {
        TextBlock label = CreatePlotLabel(text, foreground, fontSize, fontWeight);
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        canvas.Children.Add(label);
    }

    private static TextBlock CreatePlotLabel(string text, Brush foreground, double fontSize, ushort fontWeight = 400)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = new global::Windows.UI.Text.FontWeight { Weight = fontWeight },
            Foreground = foreground
        };
    }

    private static string FormatPlotAxisValue(double value)
    {
        double absoluteValue = Math.Abs(value);
        if (absoluteValue >= 1000)
            return value.ToString("F0", CultureInfo.CurrentCulture);
        if (absoluteValue >= 100)
            return value.ToString("F1", CultureInfo.CurrentCulture);
        if (absoluteValue >= 10)
            return value.ToString("F2", CultureInfo.CurrentCulture);
        return value.ToString("F3", CultureInfo.CurrentCulture);
    }

    private static string FormatPlotAge(TimeSpan age)
    {
        if (age.TotalHours >= 1)
            return $"{(int)age.TotalHours}:{age.Minutes:00}";

        return $"{(int)age.TotalMinutes}:{age.Seconds:00}";
    }

    private global::Windows.UI.Color GetPlotBackgroundColor()
    {
        return VM.ThemeMode switch
        {
            AppThemeMode.Black => Colors.Black,
            AppThemeMode.Dark => global::Windows.UI.Color.FromArgb(255, 24, 24, 24),
            AppThemeMode.Auto when IsDarkPlotTheme() => global::Windows.UI.Color.FromArgb(255, 24, 24, 24),
            _ => Colors.White
        };
    }

    private global::Windows.UI.Color GetPlotBorderColor()
    {
        return IsDarkPlotTheme()
            ? global::Windows.UI.Color.FromArgb(200, 210, 210, 210)
            : global::Windows.UI.Color.FromArgb(180, 72, 72, 72);
    }

    private global::Windows.UI.Color GetPlotGridColor()
    {
        return IsDarkPlotTheme()
            ? global::Windows.UI.Color.FromArgb(85, 190, 190, 190)
            : global::Windows.UI.Color.FromArgb(85, 96, 96, 96);
    }

    private global::Windows.UI.Color GetPlotTextColor()
    {
        return IsDarkPlotTheme()
            ? global::Windows.UI.Color.FromArgb(230, 245, 245, 245)
            : global::Windows.UI.Color.FromArgb(230, 24, 24, 24);
    }

    private global::Windows.UI.Color GetVisiblePlotColor(global::Windows.UI.Color color)
    {
        double luminance = GetRelativeLuminance(color);
        if (IsDarkPlotTheme() && luminance < 0.35)
            return MixColor(color, Colors.White, 0.45);

        if (!IsDarkPlotTheme() && luminance > 0.82)
            return MixColor(color, Colors.Black, 0.4);

        return color;
    }

    private bool IsDarkPlotTheme()
    {
        return VM.ThemeMode switch
        {
            AppThemeMode.Black or AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => ActualTheme == ElementTheme.Dark
        };
    }

    private static global::Windows.UI.Color MixColor(global::Windows.UI.Color source, global::Windows.UI.Color target, double targetAmount)
    {
        targetAmount = Math.Clamp(targetAmount, 0, 1);
        double sourceAmount = 1 - targetAmount;
        return global::Windows.UI.Color.FromArgb(
            source.A,
            (byte)Math.Round(source.R * sourceAmount + target.R * targetAmount),
            (byte)Math.Round(source.G * sourceAmount + target.G * targetAmount),
            (byte)Math.Round(source.B * sourceAmount + target.B * targetAmount));
    }

    private static double GetRelativeLuminance(global::Windows.UI.Color color)
    {
        static double Linearize(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
    }

    private sealed record PlotBounds(double Left, double Top, double Width, double Height)
    {
        public double Bottom => Top + Height;

        public double Right => Left + Width;
    }

    private sealed record PlotAxisLayout(SensorType SensorType, string Unit, double Top, double Height, double MinValue, double MaxValue)
    {
        public double Bottom => Top + Height;
    }

    private sealed record PlotSeriesSample(PlotSeriesViewModel Series, IReadOnlyList<PlotPointViewModel> Points);
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

internal sealed class SensorColumnMeasurer
{
    public const double DefaultDeviceColumnWidth = 320;
    private const double MaximumColumnWidth = 4096;
    private const double MinimumDeviceColumnWidth = 120;
    private const double SensorColumnPadding = 72;
    private const double TreeIndentWidth = 20;
    private const double ValueColumnPadding = 18;
    private const int MaxCacheEntries = 4096;
    private const string DeviceColumnWidthSetting = "winui.deviceColumnWidth";
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(5);

    private readonly MainWindowViewModel _viewModel;
    private readonly WinUiStartupTrace? _startupTrace;
    private readonly Dictionary<(string Text, bool Bold), double> _cache = new();
    private readonly List<Grid> _rowGrids = [];
    private readonly DispatcherQueueTimer _settleTimer;
    private readonly double[] _columnWidths = [DefaultDeviceColumnWidth, 120, 120, 120];
    private TextBlock? _measurementTextBlock;
    private Grid? _header;
    private double _stableDeviceColumnWidth = DefaultDeviceColumnWidth;
    private bool _settled;

    public SensorColumnMeasurer(DispatcherQueue dispatcherQueue, MainWindowViewModel viewModel, WinUiStartupTrace? startupTrace)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _startupTrace = startupTrace;

        _settleTimer = dispatcherQueue.CreateTimer();
        _settleTimer.Interval = SettleDelay;
        _settleTimer.Tick += SettleTimer_Tick;
    }

    public event EventHandler? SettleTriggered;

    public int RowCount => _rowGrids.Count;

    public int CacheEntryCount => _cache.Count;

    public double DeviceColumnWidth => _columnWidths[0];

    public bool IsSettled => _settled;

    public void ApplySavedWidth()
    {
        _stableDeviceColumnWidth = NormalizeDeviceColumnWidth(_viewModel.Settings.GetValue(DeviceColumnWidthSetting, DefaultDeviceColumnWidth));
        _columnWidths[0] = _stableDeviceColumnWidth;
    }

    public Grid CreateRowGrid()
    {
        Grid grid = CreateColumnGrid();
        _rowGrids.Add(grid);
        return grid;
    }

    public Grid CreateHeaderGrid()
    {
        Grid grid = CreateColumnGrid();
        _header = grid;
        return grid;
    }

    public void ResetRows()
    {
        _rowGrids.Clear();
    }

    public void ScheduleSettle()
    {
        if (_rowGrids.Count == 0)
            return;

        _settled = false;
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    public void StopSettleTimer()
    {
        _settleTimer.Stop();
    }

    public void UpdateWidths()
    {
        double sensorWidth = MeasureText("Sensor", true) + SensorColumnPadding;
        double valueWidth = _viewModel.ShowValueColumn ? MeasureText("Value", true) + ValueColumnPadding : 0;
        double minWidth = _viewModel.ShowMinColumn ? MeasureText("Min", true) + ValueColumnPadding : 0;
        double maxWidth = _viewModel.ShowMaxColumn ? MeasureText("Max", true) + ValueColumnPadding : 0;

        foreach (SensorTreeItemViewModel root in _viewModel.RootItems)
            MeasureColumnWidths(root, 0, ref sensorWidth, ref valueWidth, ref minWidth, ref maxWidth);

        double deviceColumnWidth = NormalizeDeviceColumnWidth(sensorWidth);
        if (!_settled)
            deviceColumnWidth = Math.Max(deviceColumnWidth, _stableDeviceColumnWidth);

        _columnWidths[0] = deviceColumnWidth;
        _columnWidths[1] = Math.Ceiling(valueWidth);
        _columnWidths[2] = Math.Ceiling(minWidth);
        _columnWidths[3] = Math.Ceiling(maxWidth);

        if (_header != null)
            ApplyColumnWidths(_header);
        foreach (Grid row in _rowGrids)
            ApplyColumnWidths(row);

        RecordDeviceColumnWidthIfChanged();
    }

    private void SettleTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _settleTimer.Stop();
        _settled = true;
        SettleTriggered?.Invoke(this, EventArgs.Empty);
    }

    private Grid CreateColumnGrid()
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition()
            }
        };

        ApplyColumnWidths(grid);
        return grid;
    }

    private void ApplyColumnWidths(Grid grid)
    {
        for (int i = 0; i < grid.ColumnDefinitions.Count && i < _columnWidths.Length; i++)
            grid.ColumnDefinitions[i].Width = new GridLength(_columnWidths[i]);
    }

    private void MeasureColumnWidths(
        SensorTreeItemViewModel item,
        int depth,
        ref double sensorWidth,
        ref double valueWidth,
        ref double minWidth,
        ref double maxWidth)
    {
        if (item.RowVisibility == Visibility.Visible)
        {
            sensorWidth = Math.Max(sensorWidth, MeasureText(item.Text) + SensorColumnPadding + depth * TreeIndentWidth);
            if (_viewModel.ShowValueColumn)
                valueWidth = Math.Max(valueWidth, MeasureText(item.Value) + ValueColumnPadding);
            if (_viewModel.ShowMinColumn)
                minWidth = Math.Max(minWidth, MeasureText(item.Min) + ValueColumnPadding);
            if (_viewModel.ShowMaxColumn)
                maxWidth = Math.Max(maxWidth, MeasureText(item.Max) + ValueColumnPadding);
        }

        foreach (SensorTreeItemViewModel child in item.Children)
            MeasureColumnWidths(child, depth + 1, ref sensorWidth, ref valueWidth, ref minWidth, ref maxWidth);
    }

    private double MeasureText(string text, bool bold = false)
    {
        (string Text, bool Bold) key = (text, bold);
        if (_cache.TryGetValue(key, out double width))
            return width;

        if (_cache.Count >= MaxCacheEntries)
            _cache.Clear();

        // Reuse a single TextBlock for measurement instead of allocating one per cache miss. This runs for every
        // sensor's Value/Min/Max on each update tick, and the frequently-changing value strings miss the cache, so the
        // old code created and discarded a WinUI element (with a native peer) on nearly every call — avoidable churn.
        _measurementTextBlock ??= new TextBlock();
        _measurementTextBlock.Text = text;
        _measurementTextBlock.FontWeight = new global::Windows.UI.Text.FontWeight { Weight = bold ? (ushort)600 : (ushort)400 };
        _measurementTextBlock.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        width = _measurementTextBlock.DesiredSize.Width;
        _cache[key] = width;
        return width;
    }

    private static double NormalizeDeviceColumnWidth(double width)
    {
        if (!double.IsFinite(width))
            width = DefaultDeviceColumnWidth;

        return Math.Ceiling(Math.Clamp(width, MinimumDeviceColumnWidth, MaximumColumnWidth));
    }

    private void RecordDeviceColumnWidthIfChanged()
    {
        if (!_settled || _rowGrids.Count == 0)
            return;

        double measuredWidth = NormalizeDeviceColumnWidth(_columnWidths[0]);
        _stableDeviceColumnWidth = measuredWidth;
        double savedWidth = NormalizeDeviceColumnWidth(_viewModel.Settings.GetValue(DeviceColumnWidthSetting, DefaultDeviceColumnWidth));
        if (Math.Abs(measuredWidth - savedWidth) < 0.5)
            return;

        _viewModel.Settings.SetValue(DeviceColumnWidthSetting, measuredWidth);
        _startupTrace?.Mark("MainWindow.RecordDeviceColumnWidth", FormattableString.Invariant($"width={measuredWidth:F0}, rows={_rowGrids.Count}"));
    }
}

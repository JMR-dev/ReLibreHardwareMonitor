// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Windows.WinUI.Services.Tracing;
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
    private readonly IStartupTracer _startupTrace;
    private readonly Dictionary<(string Text, bool Bold), double> _cache = new();
    private readonly List<RowMeasurement> _rows = [];
    private readonly Dictionary<SensorTreeItemViewModel, RowMeasurement> _rowsByItem = new();
    private readonly DispatcherQueueTimer _settleTimer;
    private readonly double[] _columnWidths = [DefaultDeviceColumnWidth, 120, 120, 120];
    private readonly double[] _measuredColumnWidths = [DefaultDeviceColumnWidth, 120, 120, 120];
    private TextBlock? _measurementTextBlock;
    private Grid? _header;
    private double _stableDeviceColumnWidth = DefaultDeviceColumnWidth;
    private SensorDisplayColumn _dirtyColumns = SensorDisplayColumn.All;
    private bool _settled;
    private bool _requiresFullMeasurement = true;
    private bool _lastShowValueColumn;
    private bool _lastShowMinColumn;
    private bool _lastShowMaxColumn;

    public SensorColumnMeasurer(DispatcherQueue dispatcherQueue, MainWindowViewModel viewModel, IStartupTracer startupTrace)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _startupTrace = startupTrace;
        _lastShowValueColumn = viewModel.ShowValueColumn;
        _lastShowMinColumn = viewModel.ShowMinColumn;
        _lastShowMaxColumn = viewModel.ShowMaxColumn;

        _settleTimer = dispatcherQueue.CreateTimer();
        _settleTimer.Interval = SettleDelay;
        _settleTimer.Tick += SettleTimer_Tick;
    }

    public event EventHandler? SettleTriggered;

    public int RowCount => _rows.Count;

    public int CacheEntryCount => _cache.Count;

    public double DeviceColumnWidth => _columnWidths[0];

    public bool IsSettled => _settled;

    public void ApplySavedWidth()
    {
        _stableDeviceColumnWidth = NormalizeDeviceColumnWidth(_viewModel.Settings.GetValue(DeviceColumnWidthSetting, DefaultDeviceColumnWidth));
        _columnWidths[0] = _stableDeviceColumnWidth;
        InvalidateAll();
    }

    public Grid CreateRowGrid(SensorTreeItemViewModel item, int depth)
    {
        ArgumentNullException.ThrowIfNull(item);

        Grid grid = CreateColumnGrid();
        RowMeasurement row = new(item, grid, depth);
        _rows.Add(row);
        _rowsByItem[item] = row;
        item.DisplayColumnsChanged += Item_DisplayColumnsChanged;
        _requiresFullMeasurement = true;
        _dirtyColumns = SensorDisplayColumn.All;
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
        foreach (RowMeasurement row in _rows)
            row.Item.DisplayColumnsChanged -= Item_DisplayColumnsChanged;

        _rows.Clear();
        _rowsByItem.Clear();
        InvalidateAll();
    }

    public void ScheduleSettle()
    {
        if (_rows.Count == 0)
            return;

        _settled = false;
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    public void StopSettleTimer()
    {
        _settleTimer.Stop();
    }

    public void InvalidateAll()
    {
        _requiresFullMeasurement = true;
        _dirtyColumns = SensorDisplayColumn.All;
        foreach (RowMeasurement row in _rows)
            row.DirtyColumns = SensorDisplayColumn.All;
    }

    public void UpdateWidths()
    {
        if (ColumnVisibilityChanged())
            InvalidateAll();

        bool widthsChanged = false;
        if (_requiresFullMeasurement)
        {
            MeasureAllRows();
            _requiresFullMeasurement = false;
            widthsChanged = UpdateDisplayedColumnWidths();
        }
        else if (_dirtyColumns != SensorDisplayColumn.None)
        {
            MeasureDirtyRows();
            widthsChanged = UpdateDisplayedColumnWidths();
        }

        if (widthsChanged)
            ApplyColumnWidthsToRegisteredGrids();

        RecordDeviceColumnWidthIfChanged();
    }

    private void Item_DisplayColumnsChanged(object? sender, SensorDisplayColumn columns)
    {
        if (columns == SensorDisplayColumn.None || sender is not SensorTreeItemViewModel item)
            return;

        if (!_rowsByItem.TryGetValue(item, out RowMeasurement? row))
            return;

        row.DirtyColumns |= columns;
        _dirtyColumns |= columns;
    }

    private void SettleTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _settleTimer.Stop();
        _settled = true;
        InvalidateAll();
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

    private void ApplyColumnWidthsToRegisteredGrids()
    {
        if (_header != null)
            ApplyColumnWidths(_header);

        foreach (RowMeasurement row in _rows)
            ApplyColumnWidths(row.Grid);
    }

    private bool ColumnVisibilityChanged()
    {
        bool changed = _lastShowValueColumn != _viewModel.ShowValueColumn
                       || _lastShowMinColumn != _viewModel.ShowMinColumn
                       || _lastShowMaxColumn != _viewModel.ShowMaxColumn;

        _lastShowValueColumn = _viewModel.ShowValueColumn;
        _lastShowMinColumn = _viewModel.ShowMinColumn;
        _lastShowMaxColumn = _viewModel.ShowMaxColumn;
        return changed;
    }

    private void MeasureAllRows()
    {
        SetHeaderColumnWidths();

        foreach (RowMeasurement row in _rows)
        {
            MeasureRowColumn(row, SensorDisplayColumn.Sensor);
            MeasureRowColumn(row, SensorDisplayColumn.Value);
            MeasureRowColumn(row, SensorDisplayColumn.Min);
            MeasureRowColumn(row, SensorDisplayColumn.Max);
            row.DirtyColumns = SensorDisplayColumn.None;
        }

        _dirtyColumns = SensorDisplayColumn.None;
    }

    private void MeasureDirtyRows()
    {
        SensorDisplayColumn dirtyColumns = _dirtyColumns;
        foreach (SensorDisplayColumn column in EnumerateColumns(dirtyColumns))
        {
            bool shouldRecalculate = false;
            int columnIndex = GetColumnIndex(column);

            foreach (RowMeasurement row in _rows)
            {
                if ((row.DirtyColumns & column) == 0)
                    continue;

                double oldWidth = row.ColumnWidths[columnIndex];
                double newWidth = GetRowColumnWidth(row, column);
                row.ColumnWidths[columnIndex] = newWidth;

                if (newWidth > _measuredColumnWidths[columnIndex])
                {
                    _measuredColumnWidths[columnIndex] = newWidth;
                }
                else if (oldWidth >= _measuredColumnWidths[columnIndex] - 0.01 && newWidth < oldWidth)
                {
                    shouldRecalculate = true;
                }

                row.DirtyColumns &= ~column;
            }

            if (shouldRecalculate)
                RecalculateMeasuredColumnWidth(column);
        }

        _dirtyColumns = SensorDisplayColumn.None;
        foreach (RowMeasurement row in _rows)
            _dirtyColumns |= row.DirtyColumns;
    }

    private void SetHeaderColumnWidths()
    {
        _measuredColumnWidths[0] = MeasureText("Sensor", true) + SensorColumnPadding;
        _measuredColumnWidths[1] = _viewModel.ShowValueColumn ? MeasureText("Value", true) + ValueColumnPadding : 0;
        _measuredColumnWidths[2] = _viewModel.ShowMinColumn ? MeasureText("Min", true) + ValueColumnPadding : 0;
        _measuredColumnWidths[3] = _viewModel.ShowMaxColumn ? MeasureText("Max", true) + ValueColumnPadding : 0;
    }

    private void MeasureRowColumn(RowMeasurement row, SensorDisplayColumn column)
    {
        int columnIndex = GetColumnIndex(column);
        double width = GetRowColumnWidth(row, column);
        row.ColumnWidths[columnIndex] = width;
        _measuredColumnWidths[columnIndex] = Math.Max(_measuredColumnWidths[columnIndex], width);
    }

    private void RecalculateMeasuredColumnWidth(SensorDisplayColumn column)
    {
        int columnIndex = GetColumnIndex(column);
        SetHeaderColumnWidth(column);

        foreach (RowMeasurement row in _rows)
            _measuredColumnWidths[columnIndex] = Math.Max(_measuredColumnWidths[columnIndex], row.ColumnWidths[columnIndex]);
    }

    private void SetHeaderColumnWidth(SensorDisplayColumn column)
    {
        int columnIndex = GetColumnIndex(column);
        _measuredColumnWidths[columnIndex] = column switch
        {
            SensorDisplayColumn.Sensor => MeasureText("Sensor", true) + SensorColumnPadding,
            SensorDisplayColumn.Value => _viewModel.ShowValueColumn ? MeasureText("Value", true) + ValueColumnPadding : 0,
            SensorDisplayColumn.Min => _viewModel.ShowMinColumn ? MeasureText("Min", true) + ValueColumnPadding : 0,
            SensorDisplayColumn.Max => _viewModel.ShowMaxColumn ? MeasureText("Max", true) + ValueColumnPadding : 0,
            _ => 0
        };
    }

    private double GetRowColumnWidth(RowMeasurement row, SensorDisplayColumn column)
    {
        if (row.Item.RowVisibility != Visibility.Visible)
            return 0;

        return column switch
        {
            SensorDisplayColumn.Sensor => MeasureText(row.Item.Text) + SensorColumnPadding + row.Depth * TreeIndentWidth,
            SensorDisplayColumn.Value => _viewModel.ShowValueColumn ? MeasureText(row.Item.Value) + ValueColumnPadding : 0,
            SensorDisplayColumn.Min => _viewModel.ShowMinColumn ? MeasureText(row.Item.Min) + ValueColumnPadding : 0,
            SensorDisplayColumn.Max => _viewModel.ShowMaxColumn ? MeasureText(row.Item.Max) + ValueColumnPadding : 0,
            _ => 0
        };
    }

    private bool UpdateDisplayedColumnWidths()
    {
        double deviceColumnWidth = NormalizeDeviceColumnWidth(_measuredColumnWidths[0]);
        if (!_settled)
            deviceColumnWidth = Math.Max(deviceColumnWidth, _stableDeviceColumnWidth);

        double[] widths =
        [
            deviceColumnWidth,
            Math.Ceiling(_measuredColumnWidths[1]),
            Math.Ceiling(_measuredColumnWidths[2]),
            Math.Ceiling(_measuredColumnWidths[3])
        ];

        bool changed = false;
        for (int i = 0; i < _columnWidths.Length; i++)
        {
            if (Math.Abs(_columnWidths[i] - widths[i]) < 0.01)
                continue;

            _columnWidths[i] = widths[i];
            changed = true;
        }

        return changed;
    }

    private static IEnumerable<SensorDisplayColumn> EnumerateColumns(SensorDisplayColumn columns)
    {
        if ((columns & SensorDisplayColumn.Sensor) != 0)
            yield return SensorDisplayColumn.Sensor;
        if ((columns & SensorDisplayColumn.Value) != 0)
            yield return SensorDisplayColumn.Value;
        if ((columns & SensorDisplayColumn.Min) != 0)
            yield return SensorDisplayColumn.Min;
        if ((columns & SensorDisplayColumn.Max) != 0)
            yield return SensorDisplayColumn.Max;
    }

    private static int GetColumnIndex(SensorDisplayColumn column)
    {
        return column switch
        {
            SensorDisplayColumn.Sensor => 0,
            SensorDisplayColumn.Value => 1,
            SensorDisplayColumn.Min => 2,
            SensorDisplayColumn.Max => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null)
        };
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
        if (!_settled || _rows.Count == 0)
            return;

        double measuredWidth = NormalizeDeviceColumnWidth(_columnWidths[0]);
        _stableDeviceColumnWidth = measuredWidth;
        double savedWidth = NormalizeDeviceColumnWidth(_viewModel.Settings.GetValue(DeviceColumnWidthSetting, DefaultDeviceColumnWidth));
        if (Math.Abs(measuredWidth - savedWidth) < 0.5)
            return;

        _viewModel.Settings.SetValue(DeviceColumnWidthSetting, measuredWidth);
        _startupTrace.Mark("MainWindow.RecordDeviceColumnWidth", FormattableString.Invariant($"width={measuredWidth:F0}, rows={_rows.Count}"));
    }

    private sealed class RowMeasurement
    {
        public RowMeasurement(SensorTreeItemViewModel item, Grid grid, int depth)
        {
            Item = item;
            Grid = grid;
            Depth = depth;
        }

        public SensorTreeItemViewModel Item { get; }

        public Grid Grid { get; }

        public int Depth { get; }

        public double[] ColumnWidths { get; } = new double[4];

        public SensorDisplayColumn DirtyColumns { get; set; } = SensorDisplayColumn.All;
    }
}

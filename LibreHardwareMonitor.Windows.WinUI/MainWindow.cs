// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Composition;
using LibreHardwareMonitor.Windows.WinUI.Controls;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Services.Tracing;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using IOPath = System.IO.Path;

namespace LibreHardwareMonitor.Windows.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly WindowChromeManager _chromeManager;
    private readonly WindowPlacementService _placementService;
    private readonly DispatcherQueueTimer _timer;
    private readonly IStartupTracer _startupTrace;
    private readonly IServiceProvider _serviceProvider;
    private readonly TrayIconService _trayIconService;
    private readonly DialogService _dialogService;
    private readonly SensorColumnMeasurer _columnMeasurer;
    private readonly SecondaryWindowCoordinator _secondaryWindows;
    private bool _firstLayoutRecorded;
    private bool _isUpdating;
    private bool _initialWindowStateApplied;
    private bool _isClosingForExit;
    private bool _isShuttingDown;
    private bool _runtimeErrorLogged;
    private bool _isMonitoringStarted;
    private bool _startupCompletionRequested;
    private bool _startupCompleteRecorded;
    private bool _sensorTreeRebuildQueued;
    private bool _sensorColumnWidthUpdateQueued;

    internal MainWindow(
        MainWindowViewModel viewModel,
        IStartupTracer startupTrace,
        IMainWindowRuntimeFactory runtimeFactory,
        IServiceProvider serviceProvider)
    {
        ViewModel = viewModel;
        _startupTrace = startupTrace;
        _serviceProvider = serviceProvider;
        _startupTrace.Mark("MainWindow.Constructor.Begin");

        MeasureStartup("MainWindow.InitializeComponent", InitializeComponent);

        MainWindowRuntime runtime = MeasureStartup(
            "MainWindow.CreateRuntimeServices",
            () => runtimeFactory.Create(this, ViewModel, () => Content.XamlRoot, HideShowMainWindow));
        _appWindow = runtime.AppWindow;
        _chromeManager = runtime.ChromeManager;
        _placementService = runtime.PlacementService;
        _trayIconService = runtime.TrayIconService;
        _dialogService = runtime.DialogService;
        _secondaryWindows = runtime.SecondaryWindows;
        _columnMeasurer = runtime.ColumnMeasurer;

        _trayIconService.IsMainIconEnabled = ViewModel.MinimizeToTray;
        MeasureStartup("MainWindow.ApplySavedDeviceColumnWidth", _columnMeasurer.ApplySavedWidth, () => FormattableString.Invariant($"width={_columnMeasurer.DeviceColumnWidth:F0}"));
        _columnMeasurer.SettleTriggered += (_, _) => UpdateSensorColumnWidths();

        TryApplyMicaBackdrop();
        RootGrid.Loaded += RootGrid_Loaded;
        RootGrid.LayoutUpdated += RootGrid_LayoutUpdated;
        Bind(ContentGrid, UIElement.IsHitTestVisibleProperty, ViewModel, nameof(ViewModel.IsHardwareInteractionEnabled));

        MeasureStartup("MainWindow.PopulateMenuSubmenus", PopulateMenuSubmenus);
        MeasureStartup("MainWindow.PopulateSensorHeader", PopulateSensorHeader);
        MeasureStartup("MainWindow.AttachPlotView", () => PlotControl.AttachViewModel(ViewModel));

        MeasureStartup("MainWindow.RestoreWindowBounds", _placementService.Restore);
        MeasureStartup("MainWindow.MaximizeWindow", _placementService.Maximize);
        MeasureStartup("MainWindow.ApplyTheme", ApplyTheme);
        MeasureStartup("MainWindow.UpdatePlotLayout", UpdatePlotLayout);

        _timer = MeasureStartup("MainWindow.CreateTimer", () =>
        {
            DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
            timer.Interval = ViewModel.UpdateInterval;
            timer.Tick += UpdateTimer_Tick;
            return timer;
        });

        MeasureStartup("MainWindow.WireEvents", () =>
        {
            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ViewModel.UpdateInterval) or nameof(ViewModel.UpdateIntervalIndex))
                    _timer.Interval = ViewModel.UpdateInterval;

                if (args.PropertyName is nameof(ViewModel.ShowPlot)
                    or nameof(ViewModel.PlotLocation)
                    or nameof(ViewModel.PlotGridColumn)
                    or nameof(ViewModel.PlotGridRow))
                    UpdatePlotLayout();

                if (args.PropertyName is nameof(ViewModel.PlotStackedAxes)
                    or nameof(ViewModel.ShowPlotAxisLabels)
                    or nameof(ViewModel.PlotTimeWindowIndex))
                    DrawPlot();

                if (args.PropertyName == nameof(ViewModel.ShowGadget))
                    UpdateGadgetVisibility();

                if (args.PropertyName == nameof(ViewModel.MinimizeToTray))
                    _trayIconService.IsMainIconEnabled = ViewModel.MinimizeToTray;

                if (args.PropertyName == nameof(ViewModel.TemperatureUnit))
                {
                    _trayIconService.Update();
                    SyncGadgetSensors();
                    ViewModel.RefreshPlotSeries();
                }

                if (args.PropertyName == nameof(ViewModel.ShowHiddenSensors))
                    QueueSensorTreeRebuild();

                if (args.PropertyName is nameof(ViewModel.ShowValueColumn)
                    or nameof(ViewModel.ShowMinColumn)
                    or nameof(ViewModel.ShowMaxColumn))
                    QueueSensorColumnWidthUpdate();

                if (args.PropertyName == nameof(ViewModel.ThemeMode))
                    ApplyTheme();
            };
            ViewModel.RootItems.CollectionChanged += RootItems_CollectionChanged;
            ViewModel.PlotInvalidated += (_, _) => DrawPlot();
            ViewModel.GadgetSensorsChanged += (_, _) => SyncGadgetSensors();
            ViewModel.TraySensorsChanged += (_, _) => SyncTraySensors();
            _trayIconService.HideShowRequested += (_, _) => HideShowMainWindow();
            _trayIconService.ExitRequested += (_, _) => CloseApplication();
            _appWindow.Changed += AppWindow_Changed;

            Closed += MainWindow_Closed;
        });
        _startupTrace.Mark("MainWindow.Constructor.Complete");
    }

    public MainWindowViewModel ViewModel { get; }

    public void StartMonitoringAfterActivation()
    {
        _startupTrace.Mark("MainWindow.StartMonitoringAfterActivation.Queued");
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            if (_isMonitoringStarted)
                return;

            _isMonitoringStarted = true;
            try
            {
                _startupTrace.Mark("MainWindow.StartMonitoringAfterActivation.Begin");
                ApplyInitialWindowState();
                await MeasureStartupAsync("MainWindowViewModel.StartAsync", ViewModel.StartAsync);
                SyncTraySensors();
                SyncGadgetSensors();
                UpdateGadgetVisibility();
                MeasureStartup("MainWindow.StartTimer", _timer.Start);
                _startupTrace.Mark("MainWindow.StartMonitoringAfterActivation.Complete");
                RequestStartupTraceComplete();
                _startupTrace.Flush();
            }
            catch (Exception ex)
            {
                _startupTrace.Mark("MainWindow.StartMonitoringAfterActivation.Exception", $"{ex.GetType().FullName}: {ex.Message}");
                _startupTrace.Flush();
                RecordRuntimeError("Hardware initialization failed", ex);
            }
        }))
        {
            _startupTrace.Mark("MainWindow.StartMonitoringAfterActivation.EnqueueFailed");
            _startupTrace.Flush();
        }
    }

    private string GetRootSizeDetail()
    {
        return FormattableString.Invariant($"root={RootGrid.ActualWidth:F0}x{RootGrid.ActualHeight:F0}, rows={_columnMeasurer.RowCount}, rootNodes={SensorTree.RootNodes.Count}");
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        _startupTrace.Mark("MainWindow.RootLoaded", GetRootSizeDetail());
        _startupTrace.Flush();
    }

    private void RootGrid_LayoutUpdated(object? sender, object e)
    {
        if (_firstLayoutRecorded)
            return;

        _firstLayoutRecorded = true;
        RootGrid.LayoutUpdated -= RootGrid_LayoutUpdated;
        _startupTrace.Mark("MainWindow.FirstLayoutUpdated", GetRootSizeDetail());
        _startupTrace.Flush();
    }

    private void MeasureStartup(string phase, Action action)
    {
        if (_startupTrace.IsComplete)
        {
            action();
            return;
        }

        _startupTrace.Measure(phase, action);
    }

    private void MeasureStartup(string phase, Action action, Func<string> getDetail)
    {
        if (_startupTrace.IsComplete)
        {
            action();
            return;
        }

        _startupTrace.Measure(phase, action, getDetail);
    }

    private T MeasureStartup<T>(string phase, Func<T> action)
    {
        if (_startupTrace.IsComplete)
            return action();

        return _startupTrace.Measure(phase, action);
    }

    private async Task MeasureStartupAsync(string phase, Func<Task> action)
    {
        if (_startupTrace.IsComplete)
        {
            await action();
            return;
        }

        await _startupTrace.MeasureAsync(phase, action);
    }

    private void PopulateMenuSubmenus()
    {
        PopulateRadioSubMenu(TemperatureUnitMenu, [
            ("Celsius", TemperatureUnit.Celsius),
            ("Fahrenheit", TemperatureUnit.Fahrenheit)
        ], () => ViewModel.TemperatureUnit, v => ViewModel.TemperatureUnit = v);
        PopulateRadioSubMenu(PlotLocationMenu, [
            ("Window", PlotLocation.Window),
            ("Bottom", PlotLocation.Bottom),
            ("Right", PlotLocation.Right)
        ], () => ViewModel.PlotLocation, v => ViewModel.PlotLocation = v);
        PopulateRadioSubMenu(ThemeMenu, [
            ("Auto", AppThemeMode.Auto),
            ("Light", AppThemeMode.Light),
            ("Dark", AppThemeMode.Dark),
            ("Black", AppThemeMode.Black)
        ], () => ViewModel.ThemeMode, v => ViewModel.ThemeMode = v);
        PopulateRadioSubMenu(StrokeThicknessMenu, [
            ("1pt", 1),
            ("2pt", 2),
            ("3pt", 3),
            ("4pt", 4)
        ], () => (int)ViewModel.PlotStrokeThickness, v => ViewModel.PlotStrokeThickness = v);
        PopulateRadioSubMenu(FileRotationMenu, [
            ("Per Session", LoggerFileRotation.PerSession),
            ("Daily", LoggerFileRotation.Daily)
        ], () => ViewModel.FileRotationMethod, v => ViewModel.FileRotationMethod = v);
        PopulateRadioSubMenu(UpdateIntervalMenu, [
            ("250ms", 0), ("500ms", 1), ("1s", 2), ("2s", 3), ("5s", 4), ("10s", 5)
        ], () => ViewModel.UpdateIntervalIndex, v => ViewModel.UpdateIntervalIndex = v);
        PopulateRadioSubMenu(LoggingIntervalMenu, [
            ("1s", 0), ("2s", 1), ("5s", 2), ("10s", 3), ("30s", 4), ("1min", 5),
            ("2min", 6), ("5min", 7), ("10min", 8), ("30min", 9), ("1h", 10), ("2h", 11), ("6h", 12)
        ], () => ViewModel.LoggingIntervalIndex, v => ViewModel.LoggingIntervalIndex = v);
        PopulateRadioSubMenu(SensorTimeWindowMenu, [
            ("30s", 0), ("1min", 1), ("2min", 2), ("5min", 3), ("10min", 4),
            ("30min", 5), ("1h", 6), ("2h", 7), ("6h", 8), ("12h", 9), ("24h", 10)
        ], () => ViewModel.SensorValuesTimeWindowIndex, v => ViewModel.SensorValuesTimeWindowIndex = v);

        RunWebServerItem.IsEnabled = !ViewModel.IsWebServerUnavailable;
        WebServerInterfaceItem.IsEnabled = !ViewModel.IsWebServerUnavailable;
        WebServerAuthItem.IsEnabled = !ViewModel.IsWebServerUnavailable;
    }

    private static void PopulateRadioSubMenu<T>(MenuFlyoutSubItem subItem, (string Label, T Value)[] items, Func<T> getter, Action<T> setter)
    {
        foreach ((string label, T value) in items)
        {
            ToggleMenuFlyoutItem item = new()
            {
                Text = label,
                IsChecked = Equals(getter(), value),
                Tag = value
            };
            item.Click += (_, _) =>
            {
                setter(value);
                foreach (ToggleMenuFlyoutItem sibling in subItem.Items.OfType<ToggleMenuFlyoutItem>())
                    sibling.IsChecked = Equals(sibling.Tag, value);
            };
            subItem.Items.Add(item);
        }
    }

    private async void OnSaveReport(object sender, RoutedEventArgs e) => await _dialogService.SaveReportAsync();
    private void OnResetHardware(object sender, RoutedEventArgs e) => ViewModel.ResetHardware();
    private void OnExit(object sender, RoutedEventArgs e) => CloseApplication();
    private void OnResetMinMax(object sender, RoutedEventArgs e) => ViewModel.ResetMinMax();
    private void OnExpandAll(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllExpanded(true);
        RebuildSensorTree();
    }
    private void OnCollapseAll(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllExpanded(false);
        RebuildSensorTree();
    }
    private void OnResetPlot(object sender, RoutedEventArgs e) => ResetPlot();
    private async void OnWebServerSettings(object sender, RoutedEventArgs e) => await _dialogService.ShowWebServerSettingsAsync();
    private async void OnWebServerAuthentication(object sender, RoutedEventArgs e) => await _dialogService.ShowWebServerAuthenticationAsync();
    private async void OnAbout(object sender, RoutedEventArgs e) => await _dialogService.ShowAboutAsync();

    private void PopulateSensorHeader()
    {
        Grid header = _columnMeasurer.CreateHeaderGrid();
        header.Padding = new Thickness(8, 5, 8, 5);
        header.Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"];
        header.Children.Add(CreateHeaderText("Sensor", 0, Visibility.Visible));
        header.Children.Add(CreateHeaderText("Value", 1, ViewModel.ValueColumnVisibility, nameof(ViewModel.ValueColumnVisibility)));
        header.Children.Add(CreateHeaderText("Min", 2, ViewModel.MinColumnVisibility, nameof(ViewModel.MinColumnVisibility)));
        header.Children.Add(CreateHeaderText("Max", 3, ViewModel.MaxColumnVisibility, nameof(ViewModel.MaxColumnVisibility)));
        SensorHeaderHost.Children.Add(header);
    }

    private void RootItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueSensorTreeRebuild();
    }

    private void QueueSensorTreeRebuild()
    {
        if (_sensorTreeRebuildQueued)
            return;

        _sensorTreeRebuildQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _sensorTreeRebuildQueued = false;
            RebuildSensorTree();
        }))
        {
            _sensorTreeRebuildQueued = false;
        }
    }

    private void QueueSensorColumnWidthUpdate()
    {
        if (_sensorColumnWidthUpdateQueued)
            return;

        _sensorColumnWidthUpdateQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _sensorColumnWidthUpdateQueued = false;
            UpdateSensorColumnWidths();
        }))
        {
            _sensorColumnWidthUpdateQueued = false;
        }
    }

    private async void UpdateTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_isUpdating || _isShuttingDown)
            return;

        _isUpdating = true;
        try
        {
            await ViewModel.UpdateAsync();

            // The window may have been closed (and the view model/computer disposed) while we awaited the update.
            if (_isShuttingDown)
                return;

            UpdateSensorColumnWidths();
            _trayIconService.Update();
            _secondaryWindows.SyncGadgetSensors();
            DrawPlot();
        }
        catch (Exception ex)
        {
            // A transient update failure must not permanently freeze the UI: keep the timer running and just record it.
            RecordRuntimeError("Sensor update failed", ex);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void RecordRuntimeError(string message, Exception exception)
    {
        _startupTrace.Mark("MainWindow.RuntimeError", $"{message}: {exception.GetType().FullName}: {exception.Message}");
        _startupTrace.Flush();

        // Use a dedicated runtime log (not the startup log that App.xaml.cs appends to) and write it only once, so a
        // recurring per-tick failure neither clobbers the startup diagnostics nor grows the file without bound.
        string logPath = IOPath.Combine(AppContext.BaseDirectory, "LibreHardwareMonitor.Windows.WinUI.runtime.log");
        if (!_runtimeErrorLogged)
        {
            try
            {
                File.WriteAllText(logPath, exception.ToString());
            }
            catch
            {
                // Never let diagnostic logging throw out of the update loop.
            }

            _runtimeErrorLogged = true;
        }

        ViewModel.SetStatusText($"{message}. See {IOPath.GetFileName(logPath)}.");
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (!_isClosingForExit && ViewModel.MinimizeOnClose)
        {
            args.Handled = true;
            MinimizeOrHideMainWindow();
            return;
        }

        _isShuttingDown = true;
        try
        {
            _timer.Stop();
            _columnMeasurer.StopSettleTimer();
            _trayIconService.Dispose();
            _secondaryWindows.CloseAll();
            _placementService.Save();
        }
        finally
        {
            // The container owns the view model and domain services. Disposing the provider runs
            // MainWindowViewModel.Dispose first (reverse construction order) to persist settings while the
            // hardware monitor and web server are still alive, then disposes those services and the tracer.
            // This MUST run even if a teardown step above throws, so the ring0 driver is always unloaded.
            (_serviceProvider as IDisposable)?.Dispose();
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_isClosingForExit || !ViewModel.MinimizeToTray)
            return;

        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            HideMainWindowToTray();
    }

    private void ApplyInitialWindowState()
    {
        if (_initialWindowStateApplied)
            return;

        _initialWindowStateApplied = true;
        if (ViewModel.StartMinimized)
            MinimizeOrHideMainWindow();
    }

    private void CloseApplication()
    {
        _isClosingForExit = true;
        Close();
    }

    private void HideShowMainWindow()
    {
        if (_chromeManager.IsHiddenOrMinimizedOrInvisible)
        {
            _chromeManager.Restore();
            Activate();
        }
        else
        {
            HideMainWindowToTray();
        }
    }

    private void MinimizeOrHideMainWindow()
    {
        if (ViewModel.MinimizeToTray)
            HideMainWindowToTray();
        else
            _chromeManager.Minimize();
    }

    private void HideMainWindowToTray()
    {
        _chromeManager.HideToTray();
    }

    private void SyncTraySensors()
    {
        _trayIconService.RestoreSelectedSensors(ViewModel.GetTraySensorItems());
    }

    private void SyncGadgetSensors()
    {
        _secondaryWindows.SyncGadgetSensors();
    }

    private void UpdateGadgetVisibility()
    {
        _secondaryWindows.UpdateGadgetVisibility();
    }

    private void UpdatePlotWindowVisibility()
    {
        _secondaryWindows.UpdatePlotWindowVisibility();
    }

    private void RebuildSensorTree()
    {
        MeasureStartup("MainWindow.RebuildSensorTree", RebuildSensorTreeCore, GetRootSizeDetail);
        _columnMeasurer.ScheduleSettle();
        SyncTraySensors();
        SyncGadgetSensors();
        if (_startupCompletionRequested)
            CompleteStartupTraceIfReady();
    }

    private void RebuildSensorTreeCore()
    {
        _columnMeasurer.ResetRows();
        SensorTree.RootNodes.Clear();
        foreach (SensorTreeItemViewModel item in ViewModel.RootItems)
        {
            TreeViewNode? node = CreateTreeNode(item);
            if (node != null)
                SensorTree.RootNodes.Add(node);
        }

        UpdateSensorColumnWidths();
    }

    private void CompleteStartupTraceIfReady()
    {
        if (_startupCompleteRecorded || _startupTrace.IsComplete || _columnMeasurer.RowCount == 0)
            return;

        _startupCompleteRecorded = true;
        _startupTrace.Complete("MainWindow.StartupComplete", GetRootSizeDetail());
    }

    private void RequestStartupTraceComplete()
    {
        _startupCompletionRequested = true;
        if (!DispatcherQueue.TryEnqueue(CompleteStartupTraceIfReady))
            CompleteStartupTraceIfReady();
    }

    private TreeViewNode? CreateTreeNode(SensorTreeItemViewModel item)
    {
        if (item.RowVisibility == Visibility.Collapsed)
            return null;

        TreeViewNode node = new()
        {
            Content = CreateSensorRow(item),
            IsExpanded = item.IsExpanded
        };

        foreach (SensorTreeItemViewModel child in item.Children)
        {
            TreeViewNode? childNode = CreateTreeNode(child);
            if (childNode != null)
                node.Children.Add(childNode);
        }

        return node;
    }

    private FrameworkElement CreateSensorRow(SensorTreeItemViewModel item)
    {
        Grid row = _columnMeasurer.CreateRowGrid();
        row.DataContext = item;
        row.Padding = new Thickness(0, 3, 0, 3);

        StackPanel sensorCell = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        CheckBox plotCheck = new() { MinWidth = 22 };
        Bind(plotCheck, ToggleButton.IsCheckedProperty, item, nameof(SensorTreeItemViewModel.Plot), BindingMode.TwoWay);
        Bind(plotCheck, UIElement.VisibilityProperty, item, nameof(SensorTreeItemViewModel.PlotCheckVisibility));
        plotCheck.Checked += (_, _) => QueuePlotSeriesRefresh();
        plotCheck.Unchecked += (_, _) => QueuePlotSeriesRefresh();
        sensorCell.Children.Add(plotCheck);

        FontIcon icon = new() { FontSize = 14 };
        Bind(icon, FontIcon.GlyphProperty, item, nameof(SensorTreeItemViewModel.IconGlyph));
        sensorCell.Children.Add(icon);

        TextBlock name = new() { TextTrimming = TextTrimming.CharacterEllipsis };
        Bind(name, TextBlock.TextProperty, item, nameof(SensorTreeItemViewModel.Text));
        ToolTip toolTip = new();
        Bind(toolTip, ContentControl.ContentProperty, item, nameof(SensorTreeItemViewModel.ToolTip));
        ToolTipService.SetToolTip(name, toolTip);
        sensorCell.Children.Add(name);

        row.Children.Add(sensorCell);
        row.Children.Add(CreateValueTextBlock(item, nameof(SensorTreeItemViewModel.Value), nameof(SensorTreeItemViewModel.ValueColumnVisibility), 1));
        row.Children.Add(CreateValueTextBlock(item, nameof(SensorTreeItemViewModel.Min), nameof(SensorTreeItemViewModel.MinColumnVisibility), 2));
        row.Children.Add(CreateValueTextBlock(item, nameof(SensorTreeItemViewModel.Max), nameof(SensorTreeItemViewModel.MaxColumnVisibility), 3));

        row.ContextFlyout = BuildSensorContextMenu(item);
        row.DoubleTapped += async (_, _) =>
        {
            if (item.Sensor?.Parameters.Count > 0)
                await _dialogService.ShowParametersAsync(item);
        };
        return row;
    }

    private MenuFlyout BuildSensorContextMenu(SensorTreeItemViewModel item)
    {
        MenuFlyout flyout = new();
        if (item.Sensor?.Parameters.Count > 0)
            flyout.Items.Add(CreateMenuItem("Parameters...", async (_, _) => await _dialogService.ShowParametersAsync(item)));

        MenuFlyoutItem rename = CreateMenuItem("Rename", async (_, _) => await _dialogService.RenameAsync(item));
        rename.IsEnabled = item.CanRename;
        flyout.Items.Add(rename);

        ToggleMenuFlyoutItem plot = new() { Text = "Show in Plot", IsEnabled = item.Sensor != null };
        Bind(plot, ToggleMenuFlyoutItem.IsCheckedProperty, item, nameof(SensorTreeItemViewModel.Plot), BindingMode.TwoWay);
        plot.Click += (_, _) => QueuePlotSeriesRefresh();
        flyout.Items.Add(plot);

        ToggleMenuFlyoutItem tray = new()
        {
            Text = "Show in Tray",
            IsChecked = ViewModel.IsSensorInTray(item),
            IsEnabled = item.Sensor != null
        };
        tray.Click += (_, _) =>
        {
            ViewModel.SetSensorInTray(item, tray.IsChecked);
            SyncTraySensors();
        };
        flyout.Items.Add(tray);

        ToggleMenuFlyoutItem gadget = new()
        {
            Text = "Show in Gadget",
            IsChecked = ViewModel.IsSensorInGadget(item),
            IsEnabled = item.Sensor != null
        };
        gadget.Click += (_, _) =>
        {
            ViewModel.SetSensorInGadget(item, gadget.IsChecked);
            SyncGadgetSensors();
        };
        flyout.Items.Add(gadget);

        MenuFlyoutItem hide = CreateMenuItem(item.IsVisible ? "Hide Sensor" : "Unhide Sensor", (_, _) =>
        {
            if (item.CanHide)
            {
                item.IsVisible = !item.IsVisible;
                RebuildSensorTree();
            }
        });
        hide.IsEnabled = item.CanHide;
        flyout.Items.Add(hide);
        flyout.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem penColor = CreateMenuItem("Pen Color...", async (_, _) => await _dialogService.ShowPenColorAsync(item));
        penColor.IsEnabled = item.Sensor != null;
        flyout.Items.Add(penColor);
        MenuFlyoutItem resetPenColor = CreateMenuItem("Reset Pen Color", (_, _) => ViewModel.SetSensorPenColor(item, null));
        resetPenColor.IsEnabled = item.PenColor != null;
        flyout.Items.Add(resetPenColor);

        if (item.Sensor?.Control != null)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(BuildControlMenu(item.Sensor.Control));
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateMenuItem("Reset Min/Max", (_, _) => ViewModel.ResetMinMax()));
        return flyout;
    }

    private void QueuePlotSeriesRefresh()
    {
        if (!DispatcherQueue.TryEnqueue(ViewModel.RefreshPlotSeries))
            ViewModel.RefreshPlotSeries();
    }

    private static MenuFlyoutSubItem BuildControlMenu(IControl control)
    {
        MenuFlyoutSubItem controlItem = new() { Text = "Control" };
        ToggleMenuFlyoutItem defaultItem = new() { Text = "Default", IsChecked = control.ControlMode == ControlMode.Default };
        defaultItem.Click += (_, _) => control.SetDefault();
        controlItem.Items.Add(defaultItem);

        MenuFlyoutSubItem manual = new() { Text = "Manual" };
        for (int value = 0; value <= 100; value += 5)
        {
            if (value > control.MaxSoftwareValue || value < control.MinSoftwareValue)
                continue;

            int controlValue = value;
            ToggleMenuFlyoutItem item = new()
            {
                Text = $"{controlValue} %",
                IsChecked = control.ControlMode == ControlMode.Software && Math.Abs(control.SoftwareValue - controlValue) < 0.1f
            };
            item.Click += (_, _) => control.SetSoftware(controlValue);
            manual.Items.Add(item);
        }

        controlItem.Items.Add(manual);
        return controlItem;
    }

    private void SensorTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count == 0)
        {
            ViewModel.SelectedItem = null;
            return;
        }

        object selected = args.AddedItems[0];
        if (selected is TreeViewNode node && node.Content is FrameworkElement element)
            ViewModel.SelectedItem = element.DataContext as SensorTreeItemViewModel;
    }

    private void ResetPlot()
    {
        ViewModel.ResetPlot();
        PlotControl.ResetZoom();
        _secondaryWindows.RedrawPlot();
    }

    private void DrawPlot()
    {
        if (ViewModel.PlotLocation == PlotLocation.Window)
            PlotControl.Clear();
        else
            PlotControl.Redraw();

        _secondaryWindows.RedrawPlot();
    }

    private void UpdatePlotLayout()
    {
        ContentGrid.ColumnDefinitions[1].Width = ViewModel.ShowPlot && ViewModel.PlotLocation == PlotLocation.Right ? new GridLength(320) : new GridLength(0);
        ContentGrid.RowDefinitions[1].Height = ViewModel.ShowPlot && ViewModel.PlotLocation == PlotLocation.Bottom ? new GridLength(220) : new GridLength(0);

        PlotPane.Visibility = ViewModel.PlotVisibility;
        Grid.SetRow(PlotPane, ViewModel.PlotGridRow);
        Grid.SetColumn(PlotPane, ViewModel.PlotGridColumn);
        Grid.SetRowSpan(PlotPane, ViewModel.PlotGridRowSpan);
        Grid.SetColumnSpan(PlotPane, ViewModel.PlotGridColumnSpan);
        Grid.SetRowSpan(SensorPane, ViewModel.SensorGridRowSpan);
        Grid.SetColumnSpan(SensorPane, ViewModel.SensorGridColumnSpan);
        UpdatePlotWindowVisibility();
        DrawPlot();
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = ViewModel.ThemeMode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark or AppThemeMode.Black => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (ViewModel.ThemeMode == AppThemeMode.Black)
        {
            SystemBackdrop = null;
            RootGrid.Background = new SolidColorBrush(Colors.Black);
        }
        else if (SystemBackdrop is MicaBackdrop)
        {
            RootGrid.Background = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            TryApplyMicaBackdrop();
            RootGrid.Background = SystemBackdrop is MicaBackdrop
                ? new SolidColorBrush(Colors.Transparent)
                : (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        }

        PlotControl.ApplyTheme(ViewModel.ThemeMode);
        _secondaryWindows.ApplyTheme(ViewModel.ThemeMode);
    }

    private void TryApplyMicaBackdrop()
    {
        if (SystemBackdrop is MicaBackdrop)
            return;

        try
        {
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        }
        catch
        {
            // Mica unavailable (e.g. Win10) — fall back to theme brush in ApplyTheme.
        }
    }

    private void UpdateSensorColumnWidths()
    {
        MeasureStartup("MainWindow.UpdateSensorColumnWidths", _columnMeasurer.UpdateWidths, () => FormattableString.Invariant($"rows={_columnMeasurer.RowCount}, cacheEntries={_columnMeasurer.CacheEntryCount}, deviceWidth={_columnMeasurer.DeviceColumnWidth:F0}, settled={_columnMeasurer.IsSettled}"));
    }

    private TextBlock CreateHeaderText(string text, int column, Visibility visibility, string? bindingPath = null)
    {
        TextBlock textBlock = new()
        {
            Text = text,
            FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 },
            Visibility = visibility
        };
        if (bindingPath != null)
            Bind(textBlock, UIElement.VisibilityProperty, ViewModel, bindingPath);
        Grid.SetColumn(textBlock, column);
        return textBlock;
    }

    private static TextBlock CreateValueTextBlock(SensorTreeItemViewModel item, string textPath, string visibilityPath, int column)
    {
        TextBlock textBlock = new() { TextTrimming = TextTrimming.CharacterEllipsis };
        Bind(textBlock, TextBlock.TextProperty, item, textPath);
        Bind(textBlock, UIElement.VisibilityProperty, item, visibilityPath);
        Grid.SetColumn(textBlock, column);
        return textBlock;
    }

    private static MenuFlyoutItem CreateMenuItem(string text, RoutedEventHandler handler)
    {
        MenuFlyoutItem item = new() { Text = text };
        item.Click += handler;
        return item;
    }

    private static void Bind(DependencyObject target, DependencyProperty property, object source, string path, BindingMode mode = BindingMode.OneWay)
    {
        BindingOperations.SetBinding(target, property, new Binding
        {
            Source = source,
            Path = new PropertyPath(path),
            Mode = mode,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
    }
}

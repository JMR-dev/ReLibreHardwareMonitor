// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Controls;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;
using IOPath = System.IO.Path;

namespace LibreHardwareMonitor.Windows.WinUI;

public sealed class MainWindow : Window
{
    private static readonly TimeSpan DeviceColumnWidthSettleDelay = TimeSpan.FromSeconds(5);

    private const double DefaultSensorColumnWidth = 320;
    private const double MaximumSensorColumnWidth = 4096;
    private const double MinimumSensorColumnWidth = 120;
    private const double SensorColumnPadding = 72;
    private const double SensorTreeIndentWidth = 20;
    private const string DeviceColumnWidthSetting = "winui.deviceColumnWidth";
    private const double ValueColumnPadding = 18;
    private const int MaxTextMeasurementCacheEntries = 4096;

    private readonly AppWindow _appWindow;
    private readonly Grid _contentGrid;
    private readonly DispatcherQueueTimer _deviceColumnWidthSettleTimer;
    private readonly DispatcherQueueTimer _timer;
    private readonly PlotView _plotView;
    private readonly Grid _plotPane;
    private readonly Grid _rootGrid;
    private readonly TreeView _sensorTree;
    private readonly Grid _sensorPane;
    private readonly WinUiStartupTrace? _startupTrace;
    private readonly TrayIconService _trayIconService;
    private readonly DialogService _dialogService;
    private readonly Dictionary<(string Text, bool Bold), double> _textMeasurementCache = new();
    private TextBlock? _measurementTextBlock;
    private readonly List<Grid> _sensorRowGrids = [];
    private readonly double[] _sensorColumnWidths = [DefaultSensorColumnWidth, 120, 120, 120];
    private PlotWindow? _plotWindow;
    private Grid? _sensorHeader;
    private SensorGadgetWindow? _gadgetWindow;
    private double _stableDeviceColumnWidth = DefaultSensorColumnWidth;
    private bool _deviceColumnWidthSettled;
    private bool _firstLayoutRecorded;
    private bool _isUpdating;
    private bool _initialWindowStateApplied;
    private bool _isClosingForExit;
    private bool _isShuttingDown;
    private bool _runtimeErrorLogged;
    private bool _isMainWindowHidden;
    private bool _isMonitoringStarted;
    private bool _startupCompletionRequested;
    private bool _startupCompleteRecorded;
    private bool _sensorTreeRebuildQueued;
    private bool _sensorColumnWidthUpdateQueued;

    public MainWindow() : this(null)
    {
    }

    internal MainWindow(WinUiStartupTrace? startupTrace)
    {
        _startupTrace = startupTrace;
        _startupTrace?.Mark("MainWindow.Constructor.Begin");
        AppSettings settings = MeasureStartup("MainWindow.LoadSettings", AppSettings.LoadDefault);
        ViewModel = MeasureStartup("MainWindow.CreateViewModel", () => new MainWindowViewModel(settings, _startupTrace));
        MeasureStartup("MainWindow.ApplySavedDeviceColumnWidth", () => ApplySavedDeviceColumnWidth(settings), () => FormattableString.Invariant($"width={_sensorColumnWidths[0]:F0}"));

        _appWindow = MeasureStartup("MainWindow.GetAppWindow", () =>
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Title = "Libre Hardware Monitor";
            return appWindow;
        });

        _trayIconService = MeasureStartup("MainWindow.CreateTrayIconService", () => new TrayIconService(
            WindowNative.GetWindowHandle(this),
            settings,
            () => ViewModel.TemperatureUnit));
        _trayIconService.IsMainIconEnabled = ViewModel.MinimizeToTray;
        _dialogService = new DialogService(() => Content.XamlRoot, ViewModel, WindowNative.GetWindowHandle(this));

        Grid root = MeasureStartup("MainWindow.BuildRoot", BuildRoot);
        MeasureStartup("MainWindow.AssignContent", () => Content = root);
        _rootGrid = root;
        _rootGrid.Loaded += RootGrid_Loaded;
        _rootGrid.LayoutUpdated += RootGrid_LayoutUpdated;

        (Grid contentGrid, Grid sensorPane, Grid plotPane, TreeView sensorTree, PlotView plotView) = MeasureStartup("MainWindow.ResolveControls", () =>
        {
            Grid resolvedContentGrid = (Grid)root.Children[1];
            Grid resolvedSensorPane = (Grid)resolvedContentGrid.Children[0];
            Grid resolvedPlotPane = (Grid)resolvedContentGrid.Children[1];
            TreeView resolvedSensorTree = (TreeView)((Grid)resolvedSensorPane.Children[1]).Children[0];
            PlotView resolvedPlotView = (PlotView)resolvedPlotPane.Children[1];
            return (resolvedContentGrid, resolvedSensorPane, resolvedPlotPane, resolvedSensorTree, resolvedPlotView);
        });
        _contentGrid = contentGrid;
        _sensorPane = sensorPane;
        _plotPane = plotPane;
        _sensorTree = sensorTree;
        _plotView = plotView;

        MeasureStartup("MainWindow.RestoreWindowBounds", RestoreWindowBounds);
        MeasureStartup("MainWindow.MaximizeWindow", MaximizeWindow);
        MeasureStartup("MainWindow.ApplyTheme", ApplyTheme);
        MeasureStartup("MainWindow.UpdatePlotLayout", UpdatePlotLayout);

        (_timer, _deviceColumnWidthSettleTimer) = MeasureStartup("MainWindow.CreateTimers", () =>
        {
            DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
            timer.Interval = ViewModel.UpdateInterval;
            timer.Tick += UpdateTimer_Tick;

            DispatcherQueueTimer settleTimer = DispatcherQueue.CreateTimer();
            settleTimer.Interval = DeviceColumnWidthSettleDelay;
            settleTimer.Tick += DeviceColumnWidthSettleTimer_Tick;

            return (timer, settleTimer);
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
        _startupTrace?.Mark("MainWindow.Constructor.Complete");
    }

    public MainWindowViewModel ViewModel { get; }

    public void StartMonitoringAfterActivation()
    {
        _startupTrace?.Mark("MainWindow.StartMonitoringAfterActivation.Queued");
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            if (_isMonitoringStarted)
                return;

            _isMonitoringStarted = true;
            try
            {
                _startupTrace?.Mark("MainWindow.StartMonitoringAfterActivation.Begin");
                ApplyInitialWindowState();
                await MeasureStartupAsync("MainWindowViewModel.StartAsync", ViewModel.StartAsync);
                SyncTraySensors();
                SyncGadgetSensors();
                UpdateGadgetVisibility();
                MeasureStartup("MainWindow.StartTimer", _timer.Start);
                _startupTrace?.Mark("MainWindow.StartMonitoringAfterActivation.Complete");
                RequestStartupTraceComplete();
                _startupTrace?.Flush();
            }
            catch (Exception ex)
            {
                _startupTrace?.Mark("MainWindow.StartMonitoringAfterActivation.Exception", $"{ex.GetType().FullName}: {ex.Message}");
                _startupTrace?.Flush();
                RecordRuntimeError("Hardware initialization failed", ex);
            }
        }))
        {
            _startupTrace?.Mark("MainWindow.StartMonitoringAfterActivation.EnqueueFailed");
            _startupTrace?.Flush();
        }
    }

    private string GetRootSizeDetail()
    {
        return FormattableString.Invariant($"root={_rootGrid.ActualWidth:F0}x{_rootGrid.ActualHeight:F0}, rows={_sensorRowGrids.Count}, rootNodes={_sensorTree.RootNodes.Count}");
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _rootGrid.Loaded -= RootGrid_Loaded;
        _startupTrace?.Mark("MainWindow.RootLoaded", GetRootSizeDetail());
        _startupTrace?.Flush();
    }

    private void RootGrid_LayoutUpdated(object? sender, object e)
    {
        if (_firstLayoutRecorded)
            return;

        _firstLayoutRecorded = true;
        _rootGrid.LayoutUpdated -= RootGrid_LayoutUpdated;
        _startupTrace?.Mark("MainWindow.FirstLayoutUpdated", GetRootSizeDetail());
        _startupTrace?.Flush();
    }

    private void MeasureStartup(string phase, Action action)
    {
        if (_startupTrace is not { IsComplete: false })
        {
            action();
            return;
        }

        _startupTrace.Measure(phase, action);
    }

    private void MeasureStartup(string phase, Action action, Func<string> getDetail)
    {
        if (_startupTrace is not { IsComplete: false })
        {
            action();
            return;
        }

        _startupTrace.Measure(phase, action, getDetail);
    }

    private T MeasureStartup<T>(string phase, Func<T> action)
    {
        if (_startupTrace is not { IsComplete: false })
            return action();

        return _startupTrace.Measure(phase, action);
    }

    private async Task MeasureStartupAsync(string phase, Func<Task> action)
    {
        if (_startupTrace is not { IsComplete: false })
        {
            await action();
            return;
        }

        await _startupTrace.MeasureAsync(phase, action);
    }

    private Grid BuildRoot()
    {
        Grid root = new()
        {
            Background = new SolidColorBrush(Colors.White),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        root.Children.Add(MeasureStartup("MainWindow.BuildMenuBar", BuildMenuBar));

        Grid contentGrid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(320) }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(220) }
            }
        };
        Bind(contentGrid, UIElement.IsHitTestVisibleProperty, ViewModel, nameof(ViewModel.IsHardwareInteractionEnabled));
        Grid.SetRow(contentGrid, 1);

        contentGrid.Children.Add(MeasureStartup("MainWindow.BuildSensorPane", BuildSensorPane));
        contentGrid.Children.Add(MeasureStartup("MainWindow.BuildPlotPane", BuildPlotPane));
        root.Children.Add(contentGrid);

        TextBlock statusText = new()
        {
            Padding = new Thickness(8, 4, 8, 4),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Bind(statusText, TextBlock.TextProperty, ViewModel, nameof(ViewModel.StatusText));
        Grid.SetRow(statusText, 2);
        root.Children.Add(statusText);

        root.Children.Add(BuildLoadingOverlay());

        return root;
    }

    private MenuBar BuildMenuBar()
    {
        MenuBar menuBar = new();
        Bind(menuBar, Control.IsEnabledProperty, ViewModel, nameof(ViewModel.IsHardwareInteractionEnabled));

        MenuBarItem file = new() { Title = "File" };
        file.Items.Add(CreateMenuItem("Save Report...", async (_, _) => await _dialogService.SaveReportAsync()));
        file.Items.Add(new MenuFlyoutSeparator());
        file.Items.Add(CreateMenuItem("Reset", (_, _) => ViewModel.ResetHardware()));
        MenuFlyoutSubItem hardware = new() { Text = "Hardware" };
        hardware.Items.Add(CreateToggleItem("Motherboard", nameof(ViewModel.IsMotherboardEnabled)));
        hardware.Items.Add(CreateToggleItem("CPU", nameof(ViewModel.IsCpuEnabled)));
        hardware.Items.Add(CreateToggleItem("Memory", nameof(ViewModel.IsMemoryEnabled)));
        hardware.Items.Add(CreateToggleItem("GPU", nameof(ViewModel.IsGpuEnabled)));
        hardware.Items.Add(CreateToggleItem("Power Monitors", nameof(ViewModel.IsPowerMonitorEnabled)));
        hardware.Items.Add(CreateToggleItem("Fan Controllers", nameof(ViewModel.IsControllerEnabled)));
        hardware.Items.Add(CreateToggleItem("Storage Devices", nameof(ViewModel.IsStorageEnabled)));
        hardware.Items.Add(CreateToggleItem("Network", nameof(ViewModel.IsNetworkEnabled)));
        hardware.Items.Add(CreateToggleItem("Power Supplies", nameof(ViewModel.IsPsuEnabled)));
        hardware.Items.Add(CreateToggleItem("Battery", nameof(ViewModel.IsBatteryEnabled)));
        file.Items.Add(hardware);
        file.Items.Add(new MenuFlyoutSeparator());
        file.Items.Add(CreateMenuItem("Exit", (_, _) => CloseApplication()));
        menuBar.Items.Add(file);

        MenuBarItem view = new() { Title = "View" };
        view.Items.Add(CreateMenuItem("Reset Min/Max", (_, _) => ViewModel.ResetMinMax()));
        view.Items.Add(CreateMenuItem("Expand All Nodes", (_, _) =>
        {
            ViewModel.SetAllExpanded(true);
            RebuildSensorTree();
        }));
        view.Items.Add(CreateMenuItem("Collapse All Nodes", (_, _) =>
        {
            ViewModel.SetAllExpanded(false);
            RebuildSensorTree();
        }));
        view.Items.Add(CreateMenuItem("Reset Plot", (_, _) => ResetPlot()));
        view.Items.Add(new MenuFlyoutSeparator());
        view.Items.Add(CreateToggleItem("Show Hidden Sensors", nameof(ViewModel.ShowHiddenSensors)));
        view.Items.Add(CreateToggleItem("Show Plot", nameof(ViewModel.ShowPlot)));
        view.Items.Add(CreateToggleItem("Gadget", nameof(ViewModel.ShowGadget)));
        MenuFlyoutSubItem columns = new() { Text = "Columns" };
        columns.Items.Add(CreateToggleItem("Value", nameof(ViewModel.ShowValueColumn)));
        columns.Items.Add(CreateToggleItem("Min", nameof(ViewModel.ShowMinColumn)));
        columns.Items.Add(CreateToggleItem("Max", nameof(ViewModel.ShowMaxColumn)));
        view.Items.Add(columns);
        menuBar.Items.Add(view);

        MenuBarItem options = new() { Title = "Options" };
        options.Items.Add(CreateToggleItem("Start Minimized", nameof(ViewModel.StartMinimized)));
        options.Items.Add(CreateToggleItem("Minimize To Tray", nameof(ViewModel.MinimizeToTray)));
        options.Items.Add(CreateToggleItem("Minimize On Close", nameof(ViewModel.MinimizeOnClose)));
        options.Items.Add(CreateToggleItem("Run On Windows Startup", nameof(ViewModel.RunOnStartup)));
        options.Items.Add(new MenuFlyoutSeparator());
        options.Items.Add(BuildRadioSubMenu("Temperature Unit", [
            ("Celsius", TemperatureUnit.Celsius),
            ("Fahrenheit", TemperatureUnit.Fahrenheit)
        ], () => ViewModel.TemperatureUnit, value => ViewModel.TemperatureUnit = value));
        options.Items.Add(BuildRadioSubMenu("Plot Location", [
            ("Window", PlotLocation.Window),
            ("Bottom", PlotLocation.Bottom),
            ("Right", PlotLocation.Right)
        ], () => ViewModel.PlotLocation, value => ViewModel.PlotLocation = value));
        options.Items.Add(BuildRadioSubMenu("Theme", [
            ("Auto", AppThemeMode.Auto),
            ("Light", AppThemeMode.Light),
            ("Dark", AppThemeMode.Dark),
            ("Black", AppThemeMode.Black)
        ], () => ViewModel.ThemeMode, value => ViewModel.ThemeMode = value));
        options.Items.Add(BuildIndexedSubMenu("Stroke Thickness", [
            ("1pt", 1),
            ("2pt", 2),
            ("3pt", 3),
            ("4pt", 4)
        ], () => (int)ViewModel.PlotStrokeThickness, value => ViewModel.PlotStrokeThickness = value));
        options.Items.Add(new MenuFlyoutSeparator());
        options.Items.Add(CreateToggleItem("Log Sensors", nameof(ViewModel.LogSensors)));
        options.Items.Add(CreateToggleItem("Force Drive Wakeup", nameof(ViewModel.ForceDriveWakeup)));
        options.Items.Add(CreateToggleItem("Throttle ATA Storage", nameof(ViewModel.ThrottleAtaUpdate)));
        options.Items.Add(BuildRadioSubMenu("File Rotation Method", [
            ("Per Session", LoggerFileRotation.PerSession),
            ("Daily", LoggerFileRotation.Daily)
        ], () => ViewModel.FileRotationMethod, value => ViewModel.FileRotationMethod = value));
        options.Items.Add(BuildIndexedSubMenu("Update Interval", [
            ("250ms", 0),
            ("500ms", 1),
            ("1s", 2),
            ("2s", 3),
            ("5s", 4),
            ("10s", 5)
        ], () => ViewModel.UpdateIntervalIndex, value => ViewModel.UpdateIntervalIndex = value));
        options.Items.Add(BuildIndexedSubMenu("Logging Interval", [
            ("1s", 0),
            ("2s", 1),
            ("5s", 2),
            ("10s", 3),
            ("30s", 4),
            ("1min", 5),
            ("2min", 6),
            ("5min", 7),
            ("10min", 8),
            ("30min", 9),
            ("1h", 10),
            ("2h", 11),
            ("6h", 12)
        ], () => ViewModel.LoggingIntervalIndex, value => ViewModel.LoggingIntervalIndex = value));
        options.Items.Add(BuildIndexedSubMenu("Sensor Values Time Window", [
            ("30s", 0),
            ("1min", 1),
            ("2min", 2),
            ("5min", 3),
            ("10min", 4),
            ("30min", 5),
            ("1h", 6),
            ("2h", 7),
            ("6h", 8),
            ("12h", 9),
            ("24h", 10)
        ], () => ViewModel.SensorValuesTimeWindowIndex, value => ViewModel.SensorValuesTimeWindowIndex = value));
        options.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutSubItem webServer = new() { Text = "Remote Web Server" };
        ToggleMenuFlyoutItem runWebServer = CreateToggleItem("Run", nameof(ViewModel.RunWebServer));
        runWebServer.IsEnabled = !ViewModel.IsWebServerUnavailable;
        webServer.Items.Add(runWebServer);
        MenuFlyoutItem interfacePort = CreateMenuItem("Interface / Port", async (_, _) => await _dialogService.ShowWebServerSettingsAsync());
        interfacePort.IsEnabled = !ViewModel.IsWebServerUnavailable;
        webServer.Items.Add(interfacePort);
        MenuFlyoutItem authentication = CreateMenuItem("Authentication", async (_, _) => await _dialogService.ShowWebServerAuthenticationAsync());
        authentication.IsEnabled = !ViewModel.IsWebServerUnavailable;
        webServer.Items.Add(authentication);
        options.Items.Add(webServer);
        menuBar.Items.Add(options);

        MenuBarItem help = new() { Title = "Help" };
        help.Items.Add(CreateMenuItem("About", async (_, _) => await _dialogService.ShowAboutAsync()));
        menuBar.Items.Add(help);

        return menuBar;
    }

    private Grid BuildLoadingOverlay()
    {
        Grid overlay = new()
        {
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(150, 128, 128, 128)),
            Visibility = ViewModel.HardwareLoadingVisibility,
            IsHitTestVisible = true
        };
        Bind(overlay, UIElement.VisibilityProperty, ViewModel, nameof(ViewModel.HardwareLoadingVisibility));
        Grid.SetRowSpan(overlay, 3);
        Canvas.SetZIndex(overlay, 1000);

        StackPanel content = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12
        };
        ProgressRing progressRing = new()
        {
            Width = 64,
            Height = 64,
            IsIndeterminate = true
        };
        TextBlock text = new()
        {
            Text = "Loading hardware devices...",
            FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 },
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(progressRing);
        content.Children.Add(text);
        overlay.Children.Add(content);

        return overlay;
    }

    private Grid BuildSensorPane()
    {
        Grid pane = new()
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };

        Grid header = CreateSensorRowGrid();
        _sensorHeader = header;
        header.Padding = new Thickness(8, 5, 8, 5);
        header.Background = (Brush)Application.Current.Resources["SystemControlBackgroundChromeMediumLowBrush"];
        header.Children.Add(CreateHeaderText("Sensor", 0, Visibility.Visible));
        header.Children.Add(CreateHeaderText("Value", 1, ViewModel.ValueColumnVisibility, nameof(ViewModel.ValueColumnVisibility)));
        header.Children.Add(CreateHeaderText("Min", 2, ViewModel.MinColumnVisibility, nameof(ViewModel.MinColumnVisibility)));
        header.Children.Add(CreateHeaderText("Max", 3, ViewModel.MaxColumnVisibility, nameof(ViewModel.MaxColumnVisibility)));
        pane.Children.Add(header);

        Grid treeHost = new();
        Grid.SetRow(treeHost, 1);
        TreeView tree = new()
        {
            ItemTemplate = CreateTreeViewNodeContentTemplate(),
            SelectionMode = TreeViewSelectionMode.Single
        };
        tree.SelectionChanged += SensorTree_SelectionChanged;
        treeHost.Children.Add(tree);
        pane.Children.Add(treeHost);

        return pane;
    }

    private Grid BuildPlotPane()
    {
        Grid pane = new()
        {
            Padding = new Thickness(8),
            BorderBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"],
            BorderThickness = new Thickness(1, 0, 0, 0),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };

        Grid toolbar = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        toolbar.Children.Add(new TextBlock { Text = "Plot", FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 }, VerticalAlignment = VerticalAlignment.Center });
        Button reset = new() { Content = "Reset" };
        reset.Click += (_, _) => ResetPlot();
        Grid.SetColumn(reset, 1);
        toolbar.Children.Add(reset);
        pane.Children.Add(toolbar);

        PlotView plotView = new(ViewModel);
        Grid.SetRow(plotView, 1);
        pane.Children.Add(plotView);

        return pane;
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
            _gadgetWindow?.UpdateSensors(ViewModel.GetGadgetSensorItems());
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
        _startupTrace?.Mark("MainWindow.RuntimeError", $"{message}: {exception.GetType().FullName}: {exception.Message}");
        _startupTrace?.Flush();

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

    private void ApplySavedDeviceColumnWidth(AppSettings settings)
    {
        _stableDeviceColumnWidth = NormalizeDeviceColumnWidth(settings.GetValue(DeviceColumnWidthSetting, DefaultSensorColumnWidth));
        _sensorColumnWidths[0] = _stableDeviceColumnWidth;
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
        _timer.Stop();
        _deviceColumnWidthSettleTimer.Stop();
        _trayIconService.Dispose();
        CloseSecondaryWindows();
        SaveWindowBounds();
        ViewModel.Dispose();
        _startupTrace?.Dispose();
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

    private void CloseSecondaryWindows()
    {
        if (_gadgetWindow != null)
        {
            SensorGadgetWindow gadgetWindow = _gadgetWindow;
            _gadgetWindow = null;
            gadgetWindow.CloseFromOwner();
        }

        if (_plotWindow != null)
        {
            PlotWindow plotWindow = _plotWindow;
            _plotWindow = null;
            plotWindow.CloseFromOwner();
        }
    }

    private void HideShowMainWindow()
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        if (_isMainWindowHidden || !IsWindowVisible(hwnd) || IsIconic(hwnd))
            RestoreMainWindow();
        else
            HideMainWindowToTray();
    }

    private void MinimizeOrHideMainWindow()
    {
        if (ViewModel.MinimizeToTray)
            HideMainWindowToTray();
        else
        {
            _isMainWindowHidden = false;
            ShowWindow(WindowNative.GetWindowHandle(this), ShowWindowMinimize);
        }
    }

    private void HideMainWindowToTray()
    {
        _isMainWindowHidden = true;
        ShowWindow(WindowNative.GetWindowHandle(this), ShowWindowHide);
    }

    private void RestoreMainWindow()
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        _isMainWindowHidden = false;
        ShowWindow(hwnd, ShowWindowShow);
        // Only un-minimize. The window is hidden to the tray with SW_HIDE while keeping its maximized state, so
        // SW_SHOW alone brings it back as it was; an unconditional SW_RESTORE would also un-maximize it.
        if (IsIconic(hwnd))
            ShowWindow(hwnd, ShowWindowRestore);
        Activate();
    }

    private void SyncTraySensors()
    {
        _trayIconService.RestoreSelectedSensors(ViewModel.GetTraySensorItems());
    }

    private void SyncGadgetSensors()
    {
        _gadgetWindow?.UpdateSensors(ViewModel.GetGadgetSensorItems());
    }

    private void UpdateGadgetVisibility()
    {
        if (ViewModel.ShowGadget)
        {
            if (_gadgetWindow == null)
            {
                _gadgetWindow = new SensorGadgetWindow(ViewModel);
                _gadgetWindow.HideShowMainWindowRequested += (_, _) => HideShowMainWindow();
                _gadgetWindow.UserClosed += (_, _) =>
                {
                    _gadgetWindow = null;
                    ViewModel.ShowGadget = false;
                };
                SyncGadgetSensors();
                _gadgetWindow.Activate();
            }

            return;
        }

        if (_gadgetWindow != null)
        {
            SensorGadgetWindow gadgetWindow = _gadgetWindow;
            _gadgetWindow = null;
            gadgetWindow.CloseFromOwner();
        }
    }

    private void UpdatePlotWindowVisibility()
    {
        if (ViewModel.IsPlotWindowVisible)
        {
            if (_plotWindow == null)
            {
                _plotWindow = new PlotWindow(ViewModel.Settings, ViewModel);
                _plotWindow.ApplyTheme(ViewModel.ThemeMode);
                _plotWindow.UserClosed += (_, _) =>
                {
                    _plotWindow = null;
                    ViewModel.ShowPlot = false;
                };
                _plotWindow.Activate();
            }

            return;
        }

        if (_plotWindow != null)
        {
            PlotWindow plotWindow = _plotWindow;
            _plotWindow = null;
            plotWindow.CloseFromOwner();
        }
    }

    private void RebuildSensorTree()
    {
        MeasureStartup("MainWindow.RebuildSensorTree", RebuildSensorTreeCore, GetRootSizeDetail);
        ScheduleDeviceColumnWidthSettle();
        SyncTraySensors();
        SyncGadgetSensors();
        if (_startupCompletionRequested)
            CompleteStartupTraceIfReady();
    }

    private void RebuildSensorTreeCore()
    {
        if (_sensorTree == null)
            return;

        _sensorRowGrids.Clear();
        _sensorTree.RootNodes.Clear();
        foreach (SensorTreeItemViewModel item in ViewModel.RootItems)
        {
            TreeViewNode? node = CreateTreeNode(item);
            if (node != null)
                _sensorTree.RootNodes.Add(node);
        }

        UpdateSensorColumnWidths();
    }

    private void CompleteStartupTraceIfReady()
    {
        if (_startupCompleteRecorded || _startupTrace is not { IsComplete: false } || _sensorRowGrids.Count == 0)
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

    private void DeviceColumnWidthSettleTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _deviceColumnWidthSettleTimer.Stop();
        _deviceColumnWidthSettled = true;
        UpdateSensorColumnWidths();
    }

    private void ScheduleDeviceColumnWidthSettle()
    {
        if (_sensorRowGrids.Count == 0)
            return;

        _deviceColumnWidthSettled = false;
        _deviceColumnWidthSettleTimer.Stop();
        _deviceColumnWidthSettleTimer.Start();
    }

    private static DataTemplate CreateTreeViewNodeContentTemplate()
    {
        const string xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ContentPresenter Content="{Binding Content}" />
            </DataTemplate>
            """;

        return (DataTemplate)XamlReader.Load(xaml);
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
        Grid row = CreateSensorRowGrid();
        _sensorRowGrids.Add(row);
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
        _plotView.ResetZoom();
        _plotWindow?.RedrawPlot();
    }

    private void DrawPlot()
    {
        if (ViewModel.PlotLocation == PlotLocation.Window)
            _plotView.Clear();
        else
            _plotView.Redraw();

        _plotWindow?.RedrawPlot();
    }

    private void UpdatePlotLayout()
    {
        _contentGrid.ColumnDefinitions[1].Width = ViewModel.ShowPlot && ViewModel.PlotLocation == PlotLocation.Right ? new GridLength(320) : new GridLength(0);
        _contentGrid.RowDefinitions[1].Height = ViewModel.ShowPlot && ViewModel.PlotLocation == PlotLocation.Bottom ? new GridLength(220) : new GridLength(0);

        _plotPane.Visibility = ViewModel.PlotVisibility;
        Grid.SetRow(_plotPane, ViewModel.PlotGridRow);
        Grid.SetColumn(_plotPane, ViewModel.PlotGridColumn);
        Grid.SetRowSpan(_plotPane, ViewModel.PlotGridRowSpan);
        Grid.SetColumnSpan(_plotPane, ViewModel.PlotGridColumnSpan);
        Grid.SetRowSpan(_sensorPane, ViewModel.SensorGridRowSpan);
        Grid.SetColumnSpan(_sensorPane, ViewModel.SensorGridColumnSpan);
        UpdatePlotWindowVisibility();
        DrawPlot();
    }

    private void ApplyTheme()
    {
        _rootGrid.RequestedTheme = ViewModel.ThemeMode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark or AppThemeMode.Black => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        _rootGrid.Background = ViewModel.ThemeMode switch
        {
            AppThemeMode.Black => new SolidColorBrush(Colors.Black),
            AppThemeMode.Dark => new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 30, 30, 30)),
            _ => (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
        };

        _plotView.ApplyTheme(ViewModel.ThemeMode);
        _plotWindow?.ApplyTheme(ViewModel.ThemeMode);
    }

    private void RestoreWindowBounds()
    {
        // AppWindow.Resize takes physical pixels and the app is PerMonitorV2 DPI-aware, so the logical default/minimum
        // sizes must be scaled by the window's DPI. Without this, on a high-DPI display (e.g. a 200% laptop panel) the
        // window — and any SW_RESTORE from the tray, which un-maximizes to this size — came out at half size.
        double scale = GetWindowScale();
        int minWidth = (int)Math.Round(470 * scale);
        int minHeight = (int)Math.Round(640 * scale);
        int width = Math.Max(minWidth, ViewModel.Settings.GetValue("mainForm.Width", (int)Math.Round(760 * scale)));
        int height = Math.Max(minHeight, ViewModel.Settings.GetValue("mainForm.Height", (int)Math.Round(680 * scale)));
        _appWindow.Resize(new SizeInt32(width, height));

        int x = ViewModel.Settings.GetValue("mainForm.Location.X", int.MinValue);
        int y = ViewModel.Settings.GetValue("mainForm.Location.Y", int.MinValue);
        if (x != int.MinValue && y != int.MinValue)
            _appWindow.Move(new PointInt32(x, y));
    }

    private double GetWindowScale()
    {
        uint dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private void MaximizeWindow()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();
    }

    private void SaveWindowBounds()
    {
        ViewModel.Settings.SetValue("mainForm.Location.X", _appWindow.Position.X);
        ViewModel.Settings.SetValue("mainForm.Location.Y", _appWindow.Position.Y);
        ViewModel.Settings.SetValue("mainForm.Width", _appWindow.Size.Width);
        ViewModel.Settings.SetValue("mainForm.Height", _appWindow.Size.Height);
        ViewModel.Save();
    }

    private void UpdateSensorColumnWidths()
    {
        MeasureStartup("MainWindow.UpdateSensorColumnWidths", UpdateSensorColumnWidthsCore, () => FormattableString.Invariant($"rows={_sensorRowGrids.Count}, cacheEntries={_textMeasurementCache.Count}, deviceWidth={_sensorColumnWidths[0]:F0}, settled={_deviceColumnWidthSettled}"));
    }

    private void UpdateSensorColumnWidthsCore()
    {
        double sensorWidth = MeasureText("Sensor", true) + SensorColumnPadding;
        double valueWidth = ViewModel.ShowValueColumn ? MeasureText("Value", true) + ValueColumnPadding : 0;
        double minWidth = ViewModel.ShowMinColumn ? MeasureText("Min", true) + ValueColumnPadding : 0;
        double maxWidth = ViewModel.ShowMaxColumn ? MeasureText("Max", true) + ValueColumnPadding : 0;

        foreach (SensorTreeItemViewModel root in ViewModel.RootItems)
            MeasureSensorColumnWidths(root, 0, ref sensorWidth, ref valueWidth, ref minWidth, ref maxWidth);

        double deviceColumnWidth = NormalizeDeviceColumnWidth(sensorWidth);
        if (!_deviceColumnWidthSettled)
            deviceColumnWidth = Math.Max(deviceColumnWidth, _stableDeviceColumnWidth);

        _sensorColumnWidths[0] = deviceColumnWidth;
        _sensorColumnWidths[1] = Math.Ceiling(valueWidth);
        _sensorColumnWidths[2] = Math.Ceiling(minWidth);
        _sensorColumnWidths[3] = Math.Ceiling(maxWidth);

        if (_sensorHeader != null)
            ApplySensorColumnWidths(_sensorHeader);
        foreach (Grid row in _sensorRowGrids)
            ApplySensorColumnWidths(row);

        RecordDeviceColumnWidthIfChanged();
    }

    private void MeasureSensorColumnWidths(
        SensorTreeItemViewModel item,
        int depth,
        ref double sensorWidth,
        ref double valueWidth,
        ref double minWidth,
        ref double maxWidth)
    {
        if (item.RowVisibility == Visibility.Visible)
        {
            sensorWidth = Math.Max(sensorWidth, MeasureText(item.Text) + SensorColumnPadding + depth * SensorTreeIndentWidth);
            if (ViewModel.ShowValueColumn)
                valueWidth = Math.Max(valueWidth, MeasureText(item.Value) + ValueColumnPadding);
            if (ViewModel.ShowMinColumn)
                minWidth = Math.Max(minWidth, MeasureText(item.Min) + ValueColumnPadding);
            if (ViewModel.ShowMaxColumn)
                maxWidth = Math.Max(maxWidth, MeasureText(item.Max) + ValueColumnPadding);
        }

        foreach (SensorTreeItemViewModel child in item.Children)
            MeasureSensorColumnWidths(child, depth + 1, ref sensorWidth, ref valueWidth, ref minWidth, ref maxWidth);
    }

    private double MeasureText(string text, bool bold = false)
    {
        (string Text, bool Bold) key = (text, bold);
        if (_textMeasurementCache.TryGetValue(key, out double width))
            return width;

        if (_textMeasurementCache.Count >= MaxTextMeasurementCacheEntries)
            _textMeasurementCache.Clear();

        // Reuse a single TextBlock for measurement instead of allocating one per cache miss. This runs for every
        // sensor's Value/Min/Max on each update tick, and the frequently-changing value strings miss the cache, so the
        // old code created and discarded a WinUI element (with a native peer) on nearly every call — avoidable churn.
        _measurementTextBlock ??= new TextBlock();
        _measurementTextBlock.Text = text;
        _measurementTextBlock.FontWeight = new global::Windows.UI.Text.FontWeight { Weight = bold ? (ushort)600 : (ushort)400 };
        _measurementTextBlock.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        width = _measurementTextBlock.DesiredSize.Width;
        _textMeasurementCache[key] = width;
        return width;
    }

    private static double NormalizeDeviceColumnWidth(double width)
    {
        if (!double.IsFinite(width))
            width = DefaultSensorColumnWidth;

        return Math.Ceiling(Math.Clamp(width, MinimumSensorColumnWidth, MaximumSensorColumnWidth));
    }

    private void RecordDeviceColumnWidthIfChanged()
    {
        if (!_deviceColumnWidthSettled || _sensorRowGrids.Count == 0)
            return;

        double measuredWidth = NormalizeDeviceColumnWidth(_sensorColumnWidths[0]);
        _stableDeviceColumnWidth = measuredWidth;
        double savedWidth = NormalizeDeviceColumnWidth(ViewModel.Settings.GetValue(DeviceColumnWidthSetting, DefaultSensorColumnWidth));
        if (Math.Abs(measuredWidth - savedWidth) < 0.5)
            return;

        ViewModel.Settings.SetValue(DeviceColumnWidthSetting, measuredWidth);
        _startupTrace?.Mark("MainWindow.RecordDeviceColumnWidth", FormattableString.Invariant($"width={measuredWidth:F0}, rows={_sensorRowGrids.Count}"));
    }

    private void ApplySensorColumnWidths(Grid grid)
    {
        for (int i = 0; i < grid.ColumnDefinitions.Count && i < _sensorColumnWidths.Length; i++)
            grid.ColumnDefinitions[i].Width = new GridLength(_sensorColumnWidths[i]);
    }

    private Grid CreateSensorRowGrid()
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

        ApplySensorColumnWidths(grid);
        return grid;
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

    private static MenuFlyoutSubItem BuildIndexedSubMenu(string text, (string Label, int Value)[] items, Func<int> getter, Action<int> setter)
    {
        return BuildRadioSubMenu(text, items, getter, setter);
    }

    private static MenuFlyoutSubItem BuildRadioSubMenu<T>(string text, (string Label, T Value)[] items, Func<T> getter, Action<T> setter)
    {
        MenuFlyoutSubItem subItem = new() { Text = text };
        foreach ((string label, T value) in items)
        {
            ToggleMenuFlyoutItem item = new()
            {
                Text = label,
                IsChecked = Equals(getter(), value)
            };
            item.Click += (_, _) =>
            {
                setter(value);
                foreach (ToggleMenuFlyoutItem sibling in subItem.Items.OfType<ToggleMenuFlyoutItem>())
                    sibling.IsChecked = Equals(sibling.Tag, value);
            };
            item.Tag = value;
            subItem.Items.Add(item);
        }

        return subItem;
    }

    private static MenuFlyoutItem CreateMenuItem(string text, RoutedEventHandler handler)
    {
        MenuFlyoutItem item = new() { Text = text };
        item.Click += handler;
        return item;
    }

    private ToggleMenuFlyoutItem CreateToggleItem(string text, string bindingPath)
    {
        ToggleMenuFlyoutItem item = new() { Text = text };
        Bind(item, ToggleMenuFlyoutItem.IsCheckedProperty, ViewModel, bindingPath, BindingMode.TwoWay);
        return item;
    }

    private static ToggleMenuFlyoutItem CreateToggleSettingItem(string text, Func<bool> getter, Action<bool> setter)
    {
        ToggleMenuFlyoutItem item = new()
        {
            Text = text,
            IsChecked = getter()
        };
        item.Click += (_, _) => setter(item.IsChecked);
        return item;
    }

    private const int ShowWindowHide = 0;
    private const int ShowWindowShow = 5;
    private const int ShowWindowMinimize = 6;
    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

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

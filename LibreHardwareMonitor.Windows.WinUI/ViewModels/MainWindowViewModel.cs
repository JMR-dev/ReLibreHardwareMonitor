// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan[] LoggingIntervals =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6)
    ];

    private static readonly TimeSpan[] UpdateIntervals =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    private static readonly TimeSpan[] SensorValuesTimeWindows =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24)
    ];

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

    private readonly HardwareMonitorService _hardwareMonitor;
    private readonly Logger _logger;
    private readonly Dictionary<string, PlotSeriesViewModel> _plotSeriesByIdentifier = new();
    private readonly RemoteWebServer _remoteWebServer;
    private readonly StartupService _startupService = new();
    private AppThemeMode _themeMode;
    private int _loggingIntervalIndex;
    private PlotLocation _plotLocation;
    private double _plotStrokeThickness;
    private SensorTreeItemViewModel? _selectedItem;
    private int _sensorValuesTimeWindowIndex;
    private bool _showHiddenSensors;
    private bool _showMaxColumn;
    private bool _showMinColumn;
    private bool _showPlot;
    private bool _showValueColumn;
    private bool _throttleAtaUpdate;
    private bool _isStarted;
    private bool _isStarting;
    private string _statusText = "";
    private TemperatureUnit _temperatureUnit;
    private int _updateIntervalIndex;

    public MainWindowViewModel(AppSettings settings)
    {
        Settings = settings;
        _hardwareMonitor = new HardwareMonitorService(settings);
        _logger = new Logger(_hardwareMonitor.Computer);
        _remoteWebServer = new RemoteWebServer(
            () => RootItems.FirstOrDefault(),
            _hardwareMonitor.Computer,
            settings.GetValue("listenerIp", "?"),
            settings.GetValue("listenerPort", 8085),
            settings.GetValue("authenticationEnabled", false),
            settings.GetValue("authenticationUserName", ""),
            settings.GetValue("authenticationPassword", ""));
        _hardwareMonitor.TreeRebuilt += HardwareMonitor_TreeRebuilt;

        _themeMode = ParseThemeMode(settings.GetValue("theme", "auto"));
        _showHiddenSensors = settings.GetValue("hiddenMenuItem", false);
        _showValueColumn = settings.GetValue("valueMenuItem", true);
        _showMinColumn = settings.GetValue("minMenuItem", false);
        _showMaxColumn = settings.GetValue("maxMenuItem", true);
        _showPlot = settings.GetValue("plotMenuItem", false);
        _plotLocation = (PlotLocation)Math.Clamp(settings.GetValue("plotLocation", 0), 0, 1);
        _plotStrokeThickness = Math.Clamp(settings.GetValue("plotStroke", 1) + 1, 1, 4);
        _temperatureUnit = (TemperatureUnit)Math.Clamp(settings.GetValue("TemperatureUnit", 0), 0, 1);
        _updateIntervalIndex = Math.Clamp(settings.GetValue("updateIntervalMenuItem", 2), 0, UpdateIntervals.Length - 1);
        _loggingIntervalIndex = Math.Clamp(settings.GetValue("loggingInterval", 0), 0, LoggingIntervals.Length - 1);
        _sensorValuesTimeWindowIndex = Math.Clamp(settings.GetValue("sensorValuesTimeWindow", 10), 0, SensorValuesTimeWindows.Length - 1);
        _throttleAtaUpdate = settings.GetValue("throttleAtaUpdateMenuItem", false);
        StorageDevice.ThrottleInterval = _throttleAtaUpdate ? TimeSpan.FromSeconds(30) : TimeSpan.Zero;
        _logger.LoggingInterval = LoggingIntervals[_loggingIntervalIndex];
        _logger.FileRotationMethod = (LoggerFileRotation)Math.Clamp(settings.GetValue("logger.fileRotation", 0), 0, 1);
    }

    public event EventHandler? PlotInvalidated;

    public Visibility MaxColumnVisibility => ShowMaxColumn ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MinColumnVisibility => ShowMinColumn ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<PlotSeriesViewModel> PlotSeries { get; } = [];

    public Visibility PlotVisibility => ShowPlot ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<SensorTreeItemViewModel> RootItems { get; } = [];

    public SensorTreeItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public AppSettings Settings { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public AppThemeMode ThemeMode
    {
        get => _themeMode;
        set
        {
            if (!SetProperty(ref _themeMode, value))
                return;

            Settings.SetValue("theme", FormatThemeMode(value));
        }
    }

    public TimeSpan UpdateInterval => UpdateIntervals[UpdateIntervalIndex];

    public Visibility ValueColumnVisibility => ShowValueColumn ? Visibility.Visible : Visibility.Collapsed;

    public bool AuthWebServer
    {
        get => _remoteWebServer.AuthEnabled;
        set
        {
            if (_remoteWebServer.AuthEnabled == value)
                return;

            _remoteWebServer.AuthEnabled = value;
            Settings.SetValue("authenticationEnabled", value);
            RestartWebServerIfRunning();
            OnPropertyChanged();
        }
    }

    public string AuthWebServerUserName
    {
        get => _remoteWebServer.UserName;
        set
        {
            if (_remoteWebServer.UserName == value)
                return;

            _remoteWebServer.UserName = value;
            Settings.SetValue("authenticationUserName", value);
            RestartWebServerIfRunning();
            OnPropertyChanged();
        }
    }

    public LoggerFileRotation FileRotationMethod
    {
        get => _logger.FileRotationMethod;
        set
        {
            if (_logger.FileRotationMethod == value)
                return;

            _logger.FileRotationMethod = value;
            Settings.SetValue("logger.fileRotation", (int)value);
            OnPropertyChanged();
        }
    }

    public bool ForceDriveWakeup
    {
        get => _hardwareMonitor.ForceDriveWakeup;
        set
        {
            if (_hardwareMonitor.ForceDriveWakeup == value)
                return;

            _hardwareMonitor.ForceDriveWakeup = value;
            OnPropertyChanged();
        }
    }

    public bool IsBatteryEnabled
    {
        get => _hardwareMonitor.IsBatteryEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsBatteryEnabled, v => _hardwareMonitor.IsBatteryEnabled = v);
    }

    public bool IsControllerEnabled
    {
        get => _hardwareMonitor.IsControllerEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsControllerEnabled, v => _hardwareMonitor.IsControllerEnabled = v);
    }

    public bool IsCpuEnabled
    {
        get => _hardwareMonitor.IsCpuEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsCpuEnabled, v => _hardwareMonitor.IsCpuEnabled = v);
    }

    public bool IsGpuEnabled
    {
        get => _hardwareMonitor.IsGpuEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsGpuEnabled, v => _hardwareMonitor.IsGpuEnabled = v);
    }

    public bool IsMemoryEnabled
    {
        get => _hardwareMonitor.IsMemoryEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsMemoryEnabled, v => _hardwareMonitor.IsMemoryEnabled = v);
    }

    public bool IsMotherboardEnabled
    {
        get => _hardwareMonitor.IsMotherboardEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsMotherboardEnabled, v => _hardwareMonitor.IsMotherboardEnabled = v);
    }

    public bool IsNetworkEnabled
    {
        get => _hardwareMonitor.IsNetworkEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsNetworkEnabled, v => _hardwareMonitor.IsNetworkEnabled = v);
    }

    public bool IsPowerMonitorEnabled
    {
        get => _hardwareMonitor.IsPowerMonitorEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsPowerMonitorEnabled, v => _hardwareMonitor.IsPowerMonitorEnabled = v);
    }

    public bool IsPsuEnabled
    {
        get => _hardwareMonitor.IsPsuEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsPsuEnabled, v => _hardwareMonitor.IsPsuEnabled = v);
    }

    public bool IsStorageEnabled
    {
        get => _hardwareMonitor.IsStorageEnabled;
        set => SetHardwareFlag(value, _hardwareMonitor.IsStorageEnabled, v => _hardwareMonitor.IsStorageEnabled = v);
    }

    public int LoggingIntervalIndex
    {
        get => _loggingIntervalIndex;
        set
        {
            value = Math.Clamp(value, 0, LoggingIntervals.Length - 1);
            if (!SetProperty(ref _loggingIntervalIndex, value))
                return;

            Settings.SetValue("loggingInterval", value);
            _logger.LoggingInterval = LoggingIntervals[value];
        }
    }

    public string ListenerIp
    {
        get => _remoteWebServer.ListenerIp;
        set
        {
            if (_remoteWebServer.ListenerIp == value)
                return;

            _remoteWebServer.ListenerIp = value;
            Settings.SetValue("listenerIp", value);
            RestartWebServerIfRunning();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WebServerUrl));
        }
    }

    public int ListenerPort
    {
        get => _remoteWebServer.ListenerPort;
        set
        {
            value = Math.Clamp(value, 1, 65535);
            if (_remoteWebServer.ListenerPort == value)
                return;

            _remoteWebServer.ListenerPort = value;
            Settings.SetValue("listenerPort", value);
            RestartWebServerIfRunning();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WebServerUrl));
        }
    }

    public bool LogSensors
    {
        get => Settings.GetValue("logSensorsMenuItem", false);
        set
        {
            if (LogSensors == value)
                return;

            Settings.SetValue("logSensorsMenuItem", value);
            OnPropertyChanged();
        }
    }

    public bool RunWebServer
    {
        get => !IsWebServerUnavailable && Settings.GetValue("runWebServerMenuItem", false);
        set
        {
            if (IsWebServerUnavailable || RunWebServer == value)
                return;

            if (value)
            {
                if (!_remoteWebServer.Start())
                {
                    Settings.SetValue("runWebServerMenuItem", false);
                    StatusText = "Remote web server failed to start.";
                    OnPropertyChanged();
                    return;
                }
            }
            else
            {
                _remoteWebServer.Stop();
            }

            Settings.SetValue("runWebServerMenuItem", value);
            OnPropertyChanged();
            UpdateStatus();
        }
    }

    public int SensorValuesTimeWindowIndex
    {
        get => _sensorValuesTimeWindowIndex;
        set
        {
            value = Math.Clamp(value, 0, SensorValuesTimeWindows.Length - 1);
            if (!SetProperty(ref _sensorValuesTimeWindowIndex, value))
                return;

            Settings.SetValue("sensorValuesTimeWindow", value);
            ApplySensorValuesTimeWindow();
        }
    }

    public bool MinimizeOnClose
    {
        get => Settings.GetValue("minCloseMenuItem", false);
        set => SetBooleanSetting("minCloseMenuItem", value);
    }

    public bool MinimizeToTray
    {
        get => Settings.GetValue("minTrayMenuItem", true);
        set => SetBooleanSetting("minTrayMenuItem", value);
    }

    public PlotLocation PlotLocation
    {
        get => _plotLocation;
        set
        {
            if (!SetProperty(ref _plotLocation, value))
                return;

            Settings.SetValue("plotLocation", (int)value);
            NotifyPlotLayoutChanged();
        }
    }

    public int PlotGridColumn => PlotLocation == PlotLocation.Right ? 1 : 0;

    public int PlotGridColumnSpan => PlotLocation == PlotLocation.Bottom ? 2 : 1;

    public int PlotGridRow => PlotLocation == PlotLocation.Bottom ? 1 : 0;

    public int PlotGridRowSpan => PlotLocation == PlotLocation.Right ? 2 : 1;

    public double PlotStrokeThickness
    {
        get => _plotStrokeThickness;
        set
        {
            value = Math.Clamp(value, 1, 4);
            if (!SetProperty(ref _plotStrokeThickness, value))
                return;

            Settings.SetValue("plotStroke", (int)value - 1);
            PlotInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowHiddenSensors
    {
        get => _showHiddenSensors;
        set
        {
            if (!SetProperty(ref _showHiddenSensors, value))
                return;

            Settings.SetValue("hiddenMenuItem", value);
            RootItems.FirstOrDefault()?.SetShowHiddenSensors(value);
        }
    }

    public bool ShowMaxColumn
    {
        get => _showMaxColumn;
        set
        {
            if (!SetProperty(ref _showMaxColumn, value))
                return;

            Settings.SetValue("maxMenuItem", value);
            NotifyColumnVisibilityChanged();
        }
    }

    public bool ShowMinColumn
    {
        get => _showMinColumn;
        set
        {
            if (!SetProperty(ref _showMinColumn, value))
                return;

            Settings.SetValue("minMenuItem", value);
            NotifyColumnVisibilityChanged();
        }
    }

    public bool ShowPlot
    {
        get => _showPlot;
        set
        {
            if (!SetProperty(ref _showPlot, value))
                return;

            Settings.SetValue("plotMenuItem", value);
            OnPropertyChanged(nameof(PlotVisibility));
            NotifyPlotLayoutChanged();
            PlotInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowValueColumn
    {
        get => _showValueColumn;
        set
        {
            if (!SetProperty(ref _showValueColumn, value))
                return;

            Settings.SetValue("valueMenuItem", value);
            NotifyColumnVisibilityChanged();
        }
    }

    public bool StartMinimized
    {
        get => Settings.GetValue("startMinMenuItem", false);
        set => SetBooleanSetting("startMinMenuItem", value);
    }

    public bool ThrottleAtaUpdate
    {
        get => _throttleAtaUpdate;
        set
        {
            if (!SetProperty(ref _throttleAtaUpdate, value))
                return;

            Settings.SetValue("throttleAtaUpdateMenuItem", value);
            StorageDevice.ThrottleInterval = value ? TimeSpan.FromSeconds(30) : TimeSpan.Zero;
        }
    }

    public bool IsWebServerUnavailable => _remoteWebServer.PlatformNotSupported;

    public string WebServerUrl
    {
        get
        {
            string host = ListenerIp is "?" or "+" or "*" or "0.0.0.0" ? "localhost" : ListenerIp;
            return $"http://{host}:{ListenerPort}/";
        }
    }

    public bool RunOnStartup
    {
        get => _startupService.IsAvailable && _startupService.Startup;
        set
        {
            if (!_startupService.IsAvailable || RunOnStartup == value)
                return;

            try
            {
                _startupService.Startup = value;
                OnPropertyChanged();
            }
            catch (InvalidOperationException)
            {
                OnPropertyChanged();
            }
        }
    }

    public int SensorGridColumnSpan => ShowPlot && PlotLocation == PlotLocation.Right ? 1 : 2;

    public int SensorGridRowSpan => ShowPlot && PlotLocation == PlotLocation.Bottom ? 1 : 2;

    public TemperatureUnit TemperatureUnit
    {
        get => _temperatureUnit;
        set
        {
            if (!SetProperty(ref _temperatureUnit, value))
                return;

            Settings.SetValue("TemperatureUnit", (int)value);
            RootItems.FirstOrDefault()?.SetTemperatureUnit(value);
            PlotInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public int UpdateIntervalIndex
    {
        get => _updateIntervalIndex;
        set
        {
            value = Math.Clamp(value, 0, UpdateIntervals.Length - 1);
            if (!SetProperty(ref _updateIntervalIndex, value))
                return;

            Settings.SetValue("updateIntervalMenuItem", value);
            OnPropertyChanged(nameof(UpdateInterval));
        }
    }

    public void Dispose()
    {
        _hardwareMonitor.TreeRebuilt -= HardwareMonitor_TreeRebuilt;
        Settings.SetValue("listenerIp", ListenerIp);
        Settings.SetValue("listenerPort", ListenerPort);
        Settings.SetValue("authenticationEnabled", AuthWebServer);
        Settings.SetValue("authenticationUserName", AuthWebServerUserName);
        Settings.SetValue("authenticationPassword", _remoteWebServer.PasswordSHA256);
        _remoteWebServer.Quit();
        _hardwareMonitor.Dispose();
        Save();
    }

    public string GetReport()
    {
        return _hardwareMonitor.Computer.GetReport();
    }

    public void ResetHardware()
    {
        _hardwareMonitor.Reset();
        UpdateStatus();
    }

    public void ResetMinMax()
    {
        _hardwareMonitor.ResetMinMax();
        RootItems.FirstOrDefault()?.RefreshValues();
    }

    public void ResetPlot()
    {
        _hardwareMonitor.ClearSensorValues();
        _plotSeriesByIdentifier.Clear();
        PlotSeries.Clear();
        PlotInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        Settings.Save();
    }

    public void SetStatusText(string statusText)
    {
        StatusText = statusText;
    }

    public void SetAllExpanded(bool isExpanded)
    {
        RootItems.FirstOrDefault()?.SetAllExpanded(isExpanded);
    }

    public void SetAuthPassword(string plainPassword)
    {
        if (string.IsNullOrWhiteSpace(plainPassword))
            return;

        _remoteWebServer.SetPassword(plainPassword);
        Settings.SetValue("authenticationPassword", _remoteWebServer.PasswordSHA256);
        RestartWebServerIfRunning();
    }

    public void SetSensorPenColor(SensorTreeItemViewModel item, Color? color)
    {
        if (item.Sensor == null)
            return;

        item.PenColor = color;
        string identifier = item.Sensor.Identifier.ToString();
        if (_plotSeriesByIdentifier.TryGetValue(identifier, out PlotSeriesViewModel? series))
            series.Color = GetPlotColor(item);

        PlotInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public async Task StartAsync()
    {
        if (_isStarted || _isStarting)
            return;

        _isStarting = true;
        try
        {
            StatusText = "Initializing hardware sensors...";
            await Task.Run(() => _hardwareMonitor.Open(raiseTreeRebuilt: false));
            UpdateRoot();
            ApplySensorValuesTimeWindow();
            StartWebServerFromSettings();
            UpdateStatus();
            _isStarted = true;
        }
        finally
        {
            _isStarting = false;
        }
    }

    public async Task UpdateAsync()
    {
        if (!_isStarted)
            return;

        await _hardwareMonitor.UpdateAsync();

        SensorTreeItemViewModel? root = RootItems.FirstOrDefault();
        root?.RefreshValues();
        TrackPlotPoints();

        if (LogSensors)
            _logger.Log();

        UpdateStatus();
    }

    private void HardwareMonitor_TreeRebuilt(object? sender, EventArgs e)
    {
        UpdateRoot();
    }

    private void ApplySensorValuesTimeWindow()
    {
        TimeSpan timeWindow = SensorValuesTimeWindows[SensorValuesTimeWindowIndex];
        SensorVisitor visitor = new(sensor => sensor.ValuesTimeWindow = timeWindow);
        visitor.VisitComputer(_hardwareMonitor.Computer);
    }

    private void NotifyColumnVisibilityChanged()
    {
        OnPropertyChanged(nameof(ValueColumnVisibility));
        OnPropertyChanged(nameof(MinColumnVisibility));
        OnPropertyChanged(nameof(MaxColumnVisibility));
        RootItems.FirstOrDefault()?.SetColumnVisibility(ShowValueColumn, ShowMinColumn, ShowMaxColumn);
    }

    private void NotifyPlotLayoutChanged()
    {
        OnPropertyChanged(nameof(PlotGridRow));
        OnPropertyChanged(nameof(PlotGridColumn));
        OnPropertyChanged(nameof(PlotGridRowSpan));
        OnPropertyChanged(nameof(PlotGridColumnSpan));
        OnPropertyChanged(nameof(SensorGridRowSpan));
        OnPropertyChanged(nameof(SensorGridColumnSpan));
    }

    private void SetBooleanSetting(string settingName, bool value)
    {
        if (Settings.GetValue(settingName, false) == value)
            return;

        Settings.SetValue(settingName, value);
        OnPropertyChanged();
    }

    private void SetHardwareFlag(bool value, bool currentValue, Action<bool> setValue)
    {
        if (currentValue == value)
            return;

        setValue(value);
        UpdateRoot();
        OnPropertyChanged();
    }

    private void TrackPlotPoints()
    {
        SensorTreeItemViewModel? root = RootItems.FirstOrDefault();
        if (root == null)
            return;

        DateTime now = DateTime.Now;
        HashSet<string> selectedIdentifiers = new();
        foreach (SensorTreeItemViewModel sensorItem in root.EnumerateSensors().Where(sensorItem => sensorItem.Plot && sensorItem.Sensor != null))
        {
            ISensor sensor = sensorItem.Sensor!;
            string identifier = sensor.Identifier.ToString();
            selectedIdentifiers.Add(identifier);
            double? value = SensorFormatter.GetPlotValue(sensor, TemperatureUnit);
            if (!value.HasValue)
                continue;

            if (!_plotSeriesByIdentifier.TryGetValue(identifier, out PlotSeriesViewModel? series))
            {
                series = new PlotSeriesViewModel(identifier, sensor.Name, GetPlotColor(sensorItem));
                _plotSeriesByIdentifier[identifier] = series;
                PlotSeries.Add(series);
            }
            else
            {
                series.Color = GetPlotColor(sensorItem);
            }

            series.Points.Add(new PlotPointViewModel(now, value.Value));
            while (series.Points.Count > 0 && now - series.Points[0].Timestamp > TimeSpan.FromHours(24))
                series.Points.RemoveAt(0);
        }

        foreach (string identifier in _plotSeriesByIdentifier.Keys.Where(identifier => !selectedIdentifiers.Contains(identifier)).ToArray())
        {
            PlotSeriesViewModel series = _plotSeriesByIdentifier[identifier];
            _plotSeriesByIdentifier.Remove(identifier);
            PlotSeries.Remove(series);
        }
    }

    private void UpdateRoot()
    {
        RootItems.Clear();
        SensorTreeItemViewModel root = _hardwareMonitor.Root;
        root.Configure(TemperatureUnit, ShowHiddenSensors, ShowValueColumn, ShowMinColumn, ShowMaxColumn);
        RootItems.Add(root);
        ApplySensorValuesTimeWindow();
    }

    private void UpdateStatus()
    {
        int hardwareCount = _hardwareMonitor.Computer.Hardware.Count;
        int sensorCount = RootItems.FirstOrDefault()?.EnumerateSensors().Count() ?? 0;
        string webServerStatus = RunWebServer ? $", web server {WebServerUrl}" : "";
        StatusText = $"{hardwareCount} hardware devices, {sensorCount} sensors, update every {FormatInterval(UpdateInterval)}{webServerStatus}";
    }

    private Color GetPlotColor(SensorTreeItemViewModel sensorItem)
    {
        return sensorItem.PenColor ?? PlotColors[_plotSeriesByIdentifier.Count % PlotColors.Length];
    }

    private void RestartWebServerIfRunning()
    {
        if (!RunWebServer)
            return;

        _remoteWebServer.Stop();
        if (!_remoteWebServer.Start())
        {
            Settings.SetValue("runWebServerMenuItem", false);
            StatusText = "Remote web server failed to restart.";
        }

        OnPropertyChanged(nameof(RunWebServer));
        UpdateStatus();
    }

    private void StartWebServerFromSettings()
    {
        if (IsWebServerUnavailable || !Settings.GetValue("runWebServerMenuItem", false))
            return;

        if (!_remoteWebServer.Start())
        {
            Settings.SetValue("runWebServerMenuItem", false);
            StatusText = "Remote web server failed to start.";
        }

        OnPropertyChanged(nameof(RunWebServer));
    }

    private static AppThemeMode ParseThemeMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "light" => AppThemeMode.Light,
            "dark" => AppThemeMode.Dark,
            "black" => AppThemeMode.Black,
            _ => AppThemeMode.Auto
        };
    }

    private static string FormatThemeMode(AppThemeMode themeMode)
    {
        return themeMode switch
        {
            AppThemeMode.Light => "light",
            AppThemeMode.Dark => "dark",
            AppThemeMode.Black => "black",
            _ => "auto"
        };
    }

    private static string FormatInterval(TimeSpan interval)
    {
        if (interval.TotalMilliseconds < 1000)
            return $"{interval.TotalMilliseconds:F0}ms";
        if (interval.TotalSeconds < 60)
            return $"{interval.TotalSeconds:F0}s";
        if (interval.TotalMinutes < 60)
            return $"{interval.TotalMinutes:F0}min";
        return $"{interval.TotalHours:F0}h";
    }
}

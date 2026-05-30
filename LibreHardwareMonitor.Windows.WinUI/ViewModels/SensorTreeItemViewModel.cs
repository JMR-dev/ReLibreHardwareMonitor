// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.Utilities;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

public sealed class SensorTreeItemViewModel : ViewModelBase
{
    private readonly AppSettings? _settings;
    private string? _expandedSettingName;
    private bool _isExpanded = true;
    private bool _isVisible = true;
    private bool _plot;
    private bool _showHiddenSensors;
    private Visibility _maxColumnVisibility = Visibility.Visible;
    private Visibility _minColumnVisibility = Visibility.Collapsed;
    private Visibility _rowVisibility = Visibility.Visible;
    private Visibility _valueColumnVisibility = Visibility.Visible;
    private Color? _penColor;
    private TemperatureUnit _temperatureUnit;

    private SensorTreeItemViewModel(SensorTreeItemKind kind, AppSettings? settings)
    {
        Kind = kind;
        _settings = settings;
    }

    public ObservableCollection<SensorTreeItemViewModel> Children { get; } = [];

    public bool CanHide => Sensor != null;

    public bool CanRename => Sensor != null || Hardware != null;

    public IHardware? Hardware { get; private init; }

    public string IconGlyph { get; private init; } = "\uE950";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && _expandedSettingName != null)
                _settings?.SetValue(_expandedSettingName, value);
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (!SetProperty(ref _isVisible, value))
                return;

            if (Sensor != null)
                _settings?.SetValue(new Identifier(Sensor.Identifier, "hidden").ToString(), !value);

            UpdateVisibilityState();
        }
    }

    public SensorTreeItemKind Kind { get; }

    public string Max => Sensor == null ? "" : SensorFormatter.FormatValue(Sensor, Sensor.Max, TemperatureUnit);

    public Visibility MaxColumnVisibility
    {
        get => _maxColumnVisibility;
        private set => SetProperty(ref _maxColumnVisibility, value);
    }

    public string Min => Sensor == null ? "" : SensorFormatter.FormatValue(Sensor, Sensor.Min, TemperatureUnit);

    public Visibility MinColumnVisibility
    {
        get => _minColumnVisibility;
        private set => SetProperty(ref _minColumnVisibility, value);
    }

    public bool Plot
    {
        get => _plot;
        set
        {
            if (Sensor == null || !SetProperty(ref _plot, value))
                return;

            _settings?.SetValue(new Identifier(Sensor.Identifier, "plot").ToString(), value);
        }
    }

    public Color? PenColor
    {
        get => _penColor;
        set
        {
            if (Sensor == null || Nullable.Equals(_penColor, value))
                return;

            _penColor = value;
            string settingName = new Identifier(Sensor.Identifier, "penColor").ToString();
            if (value.HasValue)
                _settings?.SetValue(settingName, value.Value);
            else
                _settings?.Remove(settingName);

            OnPropertyChanged();
        }
    }

    public Visibility PlotCheckVisibility => Sensor == null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RowVisibility
    {
        get => _rowVisibility;
        private set => SetProperty(ref _rowVisibility, value);
    }

    public ISensor? Sensor { get; private init; }

    public string Text
    {
        get
        {
            if (Sensor != null)
                return Sensor.Name;
            if (Hardware != null)
                return Hardware.Name;
            return _text;
        }
        set
        {
            if (Sensor != null)
                Sensor.Name = value;
            else if (Hardware != null)
                Hardware.Name = value;
            else
                _text = value;

            OnPropertyChanged();
        }
    }

    public string ToolTip
    {
        get
        {
            if (Sensor != null)
                return SensorFormatter.GetToolTip(Sensor, TemperatureUnit);

            if (Hardware?.Properties.Count > 0)
            {
                StringBuilder builder = new();
                builder.AppendLine("Hardware properties:");
                foreach (KeyValuePair<string, string> property in Hardware.Properties)
                    builder.AppendFormat(" - {0}: {1}{2}", property.Key, property.Value, Environment.NewLine);
                return builder.ToString();
            }

            return "";
        }
    }

    public TemperatureUnit TemperatureUnit
    {
        get => _temperatureUnit;
        private set
        {
            if (!SetProperty(ref _temperatureUnit, value))
                return;

            RefreshValues();
        }
    }

    public string Value => Sensor == null ? "" : SensorFormatter.FormatValue(Sensor, Sensor.Value, TemperatureUnit);

    public Visibility ValueColumnVisibility
    {
        get => _valueColumnVisibility;
        private set => SetProperty(ref _valueColumnVisibility, value);
    }

    private string _text = "";

    public static SensorTreeItemViewModel CreateRoot(string text)
    {
        return new SensorTreeItemViewModel(SensorTreeItemKind.Root, null)
        {
            _text = text,
            IconGlyph = "\uE7F4",
            _isExpanded = true
        };
    }

    public static SensorTreeItemViewModel FromHardware(IHardware hardware, AppSettings settings)
    {
        string expandedSettingName = new Identifier(hardware.Identifier, "expanded").ToString();
        SensorTreeItemViewModel item = new(SensorTreeItemKind.Hardware, settings)
        {
            Hardware = hardware,
            IconGlyph = SensorTypeDisplay.GetHardwareGlyph(hardware.HardwareType),
            _expandedSettingName = expandedSettingName,
            _isExpanded = settings.GetValue(expandedSettingName, true)
        };

        foreach (SensorType sensorType in Enum.GetValues<SensorType>())
        {
            ISensor[] sensors = hardware.Sensors
                .Where(sensor => sensor.SensorType == sensorType)
                .OrderBy(sensor => sensor.Index)
                .ToArray();

            if (sensors.Length == 0)
                continue;

            item.Children.Add(FromSensorType(hardware.Identifier, sensorType, sensors, settings));
        }

        foreach (IHardware subHardware in hardware.SubHardware.OrderBy(subHardware => subHardware.HardwareType).ThenBy(subHardware => subHardware.Name, StringComparer.OrdinalIgnoreCase))
            item.Children.Add(FromHardware(subHardware, settings));

        item.UpdateVisibilityState();
        return item;
    }

    public void Configure(TemperatureUnit temperatureUnit, bool showHiddenSensors, bool showValueColumn, bool showMinColumn, bool showMaxColumn)
    {
        SetTemperatureUnit(temperatureUnit);
        SetShowHiddenSensors(showHiddenSensors);
        SetColumnVisibility(showValueColumn, showMinColumn, showMaxColumn);
    }

    public IEnumerable<SensorTreeItemViewModel> Enumerate()
    {
        yield return this;
        foreach (SensorTreeItemViewModel child in Children)
        {
            foreach (SensorTreeItemViewModel node in child.Enumerate())
                yield return node;
        }
    }

    public IEnumerable<SensorTreeItemViewModel> EnumerateSensors()
    {
        return Enumerate().Where(item => item.Sensor != null);
    }

    public void RefreshValues()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Min));
        OnPropertyChanged(nameof(Max));
        OnPropertyChanged(nameof(ToolTip));
        foreach (SensorTreeItemViewModel child in Children)
            child.RefreshValues();
    }

    public void SetAllExpanded(bool isExpanded)
    {
        IsExpanded = isExpanded;
        foreach (SensorTreeItemViewModel child in Children)
            child.SetAllExpanded(isExpanded);
    }

    public void SetColumnVisibility(bool showValueColumn, bool showMinColumn, bool showMaxColumn)
    {
        ValueColumnVisibility = showValueColumn ? Visibility.Visible : Visibility.Collapsed;
        MinColumnVisibility = showMinColumn ? Visibility.Visible : Visibility.Collapsed;
        MaxColumnVisibility = showMaxColumn ? Visibility.Visible : Visibility.Collapsed;

        foreach (SensorTreeItemViewModel child in Children)
            child.SetColumnVisibility(showValueColumn, showMinColumn, showMaxColumn);
    }

    public void SetShowHiddenSensors(bool showHiddenSensors)
    {
        _showHiddenSensors = showHiddenSensors;
        UpdateVisibilityState();
        foreach (SensorTreeItemViewModel child in Children)
            child.SetShowHiddenSensors(showHiddenSensors);
    }

    public void SetTemperatureUnit(TemperatureUnit temperatureUnit)
    {
        TemperatureUnit = temperatureUnit;
        foreach (SensorTreeItemViewModel child in Children)
            child.SetTemperatureUnit(temperatureUnit);
    }

    private static SensorTreeItemViewModel FromSensor(ISensor sensor, AppSettings settings)
    {
        string hiddenSettingName = new Identifier(sensor.Identifier, "hidden").ToString();
        string plotSettingName = new Identifier(sensor.Identifier, "plot").ToString();
        string penColorSettingName = new Identifier(sensor.Identifier, "penColor").ToString();
        return new SensorTreeItemViewModel(SensorTreeItemKind.Sensor, settings)
        {
            Sensor = sensor,
            IconGlyph = SensorTypeDisplay.GetGlyph(sensor.SensorType),
            _isVisible = !settings.GetValue(hiddenSettingName, sensor.IsDefaultHidden),
            _plot = settings.GetValue(plotSettingName, false),
            _penColor = settings.Contains(penColorSettingName) ? settings.GetValue(penColorSettingName, Color.FromArgb(255, 0, 0, 0)) : null
        };
    }

    private static SensorTreeItemViewModel FromSensorType(Identifier parentIdentifier, SensorType sensorType, IEnumerable<ISensor> sensors, AppSettings settings)
    {
        string expandedSettingName = new Identifier(parentIdentifier, sensorType.ToString(), ".expanded").ToString();
        SensorTreeItemViewModel item = new(SensorTreeItemKind.SensorType, settings)
        {
            _text = SensorTypeDisplay.GetText(sensorType),
            IconGlyph = SensorTypeDisplay.GetGlyph(sensorType),
            _expandedSettingName = expandedSettingName,
            _isExpanded = settings.GetValue(expandedSettingName, true)
        };

        foreach (ISensor sensor in sensors)
            item.Children.Add(FromSensor(sensor, settings));

        item.UpdateVisibilityState();
        return item;
    }

    private void UpdateVisibilityState()
    {
        bool visible = Kind switch
        {
            SensorTreeItemKind.Sensor => _showHiddenSensors || IsVisible,
            SensorTreeItemKind.SensorType => _showHiddenSensors || Children.Any(child => child.IsVisible),
            _ => true
        };

        RowVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}

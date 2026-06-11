// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

/// <summary>
/// Tracks which sensors the user has chosen to show in the system-tray icons and in the desktop gadget, persisting each
/// choice as a per-sensor setting. Extracted from <see cref="MainWindowViewModel" /> to keep that selection concern (and
/// its settings-key convention) in one testable place.
/// </summary>
internal sealed class SensorSelectionService
{
    private readonly AppSettings _settings;

    public SensorSelectionService(AppSettings settings)
    {
        _settings = settings;
    }

    public event EventHandler? GadgetSensorsChanged;

    public event EventHandler? TraySensorsChanged;

    public bool IsSensorInGadget(SensorTreeItemViewModel item)
    {
        return item.Sensor != null && _settings.GetValue(GetSensorSettingName(item.Sensor, "gadget"), false);
    }

    public bool IsSensorInTray(SensorTreeItemViewModel item)
    {
        return item.Sensor != null && _settings.GetValue(GetSensorSettingName(item.Sensor, "tray"), false);
    }

    public IEnumerable<SensorTreeItemViewModel> GetGadgetSensorItems(SensorTreeItemViewModel? root)
    {
        return root?.EnumerateSensors().Where(IsSensorInGadget) ?? [];
    }

    public IEnumerable<SensorTreeItemViewModel> GetTraySensorItems(SensorTreeItemViewModel? root)
    {
        return root?.EnumerateSensors().Where(IsSensorInTray) ?? [];
    }

    public void SetSensorInGadget(SensorTreeItemViewModel item, bool value)
    {
        if (item.Sensor == null)
            return;

        SetSensorBooleanSetting(item.Sensor, "gadget", value);
        GadgetSensorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSensorInTray(SensorTreeItemViewModel item, bool value)
    {
        if (item.Sensor == null)
            return;

        SetSensorBooleanSetting(item.Sensor, "tray", value);
        if (!value)
            _settings.Remove(GetSensorSettingName(item.Sensor, "traycolor"));
        TraySensorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public static string GetSensorSettingName(ISensor sensor, string suffix)
    {
        return new Identifier(sensor.Identifier, suffix).ToString();
    }

    private void SetSensorBooleanSetting(ISensor sensor, string suffix, bool value)
    {
        string settingName = GetSensorSettingName(sensor, suffix);
        if (value)
            _settings.SetValue(settingName, true);
        else
            _settings.Remove(settingName);
    }
}

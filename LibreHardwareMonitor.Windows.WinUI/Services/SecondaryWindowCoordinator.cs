// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

internal sealed class SecondaryWindowCoordinator
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Action _hideShowMainWindowRequested;
    private PlotWindow? _plotWindow;
    private SensorGadgetWindow? _gadgetWindow;

    public SecondaryWindowCoordinator(MainWindowViewModel viewModel, Action hideShowMainWindowRequested)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _hideShowMainWindowRequested = hideShowMainWindowRequested ?? throw new ArgumentNullException(nameof(hideShowMainWindowRequested));
    }

    public void UpdateGadgetVisibility()
    {
        if (_viewModel.ShowGadget)
        {
            if (_gadgetWindow == null)
            {
                _gadgetWindow = new SensorGadgetWindow(_viewModel);
                _gadgetWindow.HideShowMainWindowRequested += (_, _) => _hideShowMainWindowRequested();
                _gadgetWindow.UserClosed += (_, _) =>
                {
                    _gadgetWindow = null;
                    _viewModel.ShowGadget = false;
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

    public void UpdatePlotWindowVisibility()
    {
        if (_viewModel.IsPlotWindowVisible)
        {
            if (_plotWindow == null)
            {
                _plotWindow = new PlotWindow(_viewModel.Settings, _viewModel);
                _plotWindow.ApplyTheme(_viewModel.ThemeMode);
                _plotWindow.UserClosed += (_, _) =>
                {
                    _plotWindow = null;
                    _viewModel.ShowPlot = false;
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

    public void SyncGadgetSensors()
    {
        _gadgetWindow?.UpdateSensors(_viewModel.GetGadgetSensorItems());
    }

    public void ApplyTheme(AppThemeMode themeMode)
    {
        _plotWindow?.ApplyTheme(themeMode);
    }

    public void RedrawPlot()
    {
        _plotWindow?.RedrawPlot();
    }

    public void CloseAll()
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
}

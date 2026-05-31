// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace LibreHardwareMonitor.Windows.WinUI;

public sealed class SensorGadgetWindow : Window
{
    private readonly AppSettings _settings;
    private readonly StackPanel _sensorList;
    private readonly MainWindowViewModel _viewModel;
    private readonly AppWindow _appWindow;
    private bool _closingFromOwner;

    public SensorGadgetWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _settings = viewModel.Settings;

        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.Title = "Sensor Gadget";

        _sensorList = new StackPanel { Spacing = 4, Padding = new Thickness(10) };
        ScrollViewer scrollViewer = new()
        {
            Content = _sensorList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Grid root = new()
        {
            Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
        };
        root.Children.Add(scrollViewer);
        root.ContextFlyout = BuildContextMenu();
        Content = root;

        RestoreBounds();
        Closed += SensorGadgetWindow_Closed;
    }

    public event EventHandler? HideShowMainWindowRequested;

    public event EventHandler? UserClosed;

    public void CloseFromOwner()
    {
        _closingFromOwner = true;
        Close();
    }

    public void UpdateSensors(IEnumerable<SensorTreeItemViewModel> sensorItems)
    {
        _sensorList.Children.Clear();
        SensorTreeItemViewModel[] items = sensorItems.Where(item => item.Sensor != null).ToArray();
        if (items.Length == 0)
        {
            _sensorList.Children.Add(new TextBlock
            {
                Text = "No sensors selected.",
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (IGrouping<IHardware, SensorTreeItemViewModel> group in items.GroupBy(item => GetRootHardware(item.Sensor!.Hardware)))
        {
            _sensorList.Children.Add(new TextBlock
            {
                Text = group.Key.Name,
                FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 },
                Margin = new Thickness(0, 8, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            foreach (SensorTreeItemViewModel item in group)
                _sensorList.Children.Add(CreateSensorRow(item));
        }
    }

    private Grid CreateSensorRow(SensorTreeItemViewModel item)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 12,
            MinWidth = 220
        };

        StackPanel namePanel = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        FontIcon icon = new() { FontSize = 12 };
        Bind(icon, FontIcon.GlyphProperty, item, nameof(SensorTreeItemViewModel.IconGlyph));
        namePanel.Children.Add(icon);

        TextBlock name = new() { TextTrimming = TextTrimming.CharacterEllipsis };
        Bind(name, TextBlock.TextProperty, item, nameof(SensorTreeItemViewModel.Text));
        namePanel.Children.Add(name);
        row.Children.Add(namePanel);

        TextBlock value = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Bind(value, TextBlock.TextProperty, item, nameof(SensorTreeItemViewModel.Value));
        Grid.SetColumn(value, 1);
        row.Children.Add(value);

        MenuFlyout flyout = new();
        flyout.Items.Add(CreateMenuItem("Remove from Gadget", (_, _) =>
        {
            _viewModel.SetSensorInGadget(item, false);
            UpdateSensors(_viewModel.GetGadgetSensorItems());
        }));
        row.ContextFlyout = flyout;
        return row;
    }

    private MenuFlyout BuildContextMenu()
    {
        MenuFlyout flyout = new();
        flyout.Items.Add(CreateMenuItem("Hide/Show Main Window", (_, _) => HideShowMainWindowRequested?.Invoke(this, EventArgs.Empty)));
        return flyout;
    }

    private void RestoreBounds()
    {
        int width = Math.Max(220, _settings.GetValue("sensorGadget.Width", 260));
        int height = Math.Max(160, _settings.GetValue("sensorGadget.Height", 360));
        _appWindow.Resize(new SizeInt32(width, height));

        int x = _settings.GetValue("sensorGadget.Location.X", int.MinValue);
        int y = _settings.GetValue("sensorGadget.Location.Y", int.MinValue);
        if (x != int.MinValue && y != int.MinValue)
            _appWindow.Move(new PointInt32(x, y));
    }

    private void SensorGadgetWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveBounds();
        if (!_closingFromOwner)
            UserClosed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveBounds()
    {
        _settings.SetValue("sensorGadget.Location.X", _appWindow.Position.X);
        _settings.SetValue("sensorGadget.Location.Y", _appWindow.Position.Y);
        _settings.SetValue("sensorGadget.Width", _appWindow.Size.Width);
        _settings.SetValue("sensorGadget.Height", _appWindow.Size.Height);
    }

    private static IHardware GetRootHardware(IHardware hardware)
    {
        while (hardware.Parent != null)
            hardware = hardware.Parent;

        return hardware;
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

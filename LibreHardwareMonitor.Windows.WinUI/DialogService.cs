// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LibreHardwareMonitor.Windows.WinUI;

/// <summary>
/// Builds and shows the app's content dialogs (rename, pen color, sensor parameters, web-server settings and
/// authentication, save report, about) and the save-report file picker. Extracted from <see cref="MainWindow" /> so the
/// modal UI flows live in one place; it reads the live <see cref="XamlRoot" /> through a provider and writes results
/// back through the view model.
/// </summary>
internal sealed class DialogService
{
    private readonly Func<XamlRoot?> _xamlRootProvider;
    private readonly MainWindowViewModel _viewModel;
    private readonly IntPtr _windowHandle;

    public DialogService(Func<XamlRoot?> xamlRootProvider, MainWindowViewModel viewModel, IntPtr windowHandle)
    {
        _xamlRootProvider = xamlRootProvider;
        _viewModel = viewModel;
        _windowHandle = windowHandle;
    }

    public async Task RenameAsync(SensorTreeItemViewModel item)
    {
        if (!item.CanRename)
            return;

        TextBox nameTextBox = new() { Text = item.Text, SelectionStart = 0, SelectionLength = item.Text.Length };
        ContentDialog dialog = new()
        {
            XamlRoot = _xamlRootProvider(),
            Title = "Rename Sensor",
            Content = nameTextBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            item.Text = nameTextBox.Text.Trim();
    }

    public async Task ShowPenColorAsync(SensorTreeItemViewModel item)
    {
        if (item.Sensor == null)
            return;

        ColorPicker picker = new()
        {
            Color = item.PenColor ?? global::Windows.UI.Color.FromArgb(255, 0, 120, 212),
            IsAlphaEnabled = false,
            IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true
        };

        ContentDialog dialog = new()
        {
            XamlRoot = _xamlRootProvider(),
            Title = "Pen Color",
            Content = picker,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            _viewModel.SetSensorPenColor(item, picker.Color);
    }

    public async Task ShowParametersAsync(SensorTreeItemViewModel item)
    {
        if (item.Sensor == null || item.Sensor.Parameters.Count == 0)
            return;

        StackPanel panel = new() { Spacing = 10 };
        TextBlock caption = new()
        {
            Text = item.Sensor.Name,
            FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 },
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(caption);

        ParameterEditorRow[] rows = item.Sensor.Parameters.Select(CreateParameterEditorRow).ToArray();
        foreach (ParameterEditorRow row in rows)
            panel.Children.Add(row.Container);

        ContentDialog dialog = new()
        {
            XamlRoot = _xamlRootProvider(),
            Title = "Parameters",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (sender, args) =>
        {
            foreach (ParameterEditorRow row in rows)
            {
                if (row.UseDefault.IsChecked == true)
                    continue;

                if (!float.TryParse(row.Value.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float _)
                    && !float.TryParse(row.Value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float _))
                {
                    row.Value.Header = "Invalid value";
                    args.Cancel = true;
                    return;
                }
            }

            foreach (ParameterEditorRow row in rows)
            {
                if (row.UseDefault.IsChecked == true)
                    row.Parameter.IsDefault = true;
                else if (float.TryParse(row.Value.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float currentCultureValue)
                         || float.TryParse(row.Value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out currentCultureValue))
                    row.Parameter.Value = currentCultureValue;
            }
        };

        await dialog.ShowAsync();
    }

    public async Task ShowWebServerSettingsAsync()
    {
        ComboBox interfaces = new() { MinWidth = 260 };
        foreach (string address in GetLocalIPv4Addresses())
            interfaces.Items.Add(address);
        interfaces.Items.Add("0.0.0.0");
        interfaces.SelectedItem = interfaces.Items.Contains(_viewModel.ListenerIp) ? _viewModel.ListenerIp : "0.0.0.0";

        TextBox port = new()
        {
            Header = "Port",
            Text = _viewModel.ListenerPort.ToString(CultureInfo.InvariantCulture),
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }
        };

        HyperlinkButton link = new()
        {
            Content = _viewModel.WebServerUrl,
            NavigateUri = new Uri(_viewModel.WebServerUrl)
        };

        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Interface", FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 } });
        content.Children.Add(interfaces);
        content.Children.Add(port);
        content.Children.Add(link);

        ContentDialog dialog = new()
        {
            XamlRoot = _xamlRootProvider(),
            Title = "Remote Web Server",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!int.TryParse(port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
            {
                port.Header = "Port must be 1-65535";
                args.Cancel = true;
                return;
            }

            _viewModel.ListenerIp = interfaces.SelectedItem as string ?? "0.0.0.0";
            _viewModel.ListenerPort = parsedPort;
        };

        await dialog.ShowAsync();
    }

    public async Task ShowWebServerAuthenticationAsync()
    {
        CheckBox enabled = new() { Content = "Enable HTTP authentication", IsChecked = _viewModel.AuthWebServer };
        TextBox userName = new()
        {
            Header = "Username",
            Text = _viewModel.AuthWebServerUserName,
            IsEnabled = enabled.IsChecked == true
        };
        PasswordBox password = new()
        {
            Header = "New password",
            IsEnabled = enabled.IsChecked == true
        };

        enabled.Checked += (_, _) => userName.IsEnabled = password.IsEnabled = true;
        enabled.Unchecked += (_, _) => userName.IsEnabled = password.IsEnabled = false;

        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(enabled);
        content.Children.Add(userName);
        content.Children.Add(password);

        ContentDialog dialog = new()
        {
            XamlRoot = _xamlRootProvider(),
            Title = "Remote Web Server Authentication",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, _) =>
        {
            _viewModel.AuthWebServer = enabled.IsChecked == true;
            _viewModel.AuthWebServerUserName = userName.Text;
            _viewModel.SetAuthPassword(password.Password);
        };

        await dialog.ShowAsync();
    }

    public async Task SaveReportAsync()
    {
        FileSavePicker picker = new()
        {
            SuggestedFileName = "LibreHardwareMonitor.Report",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("Text", [".txt"]);
        InitializeWithWindow.Initialize(picker, _windowHandle);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file == null)
            return;

        CachedFileManager.DeferUpdates(file);
        await FileIO.WriteTextAsync(file, _viewModel.GetReport());
        await CachedFileManager.CompleteUpdatesAsync(file);
    }

    public async Task ShowAboutAsync()
    {
        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "Libre Hardware Monitor",
            FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 },
            FontSize = 18
        });
        content.Children.Add(new TextBlock
        {
            Text = $"Version {typeof(DialogService).Assembly.GetName().Version}",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "A free, open source hardware monitoring application.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = "Project website",
            NavigateUri = new Uri("https://github.com/LibreHardwareMonitor/LibreHardwareMonitor")
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = "Mozilla Public License 2.0",
            NavigateUri = new Uri("https://www.mozilla.org/MPL/2.0/")
        });

        ContentDialog dialog = new()
        {
            XamlRoot = _xamlRootProvider(),
            Title = "Libre Hardware Monitor",
            Content = content,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    private static ParameterEditorRow CreateParameterEditorRow(IParameter parameter)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(120) }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        TextBlock name = new()
        {
            Text = parameter.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        grid.Children.Add(name);

        CheckBox useDefault = new()
        {
            Content = "Default",
            IsChecked = parameter.IsDefault,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(useDefault, 1);
        grid.Children.Add(useDefault);

        TextBox value = new()
        {
            Text = parameter.Value.ToString(CultureInfo.CurrentCulture),
            IsEnabled = !parameter.IsDefault
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        TextBlock description = new()
        {
            Text = parameter.Description,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(description, 1);
        Grid.SetColumnSpan(description, 3);
        grid.Children.Add(description);

        useDefault.Checked += (_, _) => value.IsEnabled = false;
        useDefault.Unchecked += (_, _) => value.IsEnabled = true;

        return new ParameterEditorRow(parameter, grid, useDefault, value);
    }

    private static string[] GetLocalIPv4Addresses()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private sealed record ParameterEditorRow(IParameter Parameter, Grid Container, CheckBox UseDefault, TextBox Value);
}

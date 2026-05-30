// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.IO;
using Microsoft.Win32;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

public sealed class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = nameof(LibreHardwareMonitor);

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool Startup
    {
        get
        {
            using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(RegistryPath);
            string? value = registryKey?.GetValue(RegistryValueName) as string;
            return string.Equals(value, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            if (!IsAvailable)
                throw new InvalidOperationException("Windows startup registration is not available on this platform.");

            using RegistryKey? registryKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (value)
                registryKey?.SetValue(RegistryValueName, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LibreHardwareMonitor.Windows.WinUI.exe"));
            else
                registryKey?.DeleteValue(RegistryValueName, false);
        }
    }
}

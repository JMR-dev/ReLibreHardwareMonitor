// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

/// <summary>
/// Optional HTTP server exposing sensor data. Extracted as an interface so the view model can be
/// unit-tested with a fake (and so the real server's network resources stay out of tests).
/// </summary>
public interface IRemoteWebServer : IDisposable
{
    bool AuthEnabled { get; set; }

    string ListenerIp { get; set; }

    int ListenerPort { get; set; }

    string PasswordHash { get; }

    bool PlatformNotSupported { get; }

    string UserName { get; set; }

    void SetRootProvider(Func<ViewModels.SensorTreeItemViewModel?> rootProvider);

    void SetPassword(string plainPassword);

    bool Start();

    bool Stop();

    void Quit();
}

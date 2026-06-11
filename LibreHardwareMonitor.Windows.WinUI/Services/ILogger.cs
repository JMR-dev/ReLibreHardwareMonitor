// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

/// <summary>
/// CSV sensor logger. Extracted as an interface so the view model can be unit-tested with a fake.
/// </summary>
public interface ILogger
{
    LoggerFileRotation FileRotationMethod { get; set; }

    TimeSpan LoggingInterval { get; set; }

    void Log();
}

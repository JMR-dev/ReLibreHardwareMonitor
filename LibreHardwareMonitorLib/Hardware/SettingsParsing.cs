// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// Shared parsing for the boolean "defer detection / diagnostics" flags that several hardware components read from
/// <see cref="ISettings" /> and environment variables. Keeps the truthy vocabulary and the env-overrides-setting
/// precedence in one place so the components cannot drift.
/// </summary>
internal static class SettingsParsing
{
    /// <summary>Returns whether <paramref name="value" /> is one of the accepted truthy tokens (1/true/yes/on).</summary>
    public static bool IsTruthy(string value)
    {
        return value != null
               && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a defer flag: a non-empty environment variable wins, otherwise the setting value (default "false").
    /// </summary>
    public static bool ShouldDefer(ISettings settings, string settingName, string environmentVariable)
    {
        string environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return IsTruthy(environmentValue);

        return IsTruthy(settings.GetValue(settingName, "false"));
    }
}

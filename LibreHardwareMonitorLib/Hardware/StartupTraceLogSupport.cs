// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.IO;

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// Shared file-name and CSV helpers for startup timing logs.
/// </summary>
public static class StartupTraceLogSupport
{
    /// <summary>
    /// Escapes a single CSV field using double quotes when the value contains a comma, quote, or newline.
    /// </summary>
    public static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (!value.Contains(",") && !value.Contains("\"") && !value.Contains("\r") && !value.Contains("\n"))
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Resolves a timestamped startup trace log file name from a file-name prefix and optional configured path.
    /// </summary>
    public static string GetLogFileName(string fileNamePrefix, string configuredPath)
    {
        string fileName = $"{fileNamePrefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log";
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Path.Combine(AppContext.BaseDirectory, fileName);

        if (configuredPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || configuredPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || Directory.Exists(configuredPath))
        {
            return Path.Combine(configuredPath, fileName);
        }

        return configuredPath;
    }
}

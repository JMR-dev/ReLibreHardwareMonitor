// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace LibreHardwareMonitor.Hardware;

internal sealed class HardwareStartupTrace : IDisposable
{
    private const string EnabledEnvironmentVariable = "LHM_HARDWARE_STARTUP_TIMING";
    private const string PathEnvironmentVariable = "LHM_HARDWARE_STARTUP_TIMING_PATH";
    private const string EnabledSetting = "diagnostics.hardwareStartupTiming";
    private const string PathSetting = "diagnostics.hardwareStartupTimingPath";

    private readonly List<Entry> _entries = new();
    private readonly string _fileName;
    private readonly Stopwatch _totalStopwatch;
    private bool _disposed;

    private HardwareStartupTrace(ISettings settings)
    {
        _fileName = GetLogFileName(settings);
        _totalStopwatch = Stopwatch.StartNew();
    }

    public static HardwareStartupTrace Create(ISettings settings)
    {
        if (!IsEnabled(settings))
            return null;

        return new HardwareStartupTrace(settings);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _totalStopwatch.Stop();
        WriteLog();
    }

    public T Measure<T>(string phase, Func<T> action)
    {
        return Measure(phase, action, null);
    }

    public T Measure<T>(string phase, Func<T> action, Func<T, string> getDetail)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            T result = action();
            stopwatch.Stop();
            AddEntry(phase, stopwatch.Elapsed, "OK", GetHardwareCount(result), GetDetail(result, getDetail));
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AddEntry(phase, stopwatch.Elapsed, "ERROR", null, $"{ex.GetType().FullName}: {ex.Message}");
            throw;
        }
    }

    public void Measure(string phase, Action action)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            action();
            stopwatch.Stop();
            AddEntry(phase, stopwatch.Elapsed, "OK", null, "");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AddEntry(phase, stopwatch.Elapsed, "ERROR", null, $"{ex.GetType().FullName}: {ex.Message}");
            throw;
        }
    }

    public void Skip(string phase, string reason)
    {
        AddEntry(phase, TimeSpan.Zero, "SKIPPED", null, reason);
    }

    private static bool IsEnabled(ISettings settings)
    {
        string settingValue = settings.GetValue(EnabledSetting, "false");
        string environmentValue = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable) ?? "";

        return IsTruthy(settingValue) || IsTruthy(environmentValue);
    }

    private static bool IsTruthy(string value)
    {
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLogFileName(ISettings settings)
    {
        string configuredPath = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
            configuredPath = settings.GetValue(PathSetting, "");

        string fileName = $"LibreHardwareMonitor.HardwareStartupTiming-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log";
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

    private static int? GetHardwareCount<T>(T result)
    {
        return result is IGroup group ? group.Hardware.Count : null;
    }

    private static string GetDetail<T>(T result, Func<T, string> getDetail)
    {
        if (getDetail == null)
            return "";

        try
        {
            return getDetail(result) ?? "";
        }
        catch (Exception ex)
        {
            return $"Detail unavailable: {ex.GetType().FullName}: {ex.Message}";
        }
    }

    private void AddEntry(string phase, TimeSpan elapsed, string status, int? hardwareCount, string detail)
    {
        Entry entry = new(phase, elapsed, status, hardwareCount, detail);
        _entries.Add(entry);
        Debug.WriteLine($"Hardware startup: {entry}");
    }

    private void WriteLog()
    {
        try
        {
            string directory = Path.GetDirectoryName(_fileName);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_fileName, BuildLog());
        }
        catch
        {
            // Startup tracing must never affect hardware initialization.
        }
    }

    private string BuildLog()
    {
        StringBuilder builder = new();
        builder.AppendLine("Libre Hardware Monitor hardware startup timing");
        builder.Append("Timestamp: ");
        builder.AppendLine(DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture));
        builder.Append("Total elapsed: ");
        builder.Append(_totalStopwatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        builder.AppendLine(" ms");
        builder.AppendLine();
        builder.AppendLine("Phase,ElapsedMs,HardwareCount,Status,Detail");

        foreach (Entry entry in _entries)
        {
            builder.Append(EscapeCsv(entry.Phase));
            builder.Append(',');
            builder.Append(entry.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(entry.HardwareCount?.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.Append(',');
            builder.Append(entry.Status);
            builder.Append(',');
            builder.AppendLine(EscapeCsv(entry.Detail));
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (!value.Contains(",") && !value.Contains("\"") && !value.Contains("\r") && !value.Contains("\n"))
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed class Entry
    {
        public Entry(string phase, TimeSpan elapsed, string status, int? hardwareCount, string detail)
        {
            Phase = phase;
            Elapsed = elapsed;
            Status = status;
            HardwareCount = hardwareCount;
            Detail = detail;
        }

        public string Phase { get; }

        public TimeSpan Elapsed { get; }

        public string Status { get; }

        public int? HardwareCount { get; }

        public string Detail { get; }

        public override string ToString()
        {
            string hardwareCount = HardwareCount?.ToString(CultureInfo.InvariantCulture) ?? "-";
            return $"{Phase}: {Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms, {Status}, {hardwareCount} hardware, {Detail}";
        }
    }
}

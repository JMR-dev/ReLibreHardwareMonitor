// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

internal sealed class WinUiStartupTrace : IDisposable
{
    private const string EnabledEnvironmentVariable = "LHM_WINUI_STARTUP_TIMING";
    private const string PathEnvironmentVariable = "LHM_WINUI_STARTUP_TIMING_PATH";

    private readonly List<Entry> _entries = new();
    private readonly string _fileName;
    private readonly object _lock = new();
    private readonly Stopwatch _totalStopwatch;
    private bool _completed;
    private bool _disposed;

    private WinUiStartupTrace()
    {
        _fileName = GetLogFileName();
        _totalStopwatch = Stopwatch.StartNew();
        Mark("WinUIStartupTrace.Begin");
    }

    public bool IsComplete
    {
        get
        {
            lock (_lock)
                return _completed || _disposed;
        }
    }

    public static WinUiStartupTrace? Create()
    {
        return IsEnabled() ? new WinUiStartupTrace() : null;
    }

    public void Complete(string phase, string detail = "")
    {
        bool shouldWrite;
        lock (_lock)
        {
            shouldWrite = !_completed && !_disposed;
            if (shouldWrite)
            {
                AddEntryLocked(new Entry(phase, _totalStopwatch.Elapsed, TimeSpan.Zero, "COMPLETE", detail));
                _completed = true;
                _totalStopwatch.Stop();
            }
        }

        if (shouldWrite)
            WriteLog();
    }

    public void Dispose()
    {
        bool shouldWrite;
        lock (_lock)
        {
            shouldWrite = !_disposed;
            if (shouldWrite)
            {
                if (!_completed)
                {
                    AddEntryLocked(new Entry("WinUIStartupTrace.Dispose", _totalStopwatch.Elapsed, TimeSpan.Zero, "DISPOSED", ""));
                    _totalStopwatch.Stop();
                }

                _disposed = true;
            }
        }

        if (shouldWrite)
            WriteLog();
    }

    public void Flush()
    {
        WriteLog();
    }

    public void Mark(string phase, string detail = "")
    {
        AddEntry(phase, TimeSpan.Zero, "MARK", detail);
    }

    public void Measure(string phase, Action action)
    {
        Measure(phase, action, null);
    }

    public void Measure(string phase, Action action, Func<string>? getDetail)
    {
        TimeSpan start = _totalStopwatch.Elapsed;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            action();
            stopwatch.Stop();
            AddEntry(phase, start, stopwatch.Elapsed, "OK", GetDetail(getDetail));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AddEntry(phase, start, stopwatch.Elapsed, "ERROR", $"{ex.GetType().FullName}: {ex.Message}");
            throw;
        }
    }

    public T Measure<T>(string phase, Func<T> action)
    {
        return Measure(phase, action, null);
    }

    public T Measure<T>(string phase, Func<T> action, Func<T, string>? getDetail)
    {
        TimeSpan start = _totalStopwatch.Elapsed;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            T result = action();
            stopwatch.Stop();
            AddEntry(phase, start, stopwatch.Elapsed, "OK", GetDetail(result, getDetail));
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AddEntry(phase, start, stopwatch.Elapsed, "ERROR", $"{ex.GetType().FullName}: {ex.Message}");
            throw;
        }
    }

    public async Task MeasureAsync(string phase, Func<Task> action)
    {
        await MeasureAsync(phase, action, null);
    }

    public async Task MeasureAsync(string phase, Func<Task> action, Func<string>? getDetail)
    {
        TimeSpan start = _totalStopwatch.Elapsed;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            await action();
            stopwatch.Stop();
            AddEntry(phase, start, stopwatch.Elapsed, "OK", GetDetail(getDetail));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AddEntry(phase, start, stopwatch.Elapsed, "ERROR", $"{ex.GetType().FullName}: {ex.Message}");
            throw;
        }
    }

    public async Task<T> MeasureAsync<T>(string phase, Func<Task<T>> action)
    {
        return await MeasureAsync(phase, action, null);
    }

    public async Task<T> MeasureAsync<T>(string phase, Func<Task<T>> action, Func<T, string>? getDetail)
    {
        TimeSpan start = _totalStopwatch.Elapsed;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            T result = await action();
            stopwatch.Stop();
            AddEntry(phase, start, stopwatch.Elapsed, "OK", GetDetail(result, getDetail));
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AddEntry(phase, start, stopwatch.Elapsed, "ERROR", $"{ex.GetType().FullName}: {ex.Message}");
            throw;
        }
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (!value.Contains(",") && !value.Contains("\"") && !value.Contains("\r") && !value.Contains("\n"))
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string GetDetail(Func<string>? getDetail)
    {
        if (getDetail == null)
            return "";

        try
        {
            return getDetail() ?? "";
        }
        catch (Exception ex)
        {
            return $"Detail unavailable: {ex.GetType().FullName}: {ex.Message}";
        }
    }

    private static string GetDetail<T>(T result, Func<T, string>? getDetail)
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

    private static string GetLogFileName()
    {
        string? configuredPath = Environment.GetEnvironmentVariable(PathEnvironmentVariable);

        string fileName = $"LibreHardwareMonitor.WinUIStartupTiming-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log";
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

    private static bool IsEnabled()
    {
        string environmentValue = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable) ?? "";
        return IsTruthy(environmentValue);
    }

    private static bool IsTruthy(string value)
    {
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private void AddEntry(string phase, TimeSpan elapsed, string status, string detail)
    {
        AddEntry(phase, _totalStopwatch.Elapsed, elapsed, status, detail);
    }

    private void AddEntry(string phase, TimeSpan start, TimeSpan elapsed, string status, string detail)
    {
        lock (_lock)
        {
            if (_completed || _disposed)
                return;

            AddEntryLocked(new Entry(phase, start, elapsed, status, detail));
        }
    }

    private void AddEntryLocked(Entry entry)
    {
        _entries.Add(entry);
        Debug.WriteLine($"WinUI startup: {entry}");
    }

    private string BuildLog(IReadOnlyList<Entry> entries, TimeSpan totalElapsed)
    {
        StringBuilder builder = new();
        builder.AppendLine("Libre Hardware Monitor WinUI startup timing");
        builder.Append("Timestamp: ");
        builder.AppendLine(DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture));
        builder.Append("Total elapsed: ");
        builder.Append(totalElapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        builder.AppendLine(" ms");
        builder.AppendLine();
        builder.AppendLine("Phase,StartMs,ElapsedMs,Status,Detail");

        foreach (Entry entry in entries.OrderBy(entry => entry.Start))
        {
            builder.Append(EscapeCsv(entry.Phase));
            builder.Append(',');
            builder.Append(entry.Start.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(entry.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(entry.Status);
            builder.Append(',');
            builder.AppendLine(EscapeCsv(entry.Detail));
        }

        return builder.ToString();
    }

    private void WriteLog()
    {
        Entry[] entries;
        TimeSpan totalElapsed;
        lock (_lock)
        {
            entries = _entries.ToArray();
            totalElapsed = _totalStopwatch.Elapsed;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_fileName);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_fileName, BuildLog(entries, totalElapsed));
        }
        catch
        {
            // Startup tracing must never affect application startup.
        }
    }

    private sealed class Entry
    {
        public Entry(string phase, TimeSpan start, TimeSpan elapsed, string status, string detail)
        {
            Phase = phase;
            Start = start;
            Elapsed = elapsed;
            Status = status;
            Detail = detail;
        }

        public string Detail { get; }

        public TimeSpan Elapsed { get; }

        public string Phase { get; }

        public TimeSpan Start { get; }

        public string Status { get; }

        public override string ToString()
        {
            return $"{Phase}: start {Start.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms, elapsed {Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms, {Status}, {Detail}";
        }
    }
}

// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Windows.WinUI.Services.Tracing;

/// <summary>
/// Optional startup instrumentation consumed by startup operations. A no-op implementation
/// is used when tracing is disabled, so callers never need null checks.
/// </summary>
internal interface IStartupTracer : IDisposable
{
    bool IsComplete { get; }

    void Mark(string phase, string detail = "");

    void Complete(string phase, string detail = "");

    void Flush();

    void Measure(string phase, Action action);

    void Measure(string phase, Action action, Func<string>? getDetail);

    T Measure<T>(string phase, Func<T> action);

    T Measure<T>(string phase, Func<T> action, Func<T, string>? getDetail);

    Task MeasureAsync(string phase, Func<Task> action);

    Task MeasureAsync(string phase, Func<Task> action, Func<string>? getDetail);

    Task<T> MeasureAsync<T>(string phase, Func<Task<T>> action);

    Task<T> MeasureAsync<T>(string phase, Func<Task<T>> action, Func<T, string>? getDetail);
}

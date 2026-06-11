// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Windows.WinUI.Services.Tracing;

/// <summary>
/// Zero-overhead tracer used when startup tracing is disabled. Measure overloads simply
/// invoke the supplied delegate; everything else is a no-op.
/// </summary>
internal sealed class NoOpStartupTracer : IStartupTracer
{
    public static readonly NoOpStartupTracer Instance = new();

    private NoOpStartupTracer()
    {
    }

    public bool IsComplete => false;

    public void Mark(string phase, string detail = "")
    {
    }

    public void Complete(string phase, string detail = "")
    {
    }

    public void Flush()
    {
    }

    public void Measure(string phase, Action action)
    {
        action();
    }

    public void Measure(string phase, Action action, Func<string>? getDetail)
    {
        action();
    }

    public T Measure<T>(string phase, Func<T> action)
    {
        return action();
    }

    public Task MeasureAsync(string phase, Func<Task> action)
    {
        return action();
    }

    public void Dispose()
    {
    }
}

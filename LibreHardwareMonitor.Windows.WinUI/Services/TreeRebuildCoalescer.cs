// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

/// <summary>
/// Coalesces a burst of hardware-change notifications into as few tree rebuilds as possible without dropping the last
/// one. After the first request a single worker waits a short debounce delay, then rebuilds while more changes keep
/// arriving; a change that lands between the worker finishing and the queued flag clearing re-arms the worker.
/// Extracted from <see cref="HardwareMonitorService" /> so this concurrency logic can be unit-tested with a controllable
/// delay.
/// </summary>
internal sealed class TreeRebuildCoalescer
{
    private readonly Action _rebuild;
    private readonly Func<bool> _canRebuild;
    private readonly Func<Task> _delay;
    private int _queued;
    private volatile bool _dirty;

    public TreeRebuildCoalescer(Action rebuild, Func<bool> canRebuild, Func<Task>? delay = null)
    {
        _rebuild = rebuild;
        _canRebuild = canRebuild;
        _delay = delay ?? (() => Task.Delay(100));
    }

    /// <summary>The most recently started worker. Exposed only so tests can deterministically await a rebuild cycle.</summary>
    internal Task RebuildLoopTask { get; private set; } = Task.CompletedTask;

    public void Request()
    {
        _dirty = true;
        if (Interlocked.Exchange(ref _queued, 1) == 1)
            return;

        RebuildLoopTask = Task.Run(async () =>
        {
            try
            {
                await _delay().ConfigureAwait(false);

                // Rebuild until no change has arrived since the last rebuild began, so a change that lands while a
                // rebuild is in flight is not coalesced away and lost.
                while (_dirty)
                {
                    _dirty = false;
                    if (_canRebuild())
                        _rebuild();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hardware tree rebuild failed: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _queued, 0);

                // A change may have slipped in between the loop's exit and clearing the flag; re-queue so it is honored.
                if (_dirty)
                    Request();
            }
        });
    }
}

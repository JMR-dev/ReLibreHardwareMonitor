// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Windows.WinUI.Services;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Services;

public class TreeRebuildCoalescerTests
{
    [Fact]
    public async Task Request_RebuildsOnceAfterDelay()
    {
        var gate = new TaskCompletionSource();
        int rebuilds = 0;
        var coalescer = new TreeRebuildCoalescer(() => Interlocked.Increment(ref rebuilds), () => true, () => gate.Task);

        coalescer.Request();
        Assert.Equal(0, rebuilds); // worker is blocked on the (not-yet-completed) delay

        gate.SetResult();
        await coalescer.RebuildLoopTask;

        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public async Task Request_CoalescesBurstIntoSingleRebuild()
    {
        var gate = new TaskCompletionSource();
        int rebuilds = 0;
        var coalescer = new TreeRebuildCoalescer(() => Interlocked.Increment(ref rebuilds), () => true, () => gate.Task);

        coalescer.Request();
        coalescer.Request();
        coalescer.Request();

        gate.SetResult();
        await coalescer.RebuildLoopTask;

        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public async Task Request_DoesNotRebuildWhenCannotRebuild()
    {
        var gate = new TaskCompletionSource();
        int rebuilds = 0;
        var coalescer = new TreeRebuildCoalescer(() => Interlocked.Increment(ref rebuilds), () => false, () => gate.Task);

        coalescer.Request();
        gate.SetResult();
        await coalescer.RebuildLoopTask;

        Assert.Equal(0, rebuilds);
    }
}

# `feat-WinUI-3` code-review follow-up

Date: 2026-06-03
Scope reviewed: `git diff master...HEAD` (the WinUI 3 app + the `LibreHardwareMonitorLib` startup-optimization changes).

A max-effort review of the branch produced 15 findings plus a set of investigated-and-cleared items. **All actionable findings have now been fixed** (in two passes); this document records what changed, the few things intentionally left, and the items confirmed correct so they aren't re-flagged.

Severity legend: 🔴 crash/corruption/security · 🟠 functional defect · 🟡 cleanup/efficiency.

Verified after every change: `LibreHardwareMonitorLib.Tests` 3/3, `LibreHardwareMonitor.Windows.WinUI.Tests` 211/211; lib + WinUI + WinForms all build with 0 errors (`-c Release -p:Platform=x64`).

---

## 1. Fixed — correctness

### F1 🔴 `Computer.Close()` raced native teardown against in-flight deferred discovery
`LibreHardwareMonitorLib/Hardware/Computer.cs`
`CancelDeferredGroupRun()` now cancels, marks the run cancelled, then **drains** the tracked deferred tasks (`Task.WaitAll`, via `WaitForDeferredGroupTasks`) before disposing the token source — so a background group constructor can no longer call into OpCode/native APIs while `OpCode.Close()`/`Mutexes.Close()` run.

### F2 🔴 `_open` was a non-volatile check-then-act with no re-entrancy guard
`LibreHardwareMonitorLib/Hardware/Computer.cs`
Added `_openLock` serializing `Open`/`OpenAsync`/`OpenInternal`/`Close`/`Reset` (body extracted into `OpenCore`), making the `_open` check-and-set atomic. Stops a `Close()`-during-async-open from no-op-and-leaking, and stops two concurrent opens from double-initializing.

### F3 🔴 `Logger` read/wrote `_sensors`/`_identifiers` cross-thread without synchronization
`LibreHardwareMonitor.Windows.WinUI/Services/Logger.cs`
Added `_sync`; the paired arrays are published together under it, mutated under it in `SensorAdded`/`SensorRemoved`, and snapshotted under it in `Log()`. The lock is never held across `VisitComputer` or file I/O (no coupling with `Computer`'s locks → no deadlock).

### D1 🟠 `GenericCpu`'s deferred TSC task was uncancellable and only waited 100 ms on close
`LibreHardwareMonitorLib/Hardware/Cpu/GenericCpu.cs`
The estimation task now receives a `CancellationToken` (new `_timeStampCounterFrequencyCancellation`), checks it before pinning affinity and between measurement windows, and `Close()` cancels then **fully** waits for it (no 100 ms cap) before `base.Close()`. The task calls `OpCode.Rdtsc`, so it must finish or skip before teardown.

### D2 🟠 `HardwareAdded`/`HardwareRemoved` can now fire on a background thread; WinForms consumers mutated UI unmarshaled
`LibreHardwareMonitor.Windows.Forms/UI/{MainForm,SystemTray,SensorGadget}.cs`, `Utilities/Logger.cs`
The three UI consumers capture the UI `SynchronizationContext` at construction and re-dispatch `HardwareAdded`/`HardwareRemoved` to it when invoked from another thread (a no-op in the normal synchronous case). The Forms `Logger` got the same `_sync` fix as F3. This makes the WinForms app safe when an `LHM_*_DEFER_DETECTION` env var pushes discovery onto a background thread.

### D3 ✅ `_deferredGroupCancellationTokenSource` accessed outside `_deferredGroupLock`
Resolved by F2: all access now occurs under `_openLock` (its only callers run under that lock).

### D4 🟠 Deferred-DIMM `HardwareAdded` ordering wasn't serialized with `AddCore`
`LibreHardwareMonitorLib/Hardware/{IHardwareDiscoveryTask,Memory/MemoryGroup,Computer}.cs`
`IHardwareDiscoveryTask` gained `StartHardwareDiscovery()`. `MemoryGroup` no longer starts its DIMM task in the constructor; it records the pending work and starts it from `StartHardwareDiscovery()`. `AddCore` now, under `_lock`, subscribes and snapshots the group's current hardware, then (outside the lock) calls `StartHardwareDiscovery()` and replays the snapshot — so each discovered DIMM is announced exactly once (never dropped, never duplicated).

### D5 🔴 PBKDF2 verify accepted an empty key segment → any password authenticated
`LibreHardwareMonitor.Windows.WinUI/Services/PasswordHasher.cs`
`VerifyPbkdf2` now rejects an empty salt or hash segment before the constant-time compare (an empty expected hash made `FixedTimeEquals(empty, empty)` return true).

### D6 🟠 Run completion ignored a nested `IHardwareDiscoveryTask` registered after the snapshot
`LibreHardwareMonitorLib/Hardware/Computer.cs`
`CompleteDeferredGroupRunWhenRegistered` now re-arms (`WaitForRegisteredTasksThenComplete`): after each `Task.WhenAll` it re-checks for newly-registered tasks (comparing against the count already awaited) and only completes the run once the set is stable, so completion can't fire while discovery is still running.

---

## 2. Fixed — cleanup / efficiency

### D7 🟡 `IsTruthy` duplicated across files
New `LibreHardwareMonitorLib/Hardware/SettingsParsing.cs` (`IsTruthy` + `ShouldDefer`). The five library copies (`Computer`, `HardwareStartupTrace`, `MemoryGroup`, `GenericCpu`, `IntelCpu`) now call it. (The WinUI `StartupTracer` copy is in a different assembly and was left as-is.)

### D8 🟡 `ShouldDefer*` env-then-setting pattern duplicated 4–5 ways
All collapsed to `SettingsParsing.ShouldDefer(settings, settingKey, envVar)`.

### D9 🟡 `TrayIconService.Update()` re-rendered every GDI tray icon each tick
`SensorIconRenderer.GetRenderKey(sensor)` returns a key for the drawn text + color; `Update` reuses the existing icon and only refreshes the tooltip when the key is unchanged, skipping the DC/DIB/font/brush/HICON churn.

### D10 🟡 `PlotTrackingService.Track()` sorted the whole history every tick
Dropped the redundant `OrderBy(value.Time)` over the full (potentially 24h) history; the latest timestamp is tracked in the same single pass and the existing final `GroupBy`/`OrderBy` still produces ordered, de-duplicated output. (A full persistent-list rewrite remains possible but wasn't needed to remove the per-tick full sort.)

### D11 🟡 `MainWindowViewModel.UpdateStatus()` walked the whole tree each tick
Hardware/sensor counts are cached (`RefreshStatusCounts`, called from `UpdateRoot` when the tree actually changes); the per-tick `UpdateStatus` only formats the string from the cached counts.

### D12 🟡 Dead `cancellationTokenSource == null` branch in the deferred-add helpers
Removed from `AddDeferredGroup`/`AddDeferredGroups` (unreachable since `StartDeferredGroupRun` always assigns the token source under `_openLock` before `AddGroups`).

### Web server 🟡 fresh `JsonSerializerOptions` per `data.json` request
`RemoteWebServer` now uses a single `static readonly JsonOptions`, restoring System.Text.Json's per-options metadata cache on the frequently-polled path.

---

## 3. Intentionally left (with rationale)

- **IntelCpu clock sensors read 0 MHz briefly at startup under deferred-TSC config** (`IntelCpu.cs`): documented, intended behavior — the guard keeps the previous value rather than reporting 0, and it self-corrects within ~1–2 updates.
- **`Measure(trace, …)` null-guard wrapper pairs** duplicated across five hardware files: a `NoOp` null-object for `HardwareStartupTrace` (as WinUI's `NoOpStartupTracer` already does) would remove them, but the churn touches many hot construction sites for little gain.
- **WinUI `StartupTracer.IsTruthy`**: a different assembly from the library `SettingsParsing`; not worth widening the lib's public surface for one diagnostic flag.
- **`RemoteWebServer.FindSensor` is O(tree) per `/Sensor` request**: `/Sensor` is an infrequent, user-initiated control action (unlike the polled `data.json`), so an id→sensor map wasn't worth the added state.

---

## 4. Investigated and cleared (correct as written — do not re-flag)

- **`D3DDisplayDevice` try/finally refactor** is a **leak fix** (closes the adapter on every `return` path), not a regression.
- **`Computer.GetIntelCpus()`** does **not** re-run a throwaway CPU probe — the synchronous `CpuGroup` is already in `_groups` before the deferred Intel-GPU task runs, so it is reused.
- **`CpuGroup`** was **not** parallelized — only refactored to extract `CreateCpu`. Returning `null` for unsupported AMD families preserves the original "add nothing" behavior.
- **`TreeRebuildCoalescer`** correctly coalesces a burst into ~one rebuild (single debounced worker, re-arms on a late change).
- **Disposed-`CancellationToken` reads** after cancellation are safe (`IsCancellationRequested` does not throw post-dispose).
- **Startup tracing** is fully no-op when disabled (`HardwareStartupTrace.Create` returns `null`; the `Measure` wrappers and `MainWindowViewModel.MeasureStartup` short-circuit).
- **WinUI tree updates are marshaled**: the VM's `HardwareMonitor_TreeRebuilt` handler marshals to the UI via `DispatcherQueue.TryEnqueue`. (Only `Logger` was unmarshaled — fixed in F3.)

---

## Architectural note (A1) — partially addressed

The defer-detection duplication (D7/D8) is now centralized in `SettingsParsing`, and `IHardwareDiscoveryTask` now owns the start/notify ordering (D4/D6). The deeper generalization remains available: a single group-keyed background-discovery policy (one `(settingKey, envVar)` table + a shared `IHardwareDiscoveryTask` base owning token + completion) would let a new deferrable group be added with no new constant and no new hand-written branch in `AddGroups`, and would automatically be covered by the central drain (F1) and completion accounting (D6).

---

## Verify (run in an elevated VS developer shell; `-p:Platform=x64` is required for the CsWin32 SetupDi generation)

```pwsh
dotnet test LibreHardwareMonitorLib.Tests/LibreHardwareMonitorLib.Tests.csproj -c Release -p:Platform=x64
dotnet test LibreHardwareMonitor.Windows.WinUI.Tests/LibreHardwareMonitor.Windows.WinUI.Tests.csproj -c Release -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -p:Platform=x64
```

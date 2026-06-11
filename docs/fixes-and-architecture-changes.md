# PR Description: WinUI 3 Migration, Performance Optimization, and Architectural Refactoring

## Summary
This Pull Request modernizes the presentation layer, optimizes startup performance, refactors the codebase to a clean MVVM + Dependency Injection (DI) architecture, and hardens thread safety and correctness across both `LibreHardwareMonitorLib` and the UI applications. 

Specifically, this branch introduces a new modern Windows front-end built with **WinUI 3** and modern **.NET**, deprecating direct reliance on legacy WinForms/ .NET Framework 4.7.2 for the primary modern target, while maintaining thread-safe compatibility for the existing WinForms app. It also resolves critical startup blocking issues and concurrency bottlenecks through background/deferred hardware discovery.

---

## Key Changes

### 1. Modern WinUI 3 Presentation Layer
- **Fluent Design & Mica Backdrops**: Leverages modern Windows 11 styling including Mica theme materials matching light and dark modes.
- **Improved UI Elements**: 
  - Host a modern `TreeView` control for sensor visualization.
  - Interactive plot views (`PlotView`) containing a configurable floating top-right plot legend overlay.
  - Secondary windows including a floating desktop sensor gadget (`SensorGadgetWindow`) and pop-out graphs.
- **High-DPI Support**: Fixes layout issues on high-DPI scaling displays and tray restore behavior.
- **MVVM Pattern**: Designed around cleanly separated ViewModels (`MainWindowViewModel`, `SensorTreeItemViewModel`, etc.) using Dependency Injection.

### 2. Startup Performance & Progressive Loading
- **Staged & Async Initialization**: Refactored `Computer.Open()` to support staged/progressive loading. Rather than blocking the main thread on slow motherboard LPC/EC/IPMI, memory SPD, or GPU probes, the UI shell loads immediately and populates hardware groups progressively as background discovery tasks complete.
- **Background TSC Estimation**: Moved CPU TSC (Time Stamp Counter) frequency estimation off the startup blocking path in `GenericCpu.cs`. It now runs asynchronously with full cancellation support.
- **Startup Instrumentation**: Introduced tracing and measurement tools under the `IStartupTracer` abstraction (`FileStartupTracer`, `NoOpStartupTracer`) to track execution time per discovery phase and identify bottlenecks.

### 3. Architecture & Composition Root Modernization
- **Dependency Injection**: Replaced manual instantiation chains with `Microsoft.Extensions.DependencyInjection`.
- **Decoupled Services**: Extracted logic out of monolithic UI containers into clean, testable service abstractions:
  - `IHardwareMonitorService` / `HardwareMonitorService`
  - `ILogger` / `Logger`
  - `IRemoteWebServer` / `RemoteWebServer`
  - `SecondaryWindowCoordinator` (for plot/gadget window lifecycles)
  - `TrayIconService` (split into interop, renderer, and orchestration layers)
  - `TreeRebuildCoalescer` (debounces and coalesces tree rebuild requests to optimize performance)
  - `PlotTrackingService`, `SensorSelectionService`, `WindowPlacementService`, `WindowChromeManager`, and `SensorColumnMeasurer`.

### 4. Thread Safety, Correctness, and Security Hardening
A comprehensive code review identified and fixed several critical concurrency issues:
- **`Computer.Close()` Race Condition Fix**: `CancelDeferredGroupRun()` now fully drains in-flight deferred tasks (`Task.WaitAll` via `WaitForDeferredGroupTasks`) before disposing token sources, stopping background threads from executing native API / OpCode calls during teardown.
- **Atomic State Transitions**: Wrapped `Open`/`OpenAsync`/`Close` in `Computer.cs` with a new `_openLock` to make checking/setting the `_open` state atomic and prevent concurrent initialization leaks.
- **UI Logger Synchronization**: Added a `_sync` lock inside `Logger` to protect concurrent reads/writes on `_sensors` and `_identifiers` arrays.
- **WinForms UI Thread Marshalling**: Legacy UI forms (`MainForm`, `SystemTray`, `SensorGadget`) were updated to marshal background `HardwareAdded`/`HardwareRemoved` events onto the UI `SynchronizationContext` to prevent unmarshaled cross-thread UI mutation.
- **Memory DIMM Discovery Serialization**: Standardized discovery ordering via `IHardwareDiscoveryTask.StartHardwareDiscovery()`, preventing duplicated or lost DIMM `HardwareAdded` announcements.
- **Web Server Optimization & Security**:
  - Fixed a vulnerability in `PasswordHasher.VerifyPbkdf2` where empty salt/hash segments could bypass authentication.
  - Reused a static `JsonSerializerOptions` in `RemoteWebServer` to restore metadata caching on the high-frequency `data.json` endpoint.

---

## Detailed Audit & Code Review Fixes

| Finding | Severity | Component | Resolution |
| :--- | :---: | :--- | :--- |
| **F1** | 🔴 Critical | `Computer.cs` | Drains in-flight deferred tasks during `Close()` to prevent races against native teardown. |
| **F2** | 🔴 Critical | `Computer.cs` | Extracted `OpenCore` and synchronized all open/close paths under `_openLock`. |
| **F3** | 🔴 Critical | `Logger.cs` | Added `_sync` mutex to synchronize sensor collections across threads safely without blocking file I/O. |
| **D1** | 🟠 Major | `GenericCpu.cs` | Deferred TSC task now receives a `CancellationToken` and is fully awaited on `Close()`. |
| **D2** | 🟠 Major | WinForms UI | Captures UI `SynchronizationContext` and dispatches deferred events to the UI thread. |
| **D4** | 🟠 Major | `Computer.cs` | Deferred-DIMM discovery is serialized via `StartHardwareDiscovery()` to avoid event duplication. |
| **D5** | 🔴 Critical | `PasswordHasher.cs` | Enforces non-empty salt/hash checks to prevent empty password bypass. |
| **D6** | 🟠 Major | `Computer.cs` | `CompleteDeferredGroupRunWhenRegistered` re-arms to wait for nested deferred tasks registered late. |
| **D9** | 🟡 Minor | `TrayIconService.cs` | Reuses existing GDI tray icons when tooltips/values are unchanged to avoid rendering churn. |
| **D10**| 🟡 Minor | `PlotTrackingService` | Eliminated redundant full-history sorts on every tick. |
| **D11**| 🟡 Minor | `MainWindowViewModel` | Cached sensor counts instead of traversing the entire tree on every update tick. |

---

## Verification & Testing
All tests and builds run successfully on `Release` for `x64` platform target.

### Automated Tests
Run the test suites with:
```powershell
# Run Library Unit Tests
dotnet test LibreHardwareMonitorLib.Tests/LibreHardwareMonitorLib.Tests.csproj -c Release -p:Platform=x64

# Run WinUI UI Tests
dotnet test LibreHardwareMonitor.Windows.WinUI.Tests/LibreHardwareMonitor.Windows.WinUI.Tests.csproj -c Release -p:Platform=x64
```

### Build Check
Ensure WinForms app compiles without errors:
```powershell
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -p:Platform=x64
```

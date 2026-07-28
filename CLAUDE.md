# TheOmenDen.PixelForge

A WinUI 3 desktop app: a **pixel art / sprite editor** with a **texture & asset pipeline** for
batch import, conversion, and processing.

## Stack

| | |
|---|---|
| Runtime | .NET 10 (`net10.0-windows10.0.26100.0`), C# 14 |
| UI | WinUI 3 / Windows App SDK 2.3.1, MSIX-packaged |
| MVVM | CommunityToolkit.Mvvm 8.4.2 (source-generated) |
| UI toolkit | CommunityToolkit.WinUI (Animations, Behaviors, Controls.Primitives, Converters, Extensions, Helpers, Triggers) |
| Rendering | Win2D (GPU canvas, app layer) · SkiaSharp (offscreen raster, Core layer) |
| Perf | CommunityToolkit.HighPerformance, DotNext (+ Threading/IO/Unsafe), RecyclableMemoryStream |
| LINQ | **ZLinq** (drop-in generator) — replaces System.Linq project-wide |
| Source gen | Riok.Mapperly (mapping), Meziantou.StronglyTypedId (ids) |
| Domain types | Meziantou.Framework: FullPath, ByteSize, Globbing, TemporaryDirectory · CsvHelper |
| Host | Microsoft.Extensions.Hosting — DI container + `appsettings.json`, built in `App.xaml.cs` |
| Logging | Serilog via `ILogger<T>`; Debug + Console + async rolling CLEF file |
| Tests | xUnit v3 (logic) + `winapp ui` automation (UI) |
| Packages | Central Package Management — versions live **only** in `Directory.Packages.props` |

## Layout

```
TheOmenDen.PixelForge.slnx
Directory.Build.props          modern-C# baseline for every project
Directory.Packages.props       all package versions (CPM)
.editorconfig                  style rules, enforced at build
src/
  TheOmenDen.PixelForge/       WinUI app — Views, ViewModels, XAML. Windows-only.
  TheOmenDen.PixelForge.Core/  net10.0 class library — image/palette/pipeline logic. NO Windows types.
tests/
  TheOmenDen.PixelForge.Core.Tests/  xUnit v3
  ui-tests.ps1                       winapp UI automation harness
```

**The boundary that matters:** anything testable without a window goes in `Core`. Sprite data,
palettes, format encoders/decoders, batch pipeline steps — all `Core`. The app project holds
Views, ViewModels, and platform glue only. If you find yourself wanting to reference
`Microsoft.UI.*` from `Core`, the logic is in the wrong project.

## Which library for what

Two renderers are referenced on purpose — do not mix them up:

- **Win2D** (`Microsoft.Graphics.Win2D`, app project) — the interactive canvas. GPU-backed,
  integrates with XAML via `CanvasControl` / `CanvasSwapChainPanel`. Use for anything the user
  sees and draws on.
- **SkiaSharp** (Core project) — offscreen raster work in the asset pipeline: decode, resample,
  quantize, encode. No window required, so it stays unit-testable. `SkiaSharp.NativeAssets.Win32`
  comes in transitively.

For pixel buffers, reach for `CommunityToolkit.HighPerformance` first (`Span2D<T>`, `Memory2D<T>`,
`ArrayPoolBufferWriter<T>`) — it covers most of what sprite manipulation needs. Drop to **DotNext**
for the things it doesn't: `DotNext.Unsafe` for raw pointer/interop work, `DotNext.Threading` for
async locks and coordination in the batch pipeline, `DotNext.IO` for buffered readers/writers.
Pool large streams with `RecyclableMemoryStream` rather than `new MemoryStream()` — the pipeline
processes many files and LOH churn is the predictable failure mode.

`CommunityToolkit.Diagnostics` (`Guard.IsNotNull`, `Guard.IsInRange`) for argument validation at
boundaries. `ColorHelper` for color-space conversion (HSL/HSV/CMYK) in Core.

Types over primitives — the Meziantou set exists to stop primitive obsession before it starts:

| Instead of | Use |
|---|---|
| `Guid`/`int` ids | `[StronglyTypedId]` — `SpriteId`, `PaletteId`, `AssetId` |
| `string` paths | `FullPath` — normalised, comparison-safe, avoids separator and casing pitfalls |
| `long` byte counts | `ByteSize` — formatting and parsing included |
| hand-rolled `*.png` matching | `Meziantou.Framework.Globbing` — gitignore-style patterns |
| `Path.GetTempPath()` + manual cleanup | `TemporaryDirectory` — disposable, self-deleting |

`CsvHelper` for palette and sprite-sheet index import/export.

**Prereleases in use:** SkiaSharp `4.151.0-rc.1.1`, RecyclableMemoryStream `4.0.0-preview`, and the
CommunityToolkit.WinUI `8.3.*-preview2` set. Each is the newest published build of its package.
Pin deliberately — don't "upgrade" one to an older stable without checking.

## Logging

Serilog is composed once, in `App.xaml.cs`. **Nothing else references Serilog** — inject
`ILogger<T>` from the host and log through `Microsoft.Extensions.Logging`. That is why `Core`
only references `Microsoft.Extensions.Logging.Abstractions`.

Two-stage init: a bootstrap logger is set before `InitializeComponent()` so a crash while
building the host is still recorded (a WinUI startup failure otherwise shows nothing at all),
then `AddSerilog` replaces it.

```csharp
// Message templates, never interpolation — `$"..."` destroys the structured properties
// and allocates even when the level is disabled.
logger.LogInformation("Exported {SpriteCount} sprites to {Path} in {Elapsed:0.0} ms",
    count, path, elapsed);
```

**Log location.** A packaged app's install directory is read-only, so the file sink path is
built in code from `ApplicationData.Current.LocalFolder`, not from `appsettings.json`. Serilog
swallows sink failures, so a relative path yields *no logs and no error*. Logs land in:

```
%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\logs\pixelforge-<date>.log
```

Format is CLEF (compact JSON), daily rolling, 14 files retained, 32 MB cap.

**Flushing is not optional.** `Sinks.Async` buffers in memory; `Log.CloseAndFlush()` on window
close is what persists the final events. Killing the process (`taskkill /F`) loses them — close
the window if you need the tail of a log.

Everything else — levels, overrides, Console/Debug sinks, enrichers, destructuring limits —
lives in `appsettings.json` and needs no rebuild to change. `Serilog.Expressions` is available
for filter expressions (`.Filter.ByExcluding("...")`); `Serilog.Sinks.EventLog` is referenced
for surfacing fatal errors to the Windows Event Log but is not wired up yet.

## Commands

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet test  tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
dotnet run   --project src/TheOmenDen.PixelForge        # packaged launch; prints the PID
.\tests\ui-tests.ps1 -AppPid <PID>                      # UI automation against the running app
```

Run the build and the tests after every code change.

## Standing rules

**0. Check the library before writing anything.**

Every package here was chosen because it is battle-tested and faster than what we'd write.
Before implementing anything non-trivial, enumerate the relevant package's public surface and
report what you found — **including when it genuinely lacks the method**, which is what justifies
custom code. Do not assume absence. The failure mode is concluding "it probably doesn't have
this" without looking, then shipping a worse version of code that already exists.

`FullPath` alone already provides `IsChildOf`, `MakePathRelativeTo`, `PathDifference`,
`IsSymbolicLink`, `CreateTempFile`, `GetKnownFolderPath`, an OS-case-aware `FullPathComparer`,
and a `FullPathJsonConverter`. Assume the same density everywhere else.

Assemblies live in `~/.nuget/packages/<id>/<version>/lib/<tfm>/`; `Expand-Archive` the `.nupkg`
for the README. For solution types, use the Roslyn navigator MCP tools (`get_public_api`,
`find_symbol`).

Rolling our own is the exception and needs a stated reason.

**1. ZLinq replaces System.Linq. Everywhere — not just hot paths.**

`ZLinq.DropInGenerator` source-generates higher-priority extension methods for arrays, spans,
`Memory<T>`, and `List<T>`, so ordinary `.Where(...).Select(...)` already binds to ZLinq with no
`.AsValueEnumerable()` and no call-site changes. It is enabled per assembly by
`[assembly: ZLinqDropIn("", DropInGenerateTypes.Collection)]` in each project's
`GlobalUsings.cs` — **a new project needs that line or it silently falls back to System.Linq.**

`ZLinqDropInTests` guards this: the tests assign chains to explicit `ValueEnumerable<...>` types,
so a fallback to System.Linq fails the build rather than quietly reintroducing allocations.

For `IEnumerable<T>` sources (not covered by `Collection`), chain explicitly:
`source.AsValueEnumerable().Where(...)`. Two ZLinq caveats that bite:

- `ValueEnumerable<T>` is a `ref struct` — it cannot cross `yield` or `await`. Materialise first
  (`ToArray()`, `ToArrayPool()`) if you need to.
- Each operator returns a different type, so you cannot reassign a chain to the same variable
  in a loop.

`ZLinq.FileSystem` and `ZLinq.Json` extend the same value-enumerable model to directory walks and
`JsonNode` trees — use them over `Directory.EnumerateFiles` + LINQ and over `JsonNode` recursion.

**2. Source generation over reflection, always.**


| Need | Use | Not |
|---|---|---|
| Object mapping | Riok.Mapperly (`[Mapper]`) | AutoMapper, hand-rolled reflection |
| MVVM plumbing | `[ObservableProperty]`, `[RelayCommand]` | manual `INotifyPropertyChanged` |
| Hot-path logging | `[LoggerMessage]` | `logger.LogX` in tight loops |
| JSON | `JsonSerializerContext` | reflection-based `JsonSerializer` |
| Ids | `[StronglyTypedId]` | raw `Guid`/`int`, primitive obsession |
| LINQ | ZLinq drop-in | System.Linq |

This is also what keeps `PublishTrimmed=true` viable. The known exception is
`Serilog.Settings.Configuration`, which resolves sinks reflectively — that is why sinks are
listed explicitly under `Using` in `appsettings.json`, and why a trimmed Release publish needs
testing before shipping.

**3. Never `SemaphoreSlim`. DotNext for async, `System.Threading.Lock` for sync.**

Banned at build time via `BannedSymbols.txt` (RS0030) — the error names the replacement inline.
DotNext.Threading covers every case with `ValueTask`-based, lower-allocation implementations:

| Instead of | Use | Notes |
|---|---|---|
| `SemaphoreSlim(1,1)` async mutex | `AsyncExclusiveLock` | `AcquireAsync` / `TryAcquireAsync` / `Release`, plus `StealAsync` |
| `SemaphoreSlim(n,n)` throttle | `AsyncSharedLock` | concurrency level + `Downgrade`; weak and strong acquisition |
| `ReaderWriterLockSlim` | `AsyncReaderWriterLock` | adds `TryOptimisticRead`, `UpgradeToWriteLockAsync`, `DowngradeFromWriteLock` |
| counting signal | `AsyncCounter` | `Increment` / `TryDecrement` / `WaitAsync` |
| `lock (someObject)` | `System.Threading.Lock` | .NET 9+ fast path; only for sections that never `await` |
| `Lazy<T>` with async factory | `AsyncLazy<T>` | `GetOrStartAsync`, resettable |
| `ManualResetEventSlim` / `CountdownEvent` / `Barrier` | `AsyncManualResetEvent` / `AsyncCountdownEvent` / `AsyncBarrier` | |

`AsyncLock` is the unified `using`-scoped wrapper — `AsyncLock.Exclusive()`, `.Semaphore(n)`,
`.ReadLock(rw)`, `.WriteLock(rw)`, `.Weak(shared)` — returning a disposable holder, which is
usually what you want at a call site.

Also in DotNext.Threading and worth reaching for before writing anything bespoke:
`AsyncTrigger`, `AsyncExchanger<T>`, `AsyncEventHub`, `TaskCompletionPipe`, `TaskQueue<T>`,
`PersistentChannel<,>`, `RandomAccessCache<,>`, `BoundedObjectPool<T>`,
`CancellationTokenMultiplexer`, and lease-based coordination under `DotNext.Threading.Leases`.

If a ban is genuinely wrong for a case, suppress RS0030 at that site with a reason — do not edit
the list.

**4. Every `Dispose` is idempotent. Derive from `DotNext.Disposable` — don't hand-roll the guard.**

Disposing twice must be a no-op, always. `DotNext.Disposable` already implements this correctly,
so inherit it rather than writing `private bool _disposed` again:

```csharp
public sealed class SpriteAtlas : Disposable
{
    private readonly RecyclableMemoryStream _pixels;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pixels.Dispose();
        }

        base.Dispose(disposing);   // CA2215
    }
}
```

`TryBeginDispose()` is the atomic guard — it returns `true` exactly once, so concurrent or
repeated `Dispose()` calls are safe without your own interlocked flag. Also provided:
`IsDisposed`, `IsDisposing`, `IsDisposingOrDisposed`, `CreateException()`,
`TrySetDisposedException(...)`, `GetDisposedTask()`, a wired finalizer, and static
`Dispose(IEnumerable<IDisposable>)` / `Dispose(ReadOnlySpan<>)` / `DisposeAsync(IEnumerable<>)`
for cleaning up a batch.

For async, declare `IAsyncDisposable` on your type and override `DisposeAsyncCore()` — the base
class's `DisposeAsync()` is `protected`, so it is opt-in.

Guard public members against use-after-dispose with the BCL helper:
`ObjectDisposedException.ThrowIf(IsDisposed, this);`

**Only when you cannot inherit** (a struct, or a type with a required base such as a
`ViewModel`), hand-roll it — and make the flag atomic, not a plain `bool`:

```csharp
private int _disposed;

public void Dispose()
{
    if (Interlocked.Exchange(ref _disposed, 1) is not 0)
    {
        return;   // already disposed — idempotent
    }

    _handle.Dispose();
    GC.SuppressFinalize(this);   // CA1816
}
```

CA1001/CA1063/CA1816/CA2213/CA2215/CA2216 are escalated to build errors, but they only check the
*shape* of the pattern — no analyzer verifies idempotency, which is exactly why the base class is
the rule rather than a suggestion.

## C# conventions

C# 14 throughout — this is enforced, not advisory. `TreatWarningsAsErrors` and
`EnforceCodeStyleInBuild` are on, so a style violation fails the build.

- **Primary constructors** for DI and dependency capture — no manual field assignment ceremony.
- **Collection expressions** `[]` everywhere, including spreads: `int[] all = [..a, ..b, 99];`
- **`record`** for DTOs and immutable data; **`readonly record struct`** for small value types
  (`Pixel`, `Rgba32`, `Point`) — these are hot in image loops, keep them off the heap.
- **`field` keyword** for properties needing validation or lazy init — never hand-roll a backing field.
- **Pattern matching** over type checks and null checks; switch expressions over switch statements.
- **`required` / `init`** to make illegal states unrepresentable.
- **`Span<T>` / `ReadOnlySpan<T>`** for pixel buffer slicing and parsing — zero allocation in hot paths.
- **Raw string literals** `"""` for embedded shaders, JSON, and multi-line text.
- **Extension blocks** (`extension(Sprite sprite) { ... }`) when adding members to external types.
- **Switch expressions** over switch statements; list and property patterns over manual indexing.
- **Expression-bodied members** wherever the body is a single expression — methods, properties,
  accessors, operators, indexers, lambdas.
- `var` only when the right-hand side makes the type obvious. Explicit type otherwise.
- File-scoped namespaces. Braces always. Private fields `_camelCase`.

**Formatting: Allman braces**, enforced by `.editorconfig` with `EnforceCodeStyleInBuild`, so a
violation is a build error rather than a review comment. Opening brace on its own line for every
construct — types, methods, control flow, initializers — with `else`/`catch`/`finally` on new
lines. 4-space indent, indented case contents and switch labels.

Don't reach for deeply nested patterns — `order is { A: { B: { C: "x" } } }` is worse than the
sequential check. Readability wins over cleverness.

## MVVM

`[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm. Never hand-write
`INotifyPropertyChanged` or `ICommand`.

```csharp
public sealed partial class CanvasViewModel(IPaletteStore palettes) : ObservableObject
{
    [ObservableProperty]
    public partial int ZoomLevel { get; set; } = 1;

    [RelayCommand]
    private async Task ExportAsync(CancellationToken ct) => /* ... */;
}
```

ViewModels stay free of `Microsoft.UI.*` types so they remain unit-testable.

## WinUI rules

- **Never run the packaged `.exe` directly** — use `dotnet run` or `winapp run`. It silently exits otherwise.
- **Never** use `AnyCPU`, add `<WindowsPackageType>None`, or delete `Package.appxmanifest`.
- `x:Bind` defaults to `OneTime` — add `Mode=OneWay` for anything that updates, or the UI stays blank.
- `TextBox` two-way bindings need `UpdateSourceTrigger=PropertyChanged`, otherwise the source only
  commits on `LostFocus` and UI automation `set-value` silently does nothing.
- **Every interactive control needs an `AutomationId`** — `ui-tests.ps1` fails the run without it,
  and it is also what makes the app usable with a screen reader.
- Prefer `x:Bind` over `Binding`; it is compiled and type-checked.
- Theming: use theme resources (`{ThemeResource ...}`), never hardcoded colors. Verify Light, Dark,
  and HighContrast.
- **`AdaptiveTrigger.MinWindowWidth` and `SizeChanged` report physical pixels, not DIPs.**
  Measured on a 144-DPI (150%) monitor: a 2040px-wide window reports `e.NewSize.Width` of 1704 —
  larger than the window's 1360 DIP width. So a 900 threshold does not fire until the window is
  down to 600 effective DIPs, and the same applies to `NavigationView`'s own Auto breakpoints.
  For a DIP-accurate breakpoint, divide by `XamlRoot.RasterizationScale` in a `SizeChanged`
  handler and call `VisualStateManager.GoToState` — see `Views/CanvasPage.xaml.cs`.
- The `NavigationView` settings entry is created by the control, so it ignores XAML
  `AutomationProperties` and is always addressable as `SettingsItem`.

## Packages

Add a package by declaring `<PackageVersion Include="X" Version="N" />` in
`Directory.Packages.props` and `<PackageReference Include="X" />` in the csproj. A `Version=`
attribute on a `PackageReference` is a restore error under CPM.

Prefer latest stable. Check with:
`dotnet package search <Name> --exact-match --source https://api.nuget.org/v3/index.json`

## Testing

Logic tests go in `Core.Tests` against `Core` — no UI thread, no dispatcher, fast. Test method
naming is `Method_Scenario_Expectation` (CA1707 is disabled under `tests/` for this reason).

UI behavior goes in `ui-tests.ps1` as `Test-UI` blocks. After a UI test run, **look at the
screenshots in `tests/ui-results/`** — UIA assertions pass while the app is visually broken
(clipping, overlap, wrong theme, truncated text).

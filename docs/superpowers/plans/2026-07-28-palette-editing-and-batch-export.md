# Palette Editing and Batch Sheet Export — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give PixelForge a palette page that previews and edits skin ramps with a live recoloured sprite, and a batch page that exports any subset of body and hair sheets — layered or flattened — with progress, cancellation and per-sheet failure reporting.

**Architecture:** Layered, and the layer boundary is the whole point. Everything testable without a window goes in `TheOmenDen.PixelForge.Core` (`net10.0`, no `Microsoft.UI.*`): recipe shape, bake orchestration, disk writing, CSV persistence, preview rendering. The app project holds Views, ViewModels and platform glue only. Core work lands first and is proved by xUnit v3 tests before any XAML exists.

**Tech Stack:** .NET 10 / C# 14 · WinUI 3 (Windows App SDK 2.3.1, MSIX) · CommunityToolkit.Mvvm source generators · SkiaSharp (offscreen raster) · DotNext (`Result<T,TError>`, `Optional<T>`, `Disposable`) · CommunityToolkit.HighPerformance (`Span2D<T>`) · RecyclableMemoryStream · CsvHelper · Meziantou.Framework (`FullPath`, `ByteSize`, `TemporaryDirectory`) · ZLinq · Serilog via `ILogger<T>`

**Spec:** `docs/superpowers/specs/2026-07-28-palette-editing-and-batch-export-design.md`

---

## Global Constraints

Every task's requirements implicitly include this section. Values are copied verbatim from `CLAUDE.md` and the spec.

- **Run after every code change:** `dotnet build TheOmenDen.PixelForge.slnx` and `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`. Both must pass before the commit step.
- **`var` everywhere, including built-in types** — `var i = 0`, not `int i = 0`. `.editorconfig` sets all three `csharp_style_var_*` rules to `true:warning`; with `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors` an explicit type is build error **IDE0007**, not a review note.
- **Allman braces.** Opening brace on its own line for every construct. 4-space indent. Enforced at build.
- **File-scoped namespaces. Braces always. Private fields `_camelCase`.**
- **One type per file, project-wide.** The file is named for the type it contains. This applies to `enum`, `record`, `readonly record struct`, `interface` and `partial` classes alike — a nested type stays with its parent, nothing else shares a file. Code blocks in this plan are grouped by task for readability; **the File Split Map below is authoritative for where each type actually lands.** This also covers files that already exist and already violate it — Task 0 fixes those before any new work starts.
- **ZLinq replaces System.Linq.** `ImmutableArray<T>` is *not* covered by the drop-in generator — call `.AsSpan()` first (a span is covered) or `.AsValueEnumerable()`. `ValueEnumerable<T>` is a `ref struct`: it cannot cross `yield` or `await`, and a chain cannot be reassigned to the same variable in a loop.
- **Never `SemaphoreSlim`, `Semaphore`, `ReaderWriterLockSlim`, `Monitor`, `Mutex`, `ManualResetEventSlim`, `CountdownEvent`, `Barrier`, `Lazy<T>`.** Banned at build time via `BannedSymbols.txt` (RS0030). `Parallel.ForEachAsync` is **not** banned and is what this plan uses.
- **Expected failure is a return value:** `Result<T, TError>` with `TError : struct, Enum`. Failure enums are numbered from 1 so `default` is never a real failure. `Guard.IsNotNull` stays for argument-null at public boundaries — a null there is a caller bug, not an expected outcome.
- **Every `Dispose` is idempotent.** Derive from `DotNext.Disposable`; call `base.Dispose(disposing)` (CA2215). Hand-roll only when a base class is required, and then use `Interlocked.Exchange`, never a plain `bool`.
- **One `RecyclableMemoryStreamManager`:** `PooledStreams.Manager`. `ToArray()` throws by design — use `WriteTo(stream)`, `GetBuffer().AsSpan(0, (int)Length)`, or `GetReadOnlySequence()`.
- **Never `SKBitmap.Pixels` in production code** — it allocates an `SKColor[]` per call (828 KiB for a source partial, straight to the LOH). Use `PeekPixels()` / `GetPixelSpan()`. Tests may use `.Pixels`; the existing `SheetBakerTests` does.
- **Package versions live only in `Directory.Packages.props`.** A `Version=` attribute on a `PackageReference` is a restore error under CPM.
- **Source generation over reflection:** `[ObservableProperty]`, `[RelayCommand]`, `[LoggerMessage]`, `JsonSerializerContext`. Reflection-based `JsonSerializer.Serialize<T>`/`Deserialize<T>` overloads are banned.
- **Message templates, never interpolation,** in log calls.
- **Every interactive control needs an `AutomationId`** — `ui-tests.ps1` fails the run without it.
- **`x:Bind` defaults to `OneTime`** — add `Mode=OneWay` for anything that updates. `TextBox` two-way bindings need `UpdateSourceTrigger=PropertyChanged` or UIA `set-value` silently does nothing.
- **Theme resources only,** never hardcoded colors. Define Light, Dark and HighContrast; HighContrast permits only the 8 system colour brushes.
- **No `Co-Authored-By` trailer.** This repo has no `.claude/settings.json`, so `attribution.commit` is unset.

---

## Lens Notes

Three lenses were applied to the spec before this plan. Each produced a concrete change, recorded here so a reader knows why the plan diverges from the spec.

**Ponytail (laziness) — four things removed:**

1. **`RampCsv` merged into `RampStore`.** The spec had two types. `RampStore` becomes one file with static `Read(TextReader)` / `Write(TextWriter)` plus instance `Load()` / `Save()`. Still disk-free testable; one fewer file.
2. **`FlattenedSheets` deleted.** `CrossProduct` becomes `RoostSheets.Flattened(bodies, hair)` — same concern (which sheets we ship), and `RoostSheets` already exists to be the one table of art selection.
3. **All three service interfaces dropped.** `ISourcePackService`, `IRampService`, `IPickerService` each had exactly one implementation and no mocking consumer — Core.Tests tests Core, not ViewModels. Register the concrete classes. The existing `IThemeService` is left alone; its existing shape is not a reason to add three more.
4. **`PickerService` holds no state** beyond a `WindowId` set once at startup.

**2d-games — one thing added:**

5. **A sheet index manifest.** Exporting 79 nameless `.webp` files leaves the consumer to reverse-engineer that output rows 3–5 are `idle` across three facings. `CLAUDE.md` already names CsvHelper for "sprite-sheet index import/export", so the manifest is a CSV emitted beside the sheets (Task 5). This is the atlas-metadata half of an atlas export; without it the atlas is not self-describing.
6. **The mode selector states the real tradeoff in its UI copy.** Layered keeps hair a separate texture, so a hairstyle swaps at runtime without rebaking and z-order stays under the engine's control. Flattened is one texture per body+hair pair — fewer draw calls, no runtime swap. That is atlas economics, and the user needs it visible to pick correctly (Task 17).

**Modern C# (C# 14) — two adopted, one rejected:**

7. **`extension(SkinRamp)` block** for the CSV row conversion, instead of a static helper class (Task 6).
8. **`field` keyword** for validated ViewModel properties (Tasks 13, 16).
9. **Rejected: "don't use `var` when the type is not obvious."** This directly contradicts the project's `.editorconfig`, which makes an explicit type build error IDE0007. Project instructions override the skill. **`var` everywhere.**

**Toolkit-first — five packages replace hand-rolled UI code.** Every version below is verified published on nuget.org and matches the `8.3.260402-preview2` train the seven existing toolkit packages already sit on. This is standing rule 0 applied to the app layer: the first draft of this plan hand-rolled four of these five.

| Package | Type used | Hand-rolled code it deletes |
|---|---|---|
| `Controls.ColorPicker` | `ColorPickerButton` | **The whole swatch-button apparatus.** The first draft built a `Button` + `Flyout` + `ColorPicker`, carried the step index in a `Tag`, and routed a `ColorChanged` handler back through a `SetStepColor` method on the view model. `ColorPickerButton` is that control, with a two-way `SelectedColor`. Deletes the handler, the `Tag` plumbing and the view-model method. |
| `Controls.Segmented` | `Segmented` / `SegmentedItem` | `RadioButtons` for the three export modes. The catalogue's own family note is explicit: Segmented is for "2-5 mutually-exclusive short toggles", which is exactly a mode switch. |
| `Behaviors` | `StackedNotificationsBehavior`, `Notification`, `AutoSelectBehavior` | A `StatusMessage` string plus a `HasStatus` bool plus a manually-bound `InfoBar.IsOpen`, **in two view models**. The behavior also queues multiple messages and auto-dismisses on a `Duration` — neither of which the hand-rolled single string did at all, and per-sheet failure reporting in a 79-sheet run genuinely needs queueing. `AutoSelectBehavior` selects hex text on focus. |
| `Collections` | `AdvancedCollectionView`, `SortDescription` | `PaletteViewModel.RefreshRamps` — a clear-and-rebuild that had to save the selected ramp's name and re-select it afterwards. Sorting built-ins ahead of customs becomes a `SortDescription` instead of construction order, and the selection-restore dance goes away. |
| `Controls.Sizers` | `PropertySizer`, `GridSplitter` | The hardcoded `Width="320"` ramp-list column, and the fixed 50/50 split between the two batch lists. Six lines of XAML instead of a magic number and a drag handler. |

`Behaviors` is **already referenced** by the app project — the first draft simply never used it.

**One rule interpretation this forces.** `ColorPickerButton.SelectedColor` is a `Windows.UI.Color`, so binding it two-way puts that type on `RampStepViewModel`. `CLAUDE.md` says view models stay free of **`Microsoft.UI.*`** types so they remain unit-testable; `Windows.UI.Color` is a WinRT struct of four bytes with no dispatcher, no XAML dependency and no window affinity, so it does not compromise testability and does not violate that rule as written. The alternative — keeping the view model purely `SKColor` — costs the event handler and view-model method that `ColorPickerButton` exists to delete. Taking the struct.

---

## File Split Map

**Authoritative.** Where a task's code block declares several types, this table says which file each one goes in. Namespace is unchanged by the split — only the file boundary moves.

### Existing files to split (Task 0)

| Today | Becomes |
|---|---|
| `Core/Palettes/SkinRamp.cs` | `Palettes/SkinRamp.cs`, `Palettes/SkinRamps.cs` |
| `Core/Spritesheets/SheetLayout.cs` | `Spritesheets/AnimationClip.cs`, `Spritesheets/SheetLayout.cs` |
| `Core/Baking/SheetRecipe.cs` | `Baking/SheetRecipe.cs`, `Baking/RecipeBaker.cs` |
| `Core/Baking/RoostSheets.cs` | `Baking/ElementsPack.cs`, `Baking/SourcePacks.cs`, `Baking/RoostSheets.cs` |

### New files by task

| Task | Type | File |
|---|---|---|
| 1 | `SheetRecipe.Overlays` (member) | `Core/Baking/SheetRecipe.cs` (existing, post-split) |
| 1 | `RecipeBaker.Finish` / `ApplyOverlays` | `Core/Baking/RecipeBaker.cs` (post-split) |
| 2 | `RoostSheets.Flattened` | `Core/Baking/RoostSheets.cs` (post-split) |
| 3 | `SheetWriter` | `Core/Baking/SheetWriter.cs` |
| 4 | `BakeProgress` | `Core/Baking/BakeProgress.cs` |
| 4 | `BatchSummary` | `Core/Baking/BatchSummary.cs` |
| 4 | `BatchBaker` | `Core/Baking/BatchBaker.cs` |
| 5 | `SheetIndexRow` | `Core/Spritesheets/SheetIndexRow.cs` |
| 5 | `SheetIndex` | `Core/Spritesheets/SheetIndex.cs` |
| 6 | `RampFailure` | `Core/Palettes/RampFailure.cs` |
| 6 | `RampRow` | `Core/Palettes/RampRow.cs` |
| 6 | `RampConversions` | `Core/Palettes/RampConversions.cs` |
| 6 | `RampStore` | `Core/Palettes/RampStore.cs` |
| 7 | `PalettePreview` | `Core/Palettes/PalettePreview.cs` |
| 7 | `RecipeBaker.AssembleLayers` | `Core/Baking/RecipeBaker.cs` (post-split) |
| 8 | `AppPaths` | `App/Services/AppPaths.cs` |
| 9 | `PackSettings` | `App/Services/PackSettings.cs` |
| 9 | `PackSettingsContext` | `App/Services/PackSettingsContext.cs` |
| 9 | `SourcePackService` | `App/Services/SourcePackService.cs` |
| 10 | `PickerService` | `App/Services/PickerService.cs` |
| 11 | `RampService` | `App/Services/RampService.cs` |
| 13 | `RampStepViewModel` | `App/ViewModels/RampStepViewModel.cs` |
| 13 | `StatusLevel` | `App/ViewModels/StatusLevel.cs` |
| 13 | `StatusNotice` | `App/ViewModels/StatusNotice.cs` |
| 13 | `PaletteViewModel` | `App/ViewModels/PaletteViewModel.cs` |
| 14 | *(none — package only)* | `SkiaSharp.Views.WinUI` provides `ToWriteableBitmap()` |
| 16 | `ExportMode` | `App/ViewModels/ExportMode.cs` |
| 16 | `SheetSelectionItem` | `App/ViewModels/SheetSelectionItem.cs` |
| 16 | `BatchExportViewModel` | `App/ViewModels/BatchExportViewModel.cs` |

Nested types stay with their parent — that is the one exception to one-type-per-file. (Task 14 no longer creates a type at all; see its rewrite.)

---

## Task 0: Split existing multi-type files

Pure mechanical refactor, no behaviour change. Doing it first means every later task edits a file that already holds exactly one type, so no task has to both add a feature and untangle a file.

**Files:** the four in the table above.

- [ ] **Step 1: Record the baseline**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Note the passing test count. It must be identical after the split — that is the whole verification for this task.

- [ ] **Step 2: Split `SkinRamp.cs`**

Move `SkinRamps` (the static class holding `StepCount`, `Source`, `All`, `Human`, `FromHex`, `Ramp`) into a new `src/TheOmenDen.PixelForge.Core/Palettes/SkinRamps.cs`, keeping `namespace TheOmenDen.PixelForge.Core.Palettes;` and carrying its `using` directives (`System.Collections.Immutable`, `ColorHelper`, `SkiaSharp`). `SkinRamp.cs` keeps only the `SkinRamp` record and needs `System.Collections.Frozen`, `System.Collections.Immutable`, `CommunityToolkit.Diagnostics`, `SkiaSharp`.

- [ ] **Step 3: Split `SheetLayout.cs`**

Move the `AnimationClip` readonly record struct into `src/TheOmenDen.PixelForge.Core/Spritesheets/AnimationClip.cs`. `SheetLayout.cs` keeps the static class and its `System.Collections.Immutable` using.

- [ ] **Step 4: Split `SheetRecipe.cs`**

Move the `RecipeBaker` static class into `src/TheOmenDen.PixelForge.Core/Baking/RecipeBaker.cs`. It needs `CommunityToolkit.Diagnostics`, `DotNext`, `Microsoft.IO`, `SkiaSharp`, `TheOmenDen.PixelForge.Core.Palettes`. `SheetRecipe.cs` keeps the record and needs `System.Collections.Immutable`, `DotNext`, `Meziantou.Framework`, `TheOmenDen.PixelForge.Core.Palettes`.

**This is the file Tasks 1 and 7 modify.** Both of those tasks name `SheetRecipe.cs` for changes that are actually to `RecipeBaker` — after this split, `RecipeBaker.Finish`, `ApplyOverlays`, `Bake` and `AssembleLayers` all live in `RecipeBaker.cs`. Only the `Overlays` property in Task 1 belongs in `SheetRecipe.cs`.

- [ ] **Step 5: Split `RoostSheets.cs`**

Move `ElementsPack` into `src/TheOmenDen.PixelForge.Core/Baking/ElementsPack.cs` and `SourcePacks` into `src/TheOmenDen.PixelForge.Core/Baking/SourcePacks.cs`. `SourcePacks` needs `CommunityToolkit.Diagnostics` (for `ThrowHelper`) and `Meziantou.Framework`. `RoostSheets.cs` keeps the static class and needs `System.Collections.Immutable`, `CommunityToolkit.Diagnostics`, `Meziantou.Framework`, `TheOmenDen.PixelForge.Core.Palettes`.

- [ ] **Step 6: Build and confirm the baseline is unchanged**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: build succeeds with zero warnings, and the **same** test count passes as in Step 1. No test file changes — nothing moved namespace, so no `using` in the test project changes either.

- [ ] **Step 7: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core
git commit -m "refactor(core): one type per file"
```

---

## File Structure

### Created

| File | Responsibility |
|---|---|
| `src/…Core/Baking/SheetWriter.cs` | Write one baked stream to `<dir>/<name>.webp`; return `ByteSize` |
| `src/…Core/Baking/BatchBaker.cs` | Bounded-parallel run over recipes; progress; cancellation; summary |
| `src/…Core/Spritesheets/SheetIndex.cs` | Emit the clip→row manifest CSV that makes the atlas self-describing |
| `src/…Core/Palettes/RampFailure.cs` | Failure enum for ramp persistence |
| `src/…Core/Palettes/RampStore.cs` | Ramp CSV read/write + LocalState load/save |
| `src/…Core/Palettes/PalettePreview.cs` | Cache curated bitmap once; recolour + upscale the idle row per ramp |
| `src/…PixelForge/Services/AppPaths.cs` | The one packaged/unpackaged LocalState branch |
| `src/…PixelForge/Services/PackSettings.cs` | Three pack dirs + `JsonSerializerContext` |
| `src/…PixelForge/Services/SourcePackService.cs` | Hold/persist pack dirs; `Changed` event |
| `src/…PixelForge/Services/PickerService.cs` | `Microsoft.Windows.Storage.Pickers` wrapper |
| `src/…PixelForge/Services/RampService.cs` | Built-ins + customs; uniqueness; import/export |
| `src/…PixelForge/ViewModels/PaletteViewModel.cs` | Ramp list, step editing, commands |
| `src/…PixelForge/ViewModels/BatchExportViewModel.cs` | Sheet selection, mode, export/cancel, progress |
| *(removed)* | Task 14 uses `SkiaSharp.Views.WinUI`'s `ToWriteableBitmap()` instead of a custom bridge |
| `src/…PixelForge/Views/PalettePage.xaml{,.cs}` | Palette UI |
| `tests/…Core.Tests/Baking/SheetWriterTests.cs` | |
| `tests/…Core.Tests/Baking/BatchBakerTests.cs` | |
| `tests/…Core.Tests/Baking/RecipeBakerOverlayTests.cs` | The flatten guard |
| `tests/…Core.Tests/Spritesheets/SheetIndexTests.cs` | |
| `tests/…Core.Tests/Palettes/RampStoreTests.cs` | |
| `tests/…Core.Tests/Palettes/PalettePreviewTests.cs` | |

### Modified

| File | Change |
|---|---|
| `src/…Core/Baking/SheetRecipe.cs` | Add `Overlays`; composite after recolour in `RecipeBaker.Finish` |
| `src/…Core/Baking/BakeFailure.cs` | Append `OutputDirectoryUnavailable`, `OutputWriteFailed` |
| `src/…Core/Baking/RoostSheets.cs` | Add `Flattened(bodies, hair)` |
| `src/…PixelForge/App.xaml.cs` | `LogDirectory` → `AppPaths`; register new services |
| `src/…PixelForge/MainWindow.xaml{,.cs}` | Palette nav item + route; cache `WindowId` |
| `src/…PixelForge/Views/PipelinePage.xaml{,.cs}` | Rewritten as batch export |
| `src/…PixelForge/Views/SettingsPage.xaml{,.cs}` | `SettingsCard` rows for pack dirs |
| `src/…PixelForge/ViewModels/SettingsViewModel.cs` | Pack paths + browse commands |
| `Directory.Packages.props` | Add `CommunityToolkit.WinUI.Controls.SettingsControls` |
| `tests/ui-tests.ps1` | Palette + batch `Test-UI` blocks |

---

## Phase A — Core: flatten correctness and sheet output

No UI dependency. Everything here is proved by tests before a single XAML file changes.

### Task 1: Overlays composite after the recolour

This is the correctness fix the whole flatten feature rests on. `RoostSheets` documents that `hair1` and `hat4` use skin-ramp hexes as hair and trim, so compositing hair *before* the recolour would rewrite those pixels. Write the failing test first — it is the guard.

**Files:**
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/SheetRecipe.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RecipeBakerOverlayTests.cs` (create)

**Interfaces:**
- Consumes: `SheetBaker.Assemble`, `SheetBaker.Recolor`, `SheetBaker.ToCanonical`, `SheetBaker.Curate`, `LosslessWebp.EncodeVerified`, `SkinRamps.Source`, `SheetLayout.*` — all existing.
- Produces: `SheetRecipe.Overlays` (`ImmutableArray<FullPath>`, defaults `[]`). Tasks 2, 4 and 5 rely on this name and type.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RecipeBakerOverlayTests.cs`:

```csharp
using System.Collections.Immutable;
using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// Overlays are drawn after the recolour, which is what makes flattening safe. RoostSheets
/// names hair1 and hat4 as partials that use skin-ramp hexes as hair and trim: composite
/// those before the substitution and the recolour rewrites them.
/// </summary>
public sealed class RecipeBakerOverlayTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    /// <summary>A source-geometry partial filled with one colour, written as PNG.</summary>
    private FullPath WritePartial(string name, SKColor fill)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var pixels = bitmap.Pixels;
        Array.Fill(pixels, fill);
        bitmap.Pixels = pixels;

        var path = _directory.FullPath / name;

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    /// <summary>
    /// The body is painted in a source ramp step, the overlay in the SAME step. After the bake
    /// the body must have moved to the target ramp and the overlay must not have moved at all.
    /// </summary>
    [Fact]
    public void Bake_LeavesOverlayColoursUntouched_WhenTheyCollideWithTheSourceRamp()
    {
        var collidingStep = SkinRamps.Source.Steps[3];
        var target = SkinRamps.All[4];

        var body = WritePartial("body.png", collidingStep);
        var overlay = WritePartial("overlay.png", collidingStep);

        // The overlay covers the whole sheet, so every visible pixel comes from it.
        var recipe = new SheetRecipe
        {
            Name = "collide",
            Layers = [body],
            Recolor = target,
            Overlays = [overlay],
        };

        var baked = RecipeBaker.Bake(recipe);

        Assert.True(baked.IsSuccessful, $"bake failed with {baked.Error}");

        using var stream = baked.Value;
        using var decoded = SKBitmap.Decode(
            stream.GetBuffer().AsSpan(0, (int)stream.Length),
            new SKImageInfo(SheetLayout.OutputWidth, SheetLayout.OutputHeight,
                SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var actual = decoded.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2);

        Assert.Equal(collidingStep, actual);
        Assert.NotEqual(target.Steps[3], actual);
    }

    /// <summary>An overlay of the wrong geometry is bad input, not a bug.</summary>
    [Fact]
    public void Bake_ReportsLayerGeometryMismatch_WhenAnOverlayIsTheWrongSize()
    {
        var body = WritePartial("body.png", SkinRamps.Source.Steps[0]);

        using var small = new SKBitmap(new SKImageInfo(48, 48, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var wrong = _directory.FullPath / "wrong.png";

        using (var stream = File.Create(wrong.Value))
        using (var image = SKImage.FromBitmap(small))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            data.SaveTo(stream);
        }

        var result = RecipeBaker.Bake(new SheetRecipe
        {
            Name = "bad-overlay",
            Layers = [body],
            Overlays = [wrong],
        });

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.LayerGeometryMismatch, result.Error);
    }

    /// <summary>No overlays is the existing layered path and must be unchanged.</summary>
    [Fact]
    public void Bake_RecoloursNormally_WhenThereAreNoOverlays()
    {
        var target = SkinRamps.All[4];
        var body = WritePartial("body.png", SkinRamps.Source.Steps[3]);

        var baked = RecipeBaker.Bake(new SheetRecipe
        {
            Name = "plain",
            Layers = [body],
            Recolor = target,
        });

        Assert.True(baked.IsSuccessful, $"bake failed with {baked.Error}");

        using var stream = baked.Value;
        using var decoded = SKBitmap.Decode(
            stream.GetBuffer().AsSpan(0, (int)stream.Length),
            new SKImageInfo(SheetLayout.OutputWidth, SheetLayout.OutputHeight,
                SKColorType.Rgba8888, SKAlphaType.Unpremul));

        Assert.Equal(target.Steps[3], decoded.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RecipeBakerOverlayTests"`

Expected: **compile error** — `SheetRecipe` has no `Overlays` property. That is the correct first failure.

- [ ] **Step 3: Add `Overlays` to `SheetRecipe`**

In `src/TheOmenDen.PixelForge.Core/Baking/SheetRecipe.cs`, add to the record after `Recolor`:

```csharp
    /// <summary>
    /// Layers drawn <em>after</em> the recolour, so their authored colours survive it. Empty
    /// for layered output.
    /// <para>
    /// This is what makes flattening safe. <see cref="RoostSheets"/> records that some hair
    /// partials legitimately use skin-ramp hexes as hair and trim; compositing those before
    /// the substitution would recolour them along with the face.
    /// </para>
    /// </summary>
    public ImmutableArray<FullPath> Overlays { get; init; } = [];
```

- [ ] **Step 4: Composite overlays in `RecipeBaker.Finish`**

Replace `RecipeBaker.Finish` in the same file. The order is recolour → overlay → curate → encode:

```csharp
    private static Result<RecyclableMemoryStream, BakeFailure> Finish(
        SKBitmap assembled,
        SheetRecipe recipe)
    {
        SKBitmap? toned = null;
        SKBitmap? overlaid = null;

        try
        {
            var subject = assembled;

            if (recipe.Recolor.TryGet(out var ramp))
            {
                var recolored = SheetBaker.Recolor(subject, ramp.SubstitutionFrom(SkinRamps.Source));

                if (!recolored.TryGet(out toned))
                {
                    return new(recolored.Error);
                }

                subject = toned;
            }

            if (!recipe.Overlays.IsDefaultOrEmpty)
            {
                var composited = ApplyOverlays(subject, recipe.Overlays);

                if (!composited.TryGet(out overlaid))
                {
                    return new(composited.Error);
                }

                subject = overlaid;
            }

            var curation = SheetBaker.Curate(subject);

            if (!curation.TryGet(out var curated))
            {
                return new(curation.Error);
            }

            using (curated)
            {
                return LosslessWebp.EncodeVerified(curated);
            }
        }
        finally
        {
            toned?.Dispose();
            overlaid?.Dispose();
        }
    }

    /// <summary>
    /// Draws overlay partials over an already-recoloured assembly. Compositing happens on a
    /// premultiplied surface because that is what Skia draws into, then converts back to the
    /// canonical unpremultiplied format — the same round trip <see cref="SheetBaker.Assemble"/>
    /// makes, and exact for the strictly binary alpha this art uses.
    /// </summary>
    private static Result<SKBitmap, BakeFailure> ApplyOverlays(
        SKBitmap subject,
        ImmutableArray<FullPath> overlays)
    {
        var loaded = new List<SKBitmap>(overlays.Length);

        try
        {
            foreach (var path in overlays)
            {
                if (!File.Exists(path.Value))
                {
                    return new(BakeFailure.LayerNotFound);
                }

                var overlay = SKBitmap.Decode(path.Value);

                if (overlay is null)
                {
                    return new(BakeFailure.LayerUnreadable);
                }

                loaded.Add(overlay);

                if (overlay.Width != SheetLayout.SourceWidth || overlay.Height != SheetLayout.SourceHeight)
                {
                    return new(BakeFailure.LayerGeometryMismatch);
                }
            }

            using var composited = new SKBitmap(new SKImageInfo(
                SheetLayout.SourceWidth, SheetLayout.SourceHeight,
                SKColorType.Rgba8888, SKAlphaType.Premul));

            using (var canvas = new SKCanvas(composited))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(subject, 0, 0, PixelExact);

                foreach (var overlay in loaded)
                {
                    canvas.DrawBitmap(overlay, 0, 0, PixelExact);
                }
            }

            return SheetBaker.ToCanonical(composited);
        }
        finally
        {
            foreach (var overlay in loaded)
            {
                overlay.Dispose();
            }
        }
    }

    /// <summary>Nearest with no mipmapping — a scaled draw must never blur pixel art.</summary>
    private static SKSamplingOptions PixelExact => new(SKFilterMode.Nearest, SKMipmapMode.None);
```

Update the single call site in `Bake` from `Finish(assembled, recipe.Recolor)` to `Finish(assembled, recipe)`.

Add `using TheOmenDen.PixelForge.Core.Spritesheets;` to the file's usings for `SheetLayout`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RecipeBakerOverlayTests"`

Expected: 3 passed.

- [ ] **Step 6: Run the full build and test suite**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: build succeeds, all tests pass. The existing `SheetBakerTests` and `LosslessWebpTests` must be unaffected — `Overlays` has a default, so every existing construction site still compiles.

- [ ] **Step 7: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Baking/SheetRecipe.cs tests/TheOmenDen.PixelForge.Core.Tests/Baking/RecipeBakerOverlayTests.cs
git commit -m "feat(core): composite overlays after the recolour so flattening is safe"
```

---

### Task 2: Flattened body × hair recipes

**Files:**
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/RoostSheets.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RoostSheetsTests.cs` (create)

**Interfaces:**
- Consumes: `SheetRecipe.Overlays` from Task 1; existing `RoostSheets.Bodies`, `RoostSheets.Hair`.
- Produces: `RoostSheets.Flattened(IReadOnlyList<SheetRecipe> bodies, IReadOnlyList<SheetRecipe> hair)` → `ImmutableArray<SheetRecipe>`. Tasks 16 and 17 call this.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RoostSheetsTests.cs`:

```csharp
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

public sealed class RoostSheetsTests
{
    private static SourcePacks Packs { get; } = new()
    {
        CoreAssets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "core")),
        Expansion1Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "x1")),
        Expansion2Assets = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "x2")),
    };

    [Fact]
    public void Flattened_ProducesOneRecipePerBodyAndHairPair()
    {
        var bodies = RoostSheets.Bodies(Packs);
        var hair = RoostSheets.Hair(Packs);

        var flattened = RoostSheets.Flattened(bodies, hair);

        Assert.Equal(bodies.Length * hair.Length, flattened.Length);
    }

    [Fact]
    public void Flattened_NamesEachSheetForItsBodyAndHair()
    {
        var bodies = RoostSheets.Bodies(Packs);
        var hair = RoostSheets.Hair(Packs);

        var flattened = RoostSheets.Flattened(bodies, hair);

        Assert.Equal("body-01_hair-01", flattened[0].Name);
        Assert.Equal($"{bodies[^1].Name}_{hair[^1].Name}", flattened[^1].Name);
    }

    /// <summary>
    /// The body's layers and ramp carry over; the hair becomes an overlay so the recolour
    /// cannot reach it.
    /// </summary>
    [Fact]
    public void Flattened_CarriesTheBodyRamp_AndPutsHairInOverlays()
    {
        var bodies = RoostSheets.Bodies(Packs);
        var hair = RoostSheets.Hair(Packs);

        var flattened = RoostSheets.Flattened(bodies, hair);

        Assert.Equal(bodies[0].Layers, flattened[0].Layers);
        Assert.Equal(hair[0].Layers, flattened[0].Overlays);

        Assert.True(flattened[0].Recolor.TryGet(out var ramp));
        Assert.Equal(SkinRamps.All[0].Name, ramp.Name);
    }

    [Fact]
    public void Flattened_ReturnsEmpty_WhenEitherSideIsEmpty()
    {
        var bodies = RoostSheets.Bodies(Packs);

        Assert.Empty(RoostSheets.Flattened(bodies, []));
        Assert.Empty(RoostSheets.Flattened([], RoostSheets.Hair(Packs)));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RoostSheetsTests"`

Expected: **compile error** — `RoostSheets.Flattened` does not exist.

- [ ] **Step 3: Add `Flattened` to `RoostSheets`**

Append to `RoostSheets` in `src/TheOmenDen.PixelForge.Core/Baking/RoostSheets.cs`:

```csharp
    /// <summary>
    /// The cross product of bodies and hair, composited into one sheet each.
    /// <para>
    /// Hair goes in <see cref="SheetRecipe.Overlays"/>, not <see cref="SheetRecipe.Layers"/>,
    /// so it is drawn after the body's recolour and keeps its authored colour — see the
    /// collision cases named on <see cref="BodyLayers"/>.
    /// </para>
    /// <para>
    /// Flattening trades runtime flexibility for draw calls: a flattened pair is one texture,
    /// but the hairstyle can no longer be swapped without rebaking. The layered sheets from
    /// <see cref="All"/> remain the Corvus contract.
    /// </para>
    /// </summary>
    public static ImmutableArray<SheetRecipe> Flattened(
        IReadOnlyList<SheetRecipe> bodies,
        IReadOnlyList<SheetRecipe> hair)
    {
        Guard.IsNotNull(bodies);
        Guard.IsNotNull(hair);

        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>(bodies.Count * hair.Count);

        foreach (var body in bodies)
        {
            foreach (var style in hair)
            {
                recipes.Add(new()
                {
                    Name = $"{body.Name}_{style.Name}",
                    Layers = body.Layers,
                    Recolor = body.Recolor,
                    Overlays = style.Layers,
                });
            }
        }

        return recipes.ToImmutable();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RoostSheetsTests"`

Expected: 4 passed.

- [ ] **Step 5: Run the full build and test suite**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Baking/RoostSheets.cs tests/TheOmenDen.PixelForge.Core.Tests/Baking/RoostSheetsTests.cs
git commit -m "feat(core): add flattened body x hair recipes"
```

---

### Task 3: Write a baked sheet to disk

**Files:**
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/BakeFailure.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Baking/SheetWriter.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/SheetWriterTests.cs`

**Interfaces:**
- Consumes: `PooledStreams`, `BakeFailure`, `FullPath`, `ByteSize`.
- Produces: `SheetWriter.Write(FullPath directory, string name, RecyclableMemoryStream sheet)` → `Result<ByteSize, BakeFailure>`. Task 4 calls this.
- Produces: `BakeFailure.OutputDirectoryUnavailable`, `BakeFailure.OutputWriteFailed`.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/SheetWriterTests.cs`:

```csharp
using Meziantou.Framework;
using Microsoft.IO;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Buffers;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

public sealed class SheetWriterTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private static RecyclableMemoryStream StreamOf(params byte[] bytes)
    {
        var stream = PooledStreams.New("test");

        stream.Write(bytes);
        stream.Position = 0;

        return stream;
    }

    [Fact]
    public void Write_PutsTheStreamBytesOnDisk_UnderTheRecipeName()
    {
        using var sheet = StreamOf(1, 2, 3, 4, 5);

        var result = SheetWriter.Write(_directory.FullPath, "body-01", sheet);

        Assert.True(result.IsSuccessful, $"write failed with {result.Error}");

        var written = _directory.FullPath / "body-01.webp";

        Assert.True(File.Exists(written.Value));
        Assert.Equal<byte[]>([1, 2, 3, 4, 5], File.ReadAllBytes(written.Value));
    }

    [Fact]
    public void Write_ReturnsTheNumberOfBytesWritten()
    {
        using var sheet = StreamOf(1, 2, 3, 4, 5);

        var result = SheetWriter.Write(_directory.FullPath, "body-01", sheet);

        Assert.True(result.IsSuccessful);
        Assert.Equal(5L, result.Value.Value);
    }

    /// <summary>
    /// Writing must not depend on where the caller left the position. A verified encode rewinds,
    /// but nothing in the type system says so.
    /// </summary>
    [Fact]
    public void Write_WritesTheWholeStream_RegardlessOfPosition()
    {
        using var sheet = StreamOf(1, 2, 3, 4, 5);

        sheet.Position = 3;

        var result = SheetWriter.Write(_directory.FullPath, "body-02", sheet);

        Assert.True(result.IsSuccessful);
        Assert.Equal(5L, result.Value.Value);
    }

    [Fact]
    public void Write_ReportsOutputDirectoryUnavailable_WhenTheDirectoryIsMissing()
    {
        using var sheet = StreamOf(1, 2, 3);

        var missing = _directory.FullPath / "does-not-exist";

        var result = SheetWriter.Write(missing, "body-01", sheet);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~SheetWriterTests"`

Expected: **compile error** — `SheetWriter` does not exist.

- [ ] **Step 3: Append the two failure members**

In `src/TheOmenDen.PixelForge.Core/Baking/BakeFailure.cs`, append after `RoundTripMismatch`. Appending keeps every existing member's number:

```csharp
    /// <summary>The output directory does not exist, or is not reachable.</summary>
    OutputDirectoryUnavailable,

    /// <summary>The sheet encoded, but the file could not be written.</summary>
    OutputWriteFailed,
```

- [ ] **Step 4: Write `SheetWriter`**

Create `src/TheOmenDen.PixelForge.Core/Baking/SheetWriter.cs`:

```csharp
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using Microsoft.IO;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Puts a baked sheet on disk.
/// <para>
/// <see cref="RecyclableMemoryStream.WriteTo(Stream)"/> is the zero-copy path and the only one
/// available: the manager sets <c>ThrowExceptionOnToArray</c>, so the obvious
/// <c>File.WriteAllBytes(stream.ToArray())</c> throws by design rather than quietly copying a
/// pooled buffer back onto the managed heap.
/// </para>
/// <para>
/// A missing directory or a locked file is someone's disk, not a bug, so both travel as
/// <see cref="BakeFailure"/> values.
/// </para>
/// </summary>
public static class SheetWriter
{
    public const string Extension = ".webp";

    /// <summary>
    /// Writes <paramref name="sheet"/> to <c>&lt;directory&gt;/&lt;name&gt;.webp</c> and reports
    /// how much landed. The stream's position is irrelevant: the whole thing is written.
    /// </summary>
    public static Result<ByteSize, BakeFailure> Write(
        FullPath directory,
        string name,
        RecyclableMemoryStream sheet)
    {
        Guard.IsNotNull(sheet);
        Guard.IsNotNullOrWhiteSpace(name);

        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        var target = directory / (name + Extension);

        try
        {
            using var file = File.Create(target.Value);

            // WriteTo ignores Position and writes the full length, which is what we want and is
            // also why this never rewinds the caller's stream.
            sheet.WriteTo(file);

            return ByteSize.FromBytes(sheet.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~SheetWriterTests"`

Expected: 4 passed. If `ByteSize.FromBytes` does not bind, use `new ByteSize(sheet.Length)` — both are on the type.

- [ ] **Step 6: Run the full build and test suite**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

- [ ] **Step 7: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Baking/SheetWriter.cs src/TheOmenDen.PixelForge.Core/Baking/BakeFailure.cs tests/TheOmenDen.PixelForge.Core.Tests/Baking/SheetWriterTests.cs
git commit -m "feat(core): write baked sheets to disk without copying the pooled buffer"
```

---

### Task 4: Batch runner with bounded parallelism, progress and cancellation

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Baking/BatchBaker.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/BatchBakerTests.cs`

**Interfaces:**
- Consumes: `RecipeBaker.Bake`, `SheetWriter.Write` (Task 3), `SheetRecipe`.
- Produces, all bound by Task 16:
  - `readonly record struct BakeProgress { string Name; Optional<ByteSize> Written; BakeFailure Failure; int Completed; int Total; bool IsSuccess; }`
  - `sealed record BatchSummary { int Succeeded; int Failed; ByteSize TotalWritten; bool Cancelled; }`
  - `BatchBaker.RunAsync(ImmutableArray<SheetRecipe>, FullPath, IProgress<BakeProgress>?, int, CancellationToken)` → `Task<BatchSummary>`

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/BatchBakerTests.cs`:

```csharp
using System.Collections.Immutable;
using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

public sealed class BatchBakerTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    /// <summary>Collects reports synchronously. Progress&lt;T&gt; posts asynchronously, which
    /// would make the count assertions racy.</summary>
    private sealed class CollectingProgress : IProgress<BakeProgress>
    {
        private readonly Lock _gate = new();

        public List<BakeProgress> Reports { get; } = [];

        public void Report(BakeProgress value)
        {
            lock (_gate)
            {
                Reports.Add(value);
            }
        }
    }

    private FullPath WritePartial(string name)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var pixels = bitmap.Pixels;
        Array.Fill(pixels, new SKColor(0x20, 0x40, 0x60, 0xFF));
        bitmap.Pixels = pixels;

        var path = _directory.FullPath / name;

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    private FullPath OutputDirectory()
    {
        var output = _directory.FullPath / "out";

        Directory.CreateDirectory(output.Value);

        return output;
    }

    private ImmutableArray<SheetRecipe> GoodRecipes(int count)
    {
        var layer = WritePartial("layer.png");
        var recipes = ImmutableArray.CreateBuilder<SheetRecipe>(count);

        for (var i = 0; i < count; i++)
        {
            recipes.Add(new() { Name = $"sheet-{i:00}", Layers = [layer] });
        }

        return recipes.ToImmutable();
    }

    [Fact]
    public async Task RunAsync_WritesOneFilePerRecipe()
    {
        var output = OutputDirectory();

        var summary = await BatchBaker.RunAsync(
            GoodRecipes(3), output, null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(3, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        Assert.False(summary.Cancelled);
        Assert.Equal(3, Directory.GetFiles(output.Value, "*.webp").Length);
    }

    [Fact]
    public async Task RunAsync_ReportsProgressOncePerRecipe()
    {
        var output = OutputDirectory();
        var progress = new CollectingProgress();

        var summary = await BatchBaker.RunAsync(
            GoodRecipes(4), output, progress, 2, TestContext.Current.CancellationToken);

        Assert.Equal(4, summary.Succeeded);
        Assert.Equal(4, progress.Reports.Count);
        Assert.All(progress.Reports, r => Assert.Equal(4, r.Total));
        Assert.All(progress.Reports, r => Assert.True(r.IsSuccess));

        // Completed is a running position, so the set must be exactly 1..4 regardless of order.
        Assert.Equal([1, 2, 3, 4], progress.Reports.Select(static r => r.Completed).Order());
    }

    /// <summary>One missing partial must not abort the sheets that are fine.</summary>
    [Fact]
    public async Task RunAsync_ContinuesPastAFailedRecipe()
    {
        var output = OutputDirectory();

        var recipes = GoodRecipes(2).Add(new SheetRecipe
        {
            Name = "broken",
            Layers = [_directory.FullPath / "absent.png"],
        });

        var summary = await BatchBaker.RunAsync(
            recipes, output, null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(2, Directory.GetFiles(output.Value, "*.webp").Length);
    }

    [Fact]
    public async Task RunAsync_ReportsTheFailureReason_ForABrokenRecipe()
    {
        var output = OutputDirectory();
        var progress = new CollectingProgress();

        var recipes = ImmutableArray.Create(new SheetRecipe
        {
            Name = "broken",
            Layers = [_directory.FullPath / "absent.png"],
        });

        await BatchBaker.RunAsync(recipes, output, progress, 1, TestContext.Current.CancellationToken);

        var report = Assert.Single(progress.Reports);

        Assert.Equal("broken", report.Name);
        Assert.Equal(BakeFailure.LayerNotFound, report.Failure);
        Assert.False(report.IsSuccess);
        Assert.False(report.Written.HasValue);
    }

    [Fact]
    public async Task RunAsync_ReportsCancelled_WhenTheTokenIsAlreadyCancelled()
    {
        var output = OutputDirectory();

        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        var summary = await BatchBaker.RunAsync(GoodRecipes(4), output, null, 2, cts.Token);

        Assert.True(summary.Cancelled);
    }

    [Fact]
    public async Task RunAsync_FailsEveryRecipe_WhenTheOutputDirectoryIsMissing()
    {
        var missing = _directory.FullPath / "no-such-dir";

        var summary = await BatchBaker.RunAsync(
            GoodRecipes(3), missing, null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.Succeeded);
        Assert.Equal(3, summary.Failed);
    }

    [Fact]
    public async Task RunAsync_SumsTheBytesWritten()
    {
        var output = OutputDirectory();

        var summary = await BatchBaker.RunAsync(
            GoodRecipes(3), output, null, 2, TestContext.Current.CancellationToken);

        var onDisk = Directory.GetFiles(output.Value, "*.webp").Sum(static f => new FileInfo(f).Length);

        Assert.Equal(onDisk, summary.TotalWritten.Value);
    }

    [Fact]
    public async Task RunAsync_ReturnsAnEmptySummary_WhenGivenNoRecipes()
    {
        var summary = await BatchBaker.RunAsync(
            [], OutputDirectory(), null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        Assert.False(summary.Cancelled);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~BatchBakerTests"`

Expected: **compile error** — `BatchBaker`, `BakeProgress` and `BatchSummary` do not exist.

- [ ] **Step 3: Write `BatchBaker`**

Create `src/TheOmenDen.PixelForge.Core/Baking/BatchBaker.cs`:

```csharp
using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>One recipe's outcome, reported the moment it finishes.</summary>
/// <remarks>
/// <see cref="BakeFailure"/> is numbered from 1, which is load-bearing here: a
/// <see cref="Failure"/> of <c>default</c> <em>is</em> the success signal, so no separate
/// boolean can drift out of step with it.
/// </remarks>
public readonly record struct BakeProgress
{
    public required string Name { get; init; }

    /// <summary>Absent on failure — there is nothing to have written.</summary>
    public required Optional<ByteSize> Written { get; init; }

    /// <summary><c>default</c> means the sheet was written.</summary>
    public required BakeFailure Failure { get; init; }

    /// <summary>Running position in the run, 1-based. Not an index — order is not guaranteed.</summary>
    public required int Completed { get; init; }

    public required int Total { get; init; }

    public bool IsSuccess => Failure is default(BakeFailure);
}

/// <summary>What a whole run came to.</summary>
public sealed record BatchSummary
{
    public required int Succeeded { get; init; }

    public required int Failed { get; init; }

    public required ByteSize TotalWritten { get; init; }

    /// <summary>The run stopped early. Sheets already written are kept.</summary>
    public required bool Cancelled { get; init; }
}

/// <summary>
/// Runs many recipes and writes each result to disk.
/// <para>
/// <c>Parallel.ForEachAsync</c> is what bounds this. DotNext's <c>TaskCompletionPipe&lt;T&gt;</c>
/// was the obvious candidate — it streams results in completion order and carries a correlation
/// token — but it does not bound concurrency: every task added starts immediately. A full
/// flattened run is 63 sheets, each decoding four 828 KiB partials, so unbounded start is the
/// memory failure mode. <c>Parallel.ForEachAsync</c> bounds <em>and</em> reports on completion,
/// so no throttle primitive is needed and none of the banned synchronisation types appear.
/// </para>
/// <para>
/// A failed recipe is reported and the run continues. One missing partial must not cost the
/// other 78 sheets.
/// </para>
/// </summary>
public static class BatchBaker
{
    public static async Task<BatchSummary> RunAsync(
        ImmutableArray<SheetRecipe> recipes,
        FullPath outputDirectory,
        IProgress<BakeProgress>? progress,
        int maxParallelism,
        CancellationToken cancellationToken)
    {
        Guard.IsGreaterThan(maxParallelism, 0);

        if (recipes.IsDefaultOrEmpty)
        {
            return Empty(cancelled: false);
        }

        var total = recipes.Length;
        var completed = 0;
        var succeeded = 0;
        var failed = 0;
        var written = 0L;
        var cancelled = false;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            CancellationToken = cancellationToken,
        };

        try
        {
            await Parallel.ForEachAsync(recipes, options, (recipe, token) =>
            {
                token.ThrowIfCancellationRequested();

                var outcome = BakeOne(recipe, outputDirectory);

                // Interlocked rather than a lock: four independent counters, touched once per
                // recipe. A lock here would serialise the reporting of work already done in
                // parallel.
                var position = Interlocked.Increment(ref completed);
                var size = Optional<ByteSize>.None;

                if (outcome.TryGet(out var actual))
                {
                    Interlocked.Increment(ref succeeded);
                    Interlocked.Add(ref written, actual.Value);
                    size = actual;
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }

                progress?.Report(new()
                {
                    Name = recipe.Name,
                    Written = size,
                    Failure = outcome.IsSuccessful ? default : outcome.Error,
                    Completed = position,
                    Total = total,
                });

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        return new()
        {
            Succeeded = succeeded,
            Failed = failed,
            TotalWritten = ByteSize.FromBytes(written),
            Cancelled = cancelled,
        };
    }

    private static BatchSummary Empty(bool cancelled) => new()
    {
        Succeeded = 0,
        Failed = 0,
        TotalWritten = ByteSize.FromBytes(0),
        Cancelled = cancelled,
    };

    /// <summary>
    /// Bake and write one recipe. The pooled stream is disposed here so its buffer returns to
    /// the pool before the next recipe on this worker asks for one.
    /// </summary>
    private static Result<ByteSize, BakeFailure> BakeOne(SheetRecipe recipe, FullPath outputDirectory)
    {
        var baked = RecipeBaker.Bake(recipe);

        if (!baked.TryGet(out var sheet))
        {
            return new(baked.Error);
        }

        using (sheet)
        {
            return SheetWriter.Write(outputDirectory, recipe.Name, sheet);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~BatchBakerTests"`

Expected: 8 passed.

- [ ] **Step 5: Run the full build and test suite**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

- [ ] **Step 6: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Baking/BatchBaker.cs tests/TheOmenDen.PixelForge.Core.Tests/Baking/BatchBakerTests.cs
git commit -m "feat(core): add bounded-parallel batch baker with progress and cancellation"
```

---

### Task 5: Sheet index manifest

An atlas that does not say what its rows mean is not self-describing. Without this, a consumer has to know from somewhere else that output rows 3–5 are `idle` across south, west and east. `CLAUDE.md` already names CsvHelper for "sprite-sheet index import/export", so this is the format.

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Spritesheets/SheetIndex.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Spritesheets/SheetIndexTests.cs`

**Interfaces:**
- Consumes: `SheetLayout.Clips`, `SheetLayout.RowFor`, `SheetLayout.FacingCount`, `SheetLayout.CellSize`, `SheetLayout.OutputColumns`.
- Produces:
  - `sealed record SheetIndexRow { string Clip; string Facing; int Row; int FrameCount; int FirstColumn; int CellSize; }`
  - `SheetIndex.Facings` → `ImmutableArray<string>`
  - `SheetIndex.Rows` → `ImmutableArray<SheetIndexRow>`
  - `SheetIndex.Write(TextWriter)` → `int`
  - `SheetIndex.WriteTo(FullPath directory)` → `Result<int, BakeFailure>`

  Task 16 calls `WriteTo` after a successful run.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Spritesheets/SheetIndexTests.cs`:

```csharp
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

public sealed class SheetIndexTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Rows_DescribeEveryClipOnEveryFacing()
    {
        Assert.Equal(SheetLayout.ClipCount * SheetLayout.FacingCount, SheetIndex.Rows.Length);
    }

    /// <summary>
    /// The manifest must agree with the remap the baker actually performs, or it is worse than
    /// no manifest at all.
    /// </summary>
    [Fact]
    public void Rows_MatchTheLayoutRowMap()
    {
        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            var clip = SheetLayout.Clips[clipIndex];

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                var expectedRow = SheetLayout.RowFor(clipIndex, facing);

                var row = SheetIndex.Rows.AsSpan()
                    .First(r => r.Clip == clip.Name && r.Facing == SheetIndex.Facings[facing]);

                Assert.Equal(expectedRow, row.Row);
                Assert.Equal(clip.FrameCount, row.FrameCount);
                Assert.Equal(clip.SourceColumn, row.FirstColumn);
                Assert.Equal(SheetLayout.CellSize, row.CellSize);
            }
        }
    }

    [Fact]
    public void Facings_AreSouthWestEast_AndNeverNorth()
    {
        Assert.Equal<string[]>(["south", "west", "east"], [.. SheetIndex.Facings]);
        Assert.DoesNotContain("north", SheetIndex.Facings);
    }

    [Fact]
    public void Write_EmitsAHeaderAndOneLinePerRow()
    {
        using var writer = new StringWriter();

        var count = SheetIndex.Write(writer);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(SheetIndex.Rows.Length, count);
        Assert.Equal(SheetIndex.Rows.Length + 1, lines.Length);
        Assert.StartsWith("Clip,Facing,Row,FrameCount,FirstColumn,CellSize", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void WriteTo_PutsIndexCsvBesideTheSheets()
    {
        var result = SheetIndex.WriteTo(_directory.FullPath);

        Assert.True(result.IsSuccessful, $"write failed with {result.Error}");
        Assert.True(File.Exists((_directory.FullPath / "index.csv").Value));
    }

    [Fact]
    public void WriteTo_ReportsOutputDirectoryUnavailable_WhenTheDirectoryIsMissing()
    {
        var result = SheetIndex.WriteTo(_directory.FullPath / "nope");

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }
}
```

Add `using TheOmenDen.PixelForge.Core.Baking;` to the test file for `BakeFailure`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~SheetIndexTests"`

Expected: **compile error** — `SheetIndex` does not exist.

- [ ] **Step 3: Write `SheetIndex`**

Create `src/TheOmenDen.PixelForge.Core/Spritesheets/SheetIndex.cs`:

```csharp
using System.Collections.Immutable;
using System.Globalization;
using CsvHelper;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>One clip on one facing, and the output row it occupies.</summary>
public sealed record SheetIndexRow
{
    public required string Clip { get; init; }

    public required string Facing { get; init; }

    /// <summary>Output row, 0-based.</summary>
    public required int Row { get; init; }

    public required int FrameCount { get; init; }

    /// <summary>Column of frame 0 in the 23-column source, kept for traceability.</summary>
    public required int FirstColumn { get; init; }

    public required int CellSize { get; init; }
}

/// <summary>
/// The manifest that makes an exported sheet self-describing.
/// <para>
/// A curated sheet is an atlas: 24 rows of 5 cells with no in-band clue that rows 3-5 are
/// <c>idle</c> south, west and east. Shipping the row map beside the art is the difference
/// between an atlas a consumer can load and one it has to be told about out of band.
/// </para>
/// <para>
/// Derived from <see cref="SheetLayout"/> rather than restated, so the manifest cannot drift
/// from the remap the baker actually performs.
/// </para>
/// </summary>
public static class SheetIndex
{
    public const string FileName = "index.csv";

    /// <summary>Source row order, north dropped — see <see cref="SheetLayout.FacingCount"/>.</summary>
    public static ImmutableArray<string> Facings { get; } = ["south", "west", "east"];

    public static ImmutableArray<SheetIndexRow> Rows { get; } = Build();

    private static ImmutableArray<SheetIndexRow> Build()
    {
        var rows = ImmutableArray.CreateBuilder<SheetIndexRow>(SheetLayout.ClipCount * SheetLayout.FacingCount);

        for (var clipIndex = 0; clipIndex < SheetLayout.Clips.Length; clipIndex++)
        {
            var clip = SheetLayout.Clips[clipIndex];

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                rows.Add(new()
                {
                    Clip = clip.Name,
                    Facing = Facings[facing],
                    Row = SheetLayout.RowFor(clipIndex, facing),
                    FrameCount = clip.FrameCount,
                    FirstColumn = clip.SourceColumn,
                    CellSize = SheetLayout.CellSize,
                });
            }
        }

        return rows.ToImmutable();
    }

    /// <summary>Writes the manifest and returns the row count.</summary>
    public static int Write(TextWriter writer)
    {
        Guard.IsNotNull(writer);

        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        csv.WriteRecords(Rows);
        csv.Flush();

        return Rows.Length;
    }

    /// <summary>Writes <c>index.csv</c> into an export directory.</summary>
    public static Result<int, BakeFailure> WriteTo(FullPath directory)
    {
        if (!Directory.Exists(directory.Value))
        {
            return new(BakeFailure.OutputDirectoryUnavailable);
        }

        try
        {
            using var writer = new StreamWriter((directory / FileName).Value);

            return Write(writer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(BakeFailure.OutputWriteFailed);
        }
    }
}
```

Add `using CommunityToolkit.Diagnostics;` for `Guard`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~SheetIndexTests"`

Expected: 6 passed. If `CsvWriter` emits `\r\n`, the line-count assertion still holds because the split is on `'\n'` with `RemoveEmptyEntries`.

- [ ] **Step 5: Run the full build and test suite**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

- [ ] **Step 6: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Spritesheets/SheetIndex.cs tests/TheOmenDen.PixelForge.Core.Tests/Spritesheets/SheetIndexTests.cs
git commit -m "feat(core): emit a clip-to-row manifest so exported atlases are self-describing"
```

---

## Phase B — Core: palette persistence and preview

### Task 6: Ramp CSV persistence

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Palettes/RampFailure.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Palettes/RampStore.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Palettes/RampStoreTests.cs`

**Interfaces:**
- Consumes: `SkinRamp`, `SkinRamps.StepCount`, `ColorHelper.ColorConverter`, `CsvHelper`, `FullPath`.
- Produces:
  - `enum RampFailure { StoreUnreadable = 1, StoreMalformed, StoreUnwritable, WrongStepCount, NameEmpty, DuplicateName, BuiltInImmutable, NotFound }`
  - `sealed record RampRow { string Name; bool IsHuman; string Step1..Step5; }`
  - `RampStore.Read(TextReader)` → `Result<ImmutableArray<SkinRamp>, RampFailure>`
  - `RampStore.Write(TextWriter, IReadOnlyList<SkinRamp>)` → `Result<int, RampFailure>`
  - instance `RampStore(FullPath file).Load()` / `.Save(IReadOnlyList<SkinRamp>)`
  - extension members `ramp.ToRow()` and `row.ToRamp()`

  Task 11 (`RampService`) binds to all of these.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Palettes/RampStoreTests.cs`:

```csharp
using System.Collections.Immutable;
using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

public sealed class RampStoreTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private static SkinRamp Custom(string name) => new()
    {
        Name = name,
        IsHuman = false,
        Steps = [.. new[]
        {
            new SKColor(0x11, 0x22, 0x33),
            new SKColor(0x44, 0x55, 0x66),
            new SKColor(0x77, 0x88, 0x99),
            new SKColor(0xAA, 0xBB, 0xCC),
            new SKColor(0xDD, 0xEE, 0xFF),
        }],
    };

    [Fact]
    public void ReadWrite_RoundTripsARamp()
    {
        using var writer = new StringWriter();

        var written = RampStore.Write(writer, [Custom("My Ramp")]);

        Assert.True(written.IsSuccessful, $"write failed with {written.Error}");
        Assert.Equal(1, written.Value);

        using var reader = new StringReader(writer.ToString());

        var read = RampStore.Read(reader);

        Assert.True(read.IsSuccessful, $"read failed with {read.Error}");

        var ramp = Assert.Single(read.Value.AsSpan().ToArray());

        Assert.Equal("My Ramp", ramp.Name);
        Assert.False(ramp.IsHuman);
        Assert.Equal(SkinRamps.StepCount, ramp.Steps.Length);
        Assert.Equal(new SKColor(0x11, 0x22, 0x33), ramp.Steps[0]);
        Assert.Equal(new SKColor(0xDD, 0xEE, 0xFF), ramp.Steps[4]);
    }

    /// <summary>
    /// The shipped ramps are the reference case — a file written from them must read back
    /// identical, or the format cannot represent what we already ship.
    /// </summary>
    [Fact]
    public void ReadWrite_RoundTripsEveryBuiltInRamp()
    {
        using var writer = new StringWriter();

        RampStore.Write(writer, [.. SkinRamps.All]);

        using var reader = new StringReader(writer.ToString());

        var read = RampStore.Read(reader);

        Assert.True(read.IsSuccessful, $"read failed with {read.Error}");
        Assert.Equal(SkinRamps.All.Length, read.Value.Length);

        for (var i = 0; i < SkinRamps.All.Length; i++)
        {
            Assert.Equal(SkinRamps.All[i].Name, read.Value[i].Name);
            Assert.Equal(SkinRamps.All[i].IsHuman, read.Value[i].IsHuman);
            Assert.Equal(SkinRamps.All[i].Steps, read.Value[i].Steps);
        }
    }

    [Fact]
    public void Read_ReportsStoreMalformed_WhenAStepIsNotHex()
    {
        using var reader = new StringReader(
            """
            Name,IsHuman,Step1,Step2,Step3,Step4,Step5
            Bad,False,#GGGGGG,#445566,#778899,#AABBCC,#DDEEFF
            """);

        var read = RampStore.Read(reader);

        Assert.False(read.IsSuccessful);
        Assert.Equal(RampFailure.StoreMalformed, read.Error);
    }

    [Fact]
    public void Read_ReportsNameEmpty_WhenARampHasNoName()
    {
        using var reader = new StringReader(
            """
            Name,IsHuman,Step1,Step2,Step3,Step4,Step5
            ,False,#112233,#445566,#778899,#AABBCC,#DDEEFF
            """);

        var read = RampStore.Read(reader);

        Assert.False(read.IsSuccessful);
        Assert.Equal(RampFailure.NameEmpty, read.Error);
    }

    [Fact]
    public void Read_ReportsDuplicateName_WhenTwoRampsShareOne()
    {
        using var reader = new StringReader(
            """
            Name,IsHuman,Step1,Step2,Step3,Step4,Step5
            Same,False,#112233,#445566,#778899,#AABBCC,#DDEEFF
            Same,False,#112233,#445566,#778899,#AABBCC,#DDEEFF
            """);

        var read = RampStore.Read(reader);

        Assert.False(read.IsSuccessful);
        Assert.Equal(RampFailure.DuplicateName, read.Error);
    }

    [Fact]
    public void Write_ReportsWrongStepCount_WhenARampIsNotFiveSteps()
    {
        using var writer = new StringWriter();

        var short_ = new SkinRamp
        {
            Name = "Short",
            IsHuman = false,
            Steps = [new SKColor(1, 2, 3)],
        };

        var written = RampStore.Write(writer, [short_]);

        Assert.False(written.IsSuccessful);
        Assert.Equal(RampFailure.WrongStepCount, written.Error);
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenTheFileDoesNotExist()
    {
        var store = new RampStore(_directory.FullPath / "absent.csv");

        var loaded = store.Load();

        Assert.True(loaded.IsSuccessful, $"load failed with {loaded.Error}");
        Assert.Empty(loaded.Value);
    }

    [Fact]
    public void SaveLoad_RoundTripsThroughDisk()
    {
        var store = new RampStore(_directory.FullPath / "ramps.csv");

        var saved = store.Save([Custom("Disk Ramp")]);

        Assert.True(saved.IsSuccessful, $"save failed with {saved.Error}");

        var loaded = store.Load();

        Assert.True(loaded.IsSuccessful, $"load failed with {loaded.Error}");
        Assert.Equal("Disk Ramp", Assert.Single(loaded.Value.AsSpan().ToArray()).Name);
    }

    /// <summary>Saving into a directory that does not exist creates it rather than failing.</summary>
    [Fact]
    public void Save_CreatesTheParentDirectory()
    {
        var store = new RampStore(_directory.FullPath / "nested" / "ramps.csv");

        var saved = store.Save([Custom("Nested")]);

        Assert.True(saved.IsSuccessful, $"save failed with {saved.Error}");
        Assert.True(File.Exists((_directory.FullPath / "nested" / "ramps.csv").Value));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RampStoreTests"`

Expected: **compile error** — `RampStore` and `RampFailure` do not exist.

- [ ] **Step 3: Write `RampFailure`**

Create `src/TheOmenDen.PixelForge.Core/Palettes/RampFailure.cs`:

```csharp
namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// Why a ramp could not be read, written or accepted.
/// <para>
/// All environmental or data conditions — a hand-edited CSV, a name collision, a file someone
/// has open in Excel. None is a programming error, so none is an exception: they travel as
/// <see cref="DotNext.Result{T, TError}"/> values.
/// </para>
/// <para>Numbered from 1 so <c>default</c> is never mistaken for a real failure.</para>
/// </summary>
public enum RampFailure
{
    /// <summary>The file exists but could not be opened.</summary>
    StoreUnreadable = 1,

    /// <summary>The CSV parsed, but a row is not a ramp — a bad hex, a missing column.</summary>
    StoreMalformed,

    /// <summary>The file could not be written.</summary>
    StoreUnwritable,

    /// <summary>Not exactly <see cref="SkinRamps.StepCount"/> colours.</summary>
    WrongStepCount,

    /// <summary>A ramp with no name cannot be selected or referenced.</summary>
    NameEmpty,

    /// <summary>Ramps are identified by name, across built-ins and customs alike.</summary>
    DuplicateName,

    /// <summary>The seven shipped ramps are the contract and cannot be edited in place.</summary>
    BuiltInImmutable,

    /// <summary>No ramp by that name.</summary>
    NotFound,
}
```

- [ ] **Step 4: Write `RampStore`**

Create `src/TheOmenDen.PixelForge.Core/Palettes/RampStore.cs`:

```csharp
using System.Collections.Immutable;
using System.Globalization;
using ColorHelper;
using CommunityToolkit.Diagnostics;
using CsvHelper;
using DotNext;
using Meziantou.Framework;
using SkiaSharp;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// One ramp as a CSV row. Steps are <c>#RRGGBB</c>, darkest first — the same literals
/// <see cref="SkinRamps"/> is authored in, so a saved file diffs directly against the source.
/// </summary>
public sealed record RampRow
{
    public string Name { get; init; } = string.Empty;

    public bool IsHuman { get; init; }

    public string Step1 { get; init; } = string.Empty;

    public string Step2 { get; init; } = string.Empty;

    public string Step3 { get; init; } = string.Empty;

    public string Step4 { get; init; } = string.Empty;

    public string Step5 { get; init; } = string.Empty;

    public string[] Steps => [Step1, Step2, Step3, Step4, Step5];
}

/// <summary>
/// Conversion between a ramp and its CSV row.
/// <para>
/// Hand-written rather than generated. Mapperly is the project default for object mapping, but
/// this is a shape change — a five-element <see cref="ImmutableArray{T}"/> to five named hex
/// columns — plus formatting, so a Mapperly mapping would need a user-implemented method whose
/// body is this entire conversion. The generator would add indirection and emit nothing.
/// </para>
/// </summary>
public static class RampConversions
{
    extension(SkinRamp ramp)
    {
        public RampRow ToRow() => new()
        {
            Name = ramp.Name,
            IsHuman = ramp.IsHuman,
            Step1 = Hex(ramp.Steps[0]),
            Step2 = Hex(ramp.Steps[1]),
            Step3 = Hex(ramp.Steps[2]),
            Step4 = Hex(ramp.Steps[3]),
            Step5 = Hex(ramp.Steps[4]),
        };
    }

    extension(RampRow row)
    {
        public Result<SkinRamp, RampFailure> ToRamp()
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                return new(RampFailure.NameEmpty);
            }

            var steps = ImmutableArray.CreateBuilder<SKColor>(SkinRamps.StepCount);

            foreach (var hex in row.Steps)
            {
                if (!TryParseHex(hex, out var color))
                {
                    return new(RampFailure.StoreMalformed);
                }

                steps.Add(color);
            }

            return new SkinRamp
            {
                Name = row.Name.Trim(),
                IsHuman = row.IsHuman,
                Steps = steps.ToImmutable(),
            };
        }
    }

    public static string Hex(SKColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}");

    /// <summary>
    /// Parsing goes through <see cref="ColorConverter.HexToRgb"/>, as
    /// <see cref="SkinRamps"/> already does — but that throws on garbage, and a hand-edited file
    /// is expected input, so the shape is checked first.
    /// </summary>
    public static bool TryParseHex(string? hex, out SKColor color)
    {
        color = default;

        if (hex is null)
        {
            return false;
        }

        var trimmed = hex.AsSpan().Trim();

        if (trimmed.Length is not 0 && trimmed[0] is '#')
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length is not 6)
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        var rgb = ColorConverter.HexToRgb(new HEX(trimmed.ToString()));

        color = new SKColor(rgb.R, rgb.G, rgb.B);

        return true;
    }
}

/// <summary>
/// Loads and saves custom ramps as CSV.
/// <para>
/// The built-in ramps are never written here — <see cref="SkinRamps.All"/> is the contract and
/// stays in code. This store holds only what a user added, and the app concatenates the two.
/// </para>
/// <para>
/// The file path is injected so the app can pass LocalState and tests can pass a
/// <see cref="TemporaryDirectory"/>. Read and write are static and take a
/// <see cref="TextReader"/>/<see cref="TextWriter"/>, so the format is testable with no
/// filesystem at all.
/// </para>
/// </summary>
public sealed class RampStore(FullPath file)
{
    public FullPath File => file;

    public static Result<ImmutableArray<SkinRamp>, RampFailure> Read(TextReader reader)
    {
        Guard.IsNotNull(reader);

        List<RampRow> rows;

        try
        {
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture, leaveOpen: true);

            rows = [.. csv.GetRecords<RampRow>()];
        }
        catch (CsvHelperException)
        {
            return new(RampFailure.StoreMalformed);
        }

        var ramps = ImmutableArray.CreateBuilder<SkinRamp>(rows.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var converted = row.ToRamp();

            if (!converted.TryGet(out var ramp))
            {
                return new(converted.Error);
            }

            if (!seen.Add(ramp.Name))
            {
                return new(RampFailure.DuplicateName);
            }

            ramps.Add(ramp);
        }

        return ramps.ToImmutable();
    }

    public static Result<int, RampFailure> Write(TextWriter writer, IReadOnlyList<SkinRamp> ramps)
    {
        Guard.IsNotNull(writer);
        Guard.IsNotNull(ramps);

        foreach (var ramp in ramps)
        {
            if (ramp.Steps.Length != SkinRamps.StepCount)
            {
                return new(RampFailure.WrongStepCount);
            }

            if (string.IsNullOrWhiteSpace(ramp.Name))
            {
                return new(RampFailure.NameEmpty);
            }
        }

        var rows = new List<RampRow>(ramps.Count);

        foreach (var ramp in ramps)
        {
            rows.Add(ramp.ToRow());
        }

        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        csv.WriteRecords(rows);
        csv.Flush();

        return ramps.Count;
    }

    /// <summary>A missing file is an empty set, not a failure — first run is the normal case.</summary>
    public Result<ImmutableArray<SkinRamp>, RampFailure> Load()
    {
        if (!System.IO.File.Exists(file.Value))
        {
            return ImmutableArray<SkinRamp>.Empty;
        }

        try
        {
            using var reader = new StreamReader(file.Value);

            return Read(reader);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(RampFailure.StoreUnreadable);
        }
    }

    public Result<int, RampFailure> Save(IReadOnlyList<SkinRamp> ramps)
    {
        try
        {
            var parent = file.Parent;

            if (parent != default)
            {
                Directory.CreateDirectory(parent.Value);
            }

            using var writer = new StreamWriter(file.Value);

            return Write(writer, ramps);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(RampFailure.StoreUnwritable);
        }
    }
}
```

**Implementer note on `FullPath.Parent`:** if `Parent` is not the member name on `Meziantou.Framework.FullPath` 3.0.1, use `FullPath.FromPath(Path.GetDirectoryName(file.Value)!)`. Check with `get_public_api` before guessing.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RampStoreTests"`

Expected: 9 passed.

- [ ] **Step 6: Run the full build and test suite**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

- [ ] **Step 7: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Palettes/RampFailure.cs src/TheOmenDen.PixelForge.Core/Palettes/RampStore.cs tests/TheOmenDen.PixelForge.Core.Tests/Palettes/RampStoreTests.cs
git commit -m "feat(core): persist custom skin ramps as CSV"
```

---

### Task 7: Live palette preview

**Files:**
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/SheetRecipe.cs` (expose `RecipeBaker.AssembleLayers`)
- Create: `src/TheOmenDen.PixelForge.Core/Palettes/PalettePreview.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Palettes/PalettePreviewTests.cs`

**Interfaces:**
- Consumes: `SheetBaker.Assemble`, `SheetBaker.Recolor`, `SheetBaker.Curate`, `SheetBaker.ToCanonical`, `SheetLayout`, `SkinRamps.Source`.
- Produces:
  - `RecipeBaker.AssembleLayers(SheetRecipe)` → `Result<SKBitmap, BakeFailure>` (decode + composite only; no recolour, curate or encode)
  - `PalettePreview.Create(SheetRecipe body)` → `Result<PalettePreview, BakeFailure>`
  - `PalettePreview.RenderIdleRow(SkinRamp ramp, int scale)` → `Result<SKBitmap, BakeFailure>`
  - `PalettePreview.IdleRowWidth` / `IdleRowHeight` constants

  Task 15 renders the result through `SkiaSharp.Views.WinUI`'s `ToWriteableBitmap()` extension.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Palettes/PalettePreviewTests.cs`:

```csharp
using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

public sealed class PalettePreviewTests : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    /// <summary>A source-geometry partial filled with one colour, written as PNG.</summary>
    private FullPath WritePartial(string name, SKColor fill)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var pixels = bitmap.Pixels;
        Array.Fill(pixels, fill);
        bitmap.Pixels = pixels;

        var path = _directory.FullPath / name;

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    private PalettePreview CreateOrFail(SKColor fill)
    {
        var recipe = new SheetRecipe
        {
            Name = "preview",
            Layers = [WritePartial("body.png", fill)],
        };

        var created = PalettePreview.Create(recipe);

        Assert.True(created.IsSuccessful, $"create failed with {created.Error}");

        return created.Value;
    }

    [Fact]
    public void RenderIdleRow_ProducesThreeFacingsAtTheRequestedScale()
    {
        using var preview = CreateOrFail(SkinRamps.Source.Steps[3]);

        var rendered = preview.RenderIdleRow(SkinRamps.All[4], scale: 4);

        Assert.True(rendered.IsSuccessful, $"render failed with {rendered.Error}");

        using var image = rendered.Value;

        Assert.Equal(SheetLayout.CellSize * SheetLayout.FacingCount * 4, image.Width);
        Assert.Equal(SheetLayout.CellSize * 4, image.Height);
    }

    [Fact]
    public void RenderIdleRow_AppliesTheRamp()
    {
        var target = SkinRamps.All[4];

        using var preview = CreateOrFail(SkinRamps.Source.Steps[3]);

        var rendered = preview.RenderIdleRow(target, scale: 1);

        Assert.True(rendered.IsSuccessful, $"render failed with {rendered.Error}");

        using var image = rendered.Value;

        Assert.Equal(target.Steps[3], image.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2));
    }

    /// <summary>
    /// The source ramp must render as itself — a substitution from Source to Source is the
    /// identity, and a preview that shifted colour on the default tone would be lying.
    /// </summary>
    [Fact]
    public void RenderIdleRow_IsIdentity_ForTheSourceRamp()
    {
        var fill = SkinRamps.Source.Steps[2];

        using var preview = CreateOrFail(fill);

        var rendered = preview.RenderIdleRow(SkinRamps.Source, scale: 1);

        Assert.True(rendered.IsSuccessful);

        using var image = rendered.Value;

        Assert.Equal(fill, image.GetPixel(SheetLayout.CellSize / 2, SheetLayout.CellSize / 2));
    }

    /// <summary>
    /// Nearest-neighbour upscaling: a scaled pixel block must be one flat colour, never a
    /// gradient. This is what keeps the XAML Image from blurring pixel art.
    /// </summary>
    [Fact]
    public void RenderIdleRow_UpscalesWithoutInterpolating()
    {
        using var preview = CreateOrFail(SkinRamps.Source.Steps[1]);

        var rendered = preview.RenderIdleRow(SkinRamps.Source, scale: 4);

        Assert.True(rendered.IsSuccessful);

        using var image = rendered.Value;

        var first = image.GetPixel(0, 0);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(first, image.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var preview = CreateOrFail(SkinRamps.Source.Steps[0]);

        preview.Dispose();
        preview.Dispose();

        Assert.True(preview.IsDisposed);
    }

    [Fact]
    public void RenderIdleRow_ThrowsObjectDisposed_AfterDispose()
    {
        var preview = CreateOrFail(SkinRamps.Source.Steps[0]);

        preview.Dispose();

        Assert.Throws<ObjectDisposedException>(() => preview.RenderIdleRow(SkinRamps.Source, 1));
    }

    [Fact]
    public void Create_ReportsLayerNotFound_WhenAPartialIsMissing()
    {
        var created = PalettePreview.Create(new SheetRecipe
        {
            Name = "absent",
            Layers = [_directory.FullPath / "nope.png"],
        });

        Assert.False(created.IsSuccessful);
        Assert.Equal(BakeFailure.LayerNotFound, created.Error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~PalettePreviewTests"`

Expected: **compile error** — `PalettePreview` does not exist.

- [ ] **Step 3: Expose `RecipeBaker.AssembleLayers`**

`RecipeBaker.Bake` already decodes layers and composites them, then goes straight on to recolour and encode. The preview needs the first half only. Extract it rather than duplicating the decode loop.

In `src/TheOmenDen.PixelForge.Core/Baking/SheetRecipe.cs`, replace the body of `Bake` and add the new public method:

```csharp
    public static Result<RecyclableMemoryStream, BakeFailure> Bake(SheetRecipe recipe)
    {
        Guard.IsNotNull(recipe);

        var assembly = AssembleLayers(recipe);

        if (!assembly.TryGet(out var assembled))
        {
            return new(assembly.Error);
        }

        using (assembled)
        {
            return Finish(assembled, recipe);
        }
    }

    /// <summary>
    /// Decodes a recipe's layers and composites them, stopping before the recolour. Overlays are
    /// deliberately not applied — they belong after the substitution.
    /// <para>
    /// Exposed for the palette preview, which needs the assembly but neither the recolour nor an
    /// encode. Sharing this is what keeps the decode-and-validate loop in one place.
    /// </para>
    /// </summary>
    public static Result<SKBitmap, BakeFailure> AssembleLayers(SheetRecipe recipe)
    {
        Guard.IsNotNull(recipe);

        if (recipe.Layers.IsDefaultOrEmpty)
        {
            return new(BakeFailure.NoLayersSupplied);
        }

        var loaded = new List<SKBitmap>(recipe.Layers.Length);

        try
        {
            foreach (var path in recipe.Layers)
            {
                if (!File.Exists(path.Value))
                {
                    return new(BakeFailure.LayerNotFound);
                }

                // No format pinning needed here: layers are only ever read by SKCanvas, which
                // handles any colour type. Assemble is what returns canonical pixels.
                var layer = SKBitmap.Decode(path.Value);

                if (layer is null)
                {
                    return new(BakeFailure.LayerUnreadable);
                }

                loaded.Add(layer);
            }

            return SheetBaker.Assemble(loaded);
        }
        finally
        {
            foreach (var layer in loaded)
            {
                layer.Dispose();
            }
        }
    }
```

- [ ] **Step 4: Write `PalettePreview`**

Create `src/TheOmenDen.PixelForge.Core/Palettes/PalettePreview.cs`:

```csharp
using DotNext;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// Renders a recoloured sprite for the palette editor, fast enough to keep up with a colour
/// picker being dragged.
/// <para>
/// The body is baked <em>once</em>, un-recoloured, and the curated result is cached. Each render
/// then applies only the five-colour substitution, so changing a ramp step costs a pass over one
/// small crop rather than a decode, composite and curate.
/// </para>
/// <para>
/// The recolour happens <em>after</em> the curate here, the reverse of the export path. Both
/// operations are pixel-local, so they commute — the output is identical, and cropping first
/// means the substitution walks 6,912 pixels instead of 211,968.
/// </para>
/// <para>
/// Upscaling is nearest-neighbour, because WinUI 3's <c>Image</c> has no interpolation-mode
/// switch: scaling here is the only way to stop the platform blurring pixel art.
/// </para>
/// </summary>
public sealed class PalettePreview : Disposable
{
    /// <summary>Frame 0 of the idle clip on all three facings — the three faces, side by side.</summary>
    public const int IdleRowWidth = SheetLayout.CellSize * SheetLayout.FacingCount;

    public const int IdleRowHeight = SheetLayout.CellSize;

    private readonly SKBitmap _curated;

    private PalettePreview(SKBitmap curated) => _curated = curated;

    /// <summary>Nearest with no mipmapping — a scaled draw must never blur pixel art.</summary>
    private static SKSamplingOptions PixelExact => new(SKFilterMode.Nearest, SKMipmapMode.None);

    private static SKImageInfo CanonicalInfo(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    /// <summary>
    /// Index of the idle clip. Looked up by name rather than hard-coded, so reordering
    /// <see cref="SheetLayout.Clips"/> cannot silently point the preview at a different animation.
    /// </summary>
    private static int IdleClipIndex { get; } = FindClip("idle");

    private static int FindClip(string name)
    {
        for (var i = 0; i < SheetLayout.Clips.Length; i++)
        {
            if (SheetLayout.Clips[i].Name == name)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Bakes <paramref name="body"/> without its recolour and caches the curated sheet.
    /// <see cref="SheetRecipe.Recolor"/> is ignored on purpose: the cache must hold source-toned
    /// pixels so any ramp can be substituted in later.
    /// </summary>
    public static Result<PalettePreview, BakeFailure> Create(SheetRecipe body)
    {
        var assembly = RecipeBaker.AssembleLayers(body);

        if (!assembly.TryGet(out var assembled))
        {
            return new(assembly.Error);
        }

        using (assembled)
        {
            var curation = SheetBaker.Curate(assembled);

            if (!curation.TryGet(out var curated))
            {
                return new(curation.Error);
            }

            return new PalettePreview(curated);
        }
    }

    /// <summary>
    /// The idle frame on all three facings, recoloured into <paramref name="ramp"/> and scaled up
    /// by <paramref name="scale"/>. The returned bitmap is the caller's to dispose.
    /// </summary>
    public Result<SKBitmap, BakeFailure> RenderIdleRow(SkinRamp ramp, int scale)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Guard.IsNotNull(ramp);
        Guard.IsGreaterThan(scale, 0);

        var cropped = CropIdleRow();

        if (!cropped.TryGet(out var crop))
        {
            return cropped;
        }

        SKBitmap? toned = null;

        try
        {
            using (crop)
            {
                var recolored = SheetBaker.Recolor(crop, ramp.SubstitutionFrom(SkinRamps.Source));

                if (!recolored.TryGet(out toned))
                {
                    return new(recolored.Error);
                }
            }

            if (scale is 1)
            {
                var result = toned;

                toned = null;

                return result;
            }

            return Upscale(toned, scale);
        }
        finally
        {
            toned?.Dispose();
        }
    }

    /// <summary>
    /// Copies frame 0 of the idle clip from each facing row into a three-cell strip. Drawn with
    /// <see cref="SKCanvas"/> rather than hand-rolled stride arithmetic.
    /// </summary>
    private Result<SKBitmap, BakeFailure> CropIdleRow()
    {
        using var strip = new SKBitmap(new SKImageInfo(
            IdleRowWidth, IdleRowHeight, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(strip))
        {
            canvas.Clear(SKColors.Transparent);

            for (var facing = 0; facing < SheetLayout.FacingCount; facing++)
            {
                var sourceRow = SheetLayout.RowFor(IdleClipIndex, facing);

                var source = SKRect.Create(
                    0,
                    sourceRow * SheetLayout.CellSize,
                    SheetLayout.CellSize,
                    SheetLayout.CellSize);

                var destination = SKRect.Create(
                    facing * SheetLayout.CellSize,
                    0,
                    SheetLayout.CellSize,
                    SheetLayout.CellSize);

                canvas.DrawBitmap(_curated, source, destination, PixelExact);
            }
        }

        return SheetBaker.ToCanonical(strip);
    }

    private static Result<SKBitmap, BakeFailure> Upscale(SKBitmap source, int scale)
    {
        using var scaled = new SKBitmap(new SKImageInfo(
            source.Width * scale, source.Height * scale, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                source,
                SKRect.Create(0, 0, source.Width, source.Height),
                SKRect.Create(0, 0, scaled.Width, scaled.Height),
                PixelExact);
        }

        return SheetBaker.ToCanonical(scaled);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _curated.Dispose();
        }

        base.Dispose(disposing);
    }
}
```

Add `using CommunityToolkit.Diagnostics;` for `Guard`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~PalettePreviewTests"`

Expected: 7 passed.

If `RenderIdleRow_UpscalesWithoutInterpolating` fails with a gradient, `PixelExact` is not reaching the draw — confirm the `SKSamplingOptions` overload of `DrawBitmap` is the one being bound, not an `SKPaint` overload.

- [ ] **Step 6: Run the full build and test suite**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: all green. `RecipeBakerOverlayTests` from Task 1 must still pass — `Bake` was refactored, not changed in behaviour.

- [ ] **Step 7: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Palettes/PalettePreview.cs src/TheOmenDen.PixelForge.Core/Baking/SheetRecipe.cs tests/TheOmenDen.PixelForge.Core.Tests/Palettes/PalettePreviewTests.cs
git commit -m "feat(core): add cached live palette preview"
```

---

## Phase C — App: foundation

Core is complete and proved after Task 7. Everything from here needs a window.

### Task 8: One LocalState path

`App.LogDirectory` already carries the only packaged/unpackaged branch in the codebase. Ramps and pack settings need the same directory, and duplicating that branch twice more is how the three drift apart.

**Files:**
- Create: `src/TheOmenDen.PixelForge/Services/AppPaths.cs`
- Modify: `src/TheOmenDen.PixelForge/App.xaml.cs`

**Interfaces:**
- Produces: `AppPaths.LocalState` → `FullPath`, `AppPaths.Logs` → `FullPath`, `AppPaths.RampStoreFile` → `FullPath`, `AppPaths.PackSettingsFile` → `FullPath`, `AppPaths.IsPackaged` → `bool`. Tasks 9 and 11 consume these.

- [ ] **Step 1: Write `AppPaths`**

Create `src/TheOmenDen.PixelForge/Services/AppPaths.cs`:

```csharp
using Meziantou.Framework;
using Windows.ApplicationModel;
using Windows.Storage;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Where the app keeps writable state.
/// <para>
/// A packaged app's install directory is read-only, so everything writable goes to LocalState.
/// This is the single place that branch is made — <c>ApplicationData.Current</c> throws without
/// package identity, and the "Unpackaged" launch profile has none.
/// </para>
/// <para>
/// Deliberately not <c>ApplicationData.Current.LocalSettings</c>: it has the same identity
/// requirement, so a settings API would need this same branch anyway. Plain files under one
/// directory keep both launch modes on one path.
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// <c>Package.Current</c> throws when the app runs without package identity, which is the
    /// only reliable way to detect it.
    /// </summary>
    public static bool IsPackaged
    {
        get
        {
            try
            {
                return Package.Current is not null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A static property initialiser runs once, which is all the caching this needs — no
    /// <c>Lazy&lt;T&gt;</c> (banned) and no hand-rolled null check.
    /// </summary>
    public static FullPath LocalState { get; } = FullPath.FromPath(IsPackaged
        ? ApplicationData.Current.LocalFolder.Path
        : AppContext.BaseDirectory);

    public static FullPath Logs => LocalState / "logs";

    public static FullPath RampStoreFile => LocalState / "ramps.csv";

    public static FullPath PackSettingsFile => LocalState / "packs.json";
}
```

- [ ] **Step 2: Point `App` at it**

In `src/TheOmenDen.PixelForge/App.xaml.cs`, delete the `LogDirectory` property and the `IsPackaged` property, and replace the file-sink path expression:

```csharp
            .WriteTo.Async(sink => sink.File(
                new CompactJsonFormatter(),
                Path.Combine(AppPaths.Logs.Value, "pixelforge-.log"),
```

and the startup log line:

```csharp
        Log.Information("PixelForge starting. Logs: {LogDirectory}", AppPaths.Logs);
```

Remove the now-unused `using Windows.ApplicationModel;` and `using Windows.Storage;` from `App.xaml.cs`.

- [ ] **Step 3: Build and run the app to confirm logs still land**

Run: `dotnet build TheOmenDen.PixelForge.slnx`

Then: `dotnet run --project src/TheOmenDen.PixelForge`

Close the window (do **not** `taskkill` — `Sinks.Async` buffers, and `Log.CloseAndFlush()` on window close is what persists the final events).

Confirm a `pixelforge-<date>.log` exists under `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\logs\`.

- [ ] **Step 4: Run the test suite**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

- [ ] **Step 5: Commit**

```bash
git add src/TheOmenDen.PixelForge/Services/AppPaths.cs src/TheOmenDen.PixelForge/App.xaml.cs
git commit -m "refactor(app): hoist the LocalState branch into AppPaths"
```

---

### Task 9: Source pack settings

**Files:**
- Create: `src/TheOmenDen.PixelForge/Services/PackSettings.cs`
- Create: `src/TheOmenDen.PixelForge/Services/SourcePackService.cs`
- Modify: `src/TheOmenDen.PixelForge/App.xaml.cs` (register)

**Interfaces:**
- Consumes: `AppPaths.PackSettingsFile` (Task 8), `SourcePacks`, `ElementsPack` (existing Core types).
- Produces:
  - `SourcePackService.Core` / `.Expansion1` / `.Expansion2` → `Optional<FullPath>`
  - `SourcePackService.Resolved` → `Optional<SourcePacks>` — present only when all three are set **and** exist on disk
  - `SourcePackService.Set(ElementsPack pack, FullPath path)`
  - `SourcePackService.Changed` → `event EventHandler?`

  Tasks 12, 16 and 17 bind to these.

- [ ] **Step 1: Write `PackSettings`**

Create `src/TheOmenDen.PixelForge/Services/PackSettings.cs`:

```csharp
using System.Text.Json.Serialization;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// The three pack directories as persisted. Plain strings rather than <c>FullPath</c>: the
/// source-generated serialiser needs no converter for a string, and the conversion belongs at
/// the service boundary where a path can also be validated.
/// </summary>
public sealed record PackSettings
{
    public string? CoreAssets { get; init; }

    public string? Expansion1Assets { get; init; }

    public string? Expansion2Assets { get; init; }
}

/// <summary>
/// Source-generated context. The reflection-based <c>JsonSerializer</c> overloads are banned —
/// they are trim-unsafe and this app publishes with <c>PublishTrimmed=true</c>.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PackSettings))]
internal sealed partial class PackSettingsContext : JsonSerializerContext;
```

- [ ] **Step 2: Write `SourcePackService`**

Create `src/TheOmenDen.PixelForge/Services/SourcePackService.cs`:

```csharp
using System.Text.Json;
using DotNext;
using Meziantou.Framework;
using Microsoft.Extensions.Logging;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Holds the three Time Elements pack directories and persists them to LocalState.
/// <para>
/// The packs live outside every repo — a directory of raw per-slot partials <em>is</em> the
/// asset pack, which the licence does not let us redistribute. So the app cannot ship them and
/// has to be told where they are.
/// </para>
/// <para>
/// No interface: there is one implementation and nothing mocks it. The concrete class is
/// registered directly.
/// </para>
/// </summary>
public sealed class SourcePackService(ILogger<SourcePackService> logger)
{
    public Optional<FullPath> Core { get; private set; } = Optional<FullPath>.None;

    public Optional<FullPath> Expansion1 { get; private set; } = Optional<FullPath>.None;

    public Optional<FullPath> Expansion2 { get; private set; } = Optional<FullPath>.None;

    /// <summary>Raised whenever a path changes, so pages can re-evaluate whether export is possible.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The packs, but only when all three are set and still on disk. A pack directory that was
    /// deleted since it was configured must not produce a <see cref="SourcePacks"/> that fails
    /// 79 times during a batch run.
    /// </summary>
    public Optional<SourcePacks> Resolved
    {
        get
        {
            if (!Core.TryGet(out var core)
                || !Expansion1.TryGet(out var expansion1)
                || !Expansion2.TryGet(out var expansion2))
            {
                return Optional<SourcePacks>.None;
            }

            if (!Directory.Exists(core.Value)
                || !Directory.Exists(expansion1.Value)
                || !Directory.Exists(expansion2.Value))
            {
                return Optional<SourcePacks>.None;
            }

            return new SourcePacks
            {
                CoreAssets = core,
                Expansion1Assets = expansion1,
                Expansion2Assets = expansion2,
            };
        }
    }

    public void Set(ElementsPack pack, FullPath path)
    {
        switch (pack)
        {
            case ElementsPack.Core:
                Core = path;
                break;
            case ElementsPack.CharacterExpansion1:
                Expansion1 = path;
                break;
            case ElementsPack.CharacterExpansion2:
                Expansion2 = path;
                break;
            default:
                return;
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Load()
    {
        var file = AppPaths.PackSettingsFile;

        if (!File.Exists(file.Value))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(file.Value);

            var settings = JsonSerializer.Deserialize(stream, PackSettingsContext.Default.PackSettings);

            if (settings is null)
            {
                return;
            }

            Core = ToPath(settings.CoreAssets);
            Expansion1 = ToPath(settings.Expansion1Assets);
            Expansion2 = ToPath(settings.Expansion2Assets);

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt settings file must not stop the app starting — the user can re-pick.
            logger.LogWarning(exception, "Could not read pack settings from {File}", file);
        }
    }

    private void Save()
    {
        var file = AppPaths.PackSettingsFile;

        try
        {
            Directory.CreateDirectory(AppPaths.LocalState.Value);

            using var stream = File.Create(file.Value);

            JsonSerializer.Serialize(stream, new PackSettings
            {
                CoreAssets = Core.TryGet(out var core) ? core.Value : null,
                Expansion1Assets = Expansion1.TryGet(out var one) ? one.Value : null,
                Expansion2Assets = Expansion2.TryGet(out var two) ? two.Value : null,
            }, PackSettingsContext.Default.PackSettings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not write pack settings to {File}", file);
        }
    }

    private static Optional<FullPath> ToPath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Optional<FullPath>.None : FullPath.FromPath(value);
}
```

- [ ] **Step 3: Register it and load at startup**

In `src/TheOmenDen.PixelForge/App.xaml.cs`, add to `BuildHost` beside the existing registrations:

```csharp
        builder.Services.AddSingleton<SourcePackService>();
```

and in `OnLaunched`, before the window is created:

```csharp
        Services.GetRequiredService<SourcePackService>().Load();
```

- [ ] **Step 4: Build**

Run: `dotnet build TheOmenDen.PixelForge.slnx`

Expected: succeeds. A `JsonSerializer.Serialize`/`Deserialize` call taking a `JsonTypeInfo<T>` is the source-generated overload, so RS0030 does not fire.

- [ ] **Step 5: Commit**

```bash
git add src/TheOmenDen.PixelForge/Services/PackSettings.cs src/TheOmenDen.PixelForge/Services/SourcePackService.cs src/TheOmenDen.PixelForge/App.xaml.cs
git commit -m "feat(app): persist the three source pack directories"
```

---

### Task 10: Folder and file pickers

**Files:**
- Create: `src/TheOmenDen.PixelForge/Services/PickerService.cs`
- Modify: `src/TheOmenDen.PixelForge/App.xaml.cs` (register, cache `WindowId`)
- Modify: `src/TheOmenDen.PixelForge/MainWindow.xaml.cs` (publish `WindowId`)

**Interfaces:**
- Produces:
  - `PickerService.WindowId` → `WindowId` (settable once at startup)
  - `PickerService.PickFolderAsync()` → `Task<Optional<FullPath>>`
  - `PickerService.PickOpenFileAsync(string extension)` → `Task<Optional<FullPath>>`
  - `PickerService.PickSaveFileAsync(string suggestedName, string extension, string filterName)` → `Task<Optional<FullPath>>`

  Tasks 12, 13 and 16 call these.

- [ ] **Step 1: Write `PickerService`**

Create `src/TheOmenDen.PixelForge/Services/PickerService.cs`:

```csharp
using DotNext;
using Meziantou.Framework;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// File and folder pickers.
/// <para>
/// <c>Microsoft.Windows.Storage.Pickers</c>, not <c>Windows.Storage.Pickers</c>. The legacy WinRT
/// pickers need <c>WinRT.Interop.InitializeWithWindow</c> and then silently display no dialog at
/// all in a packaged build even when that call succeeds — the classic "save button does nothing
/// once installed" bug. The WinAppSDK replacement takes a <see cref="WindowId"/> and behaves
/// identically packaged and unpackaged.
/// </para>
/// <para>
/// Results come back as plain filesystem paths, so everything downstream is
/// <see cref="FullPath"/> and <c>System.IO</c> — no <c>StorageFile</c> round trip.
/// </para>
/// </summary>
public sealed class PickerService
{
    /// <summary>
    /// Set once, from <c>MainWindow</c>'s constructor. ViewModels have no XAML sender to pull a
    /// <c>XamlRoot</c> from, so the id is cached rather than passed at every call site.
    /// </summary>
    public WindowId WindowId { get; set; }

    public async Task<Optional<FullPath>> PickFolderAsync()
    {
        var picker = new FolderPicker(WindowId)
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        var result = await picker.PickSingleFolderAsync();

        return result is null ? Optional<FullPath>.None : FullPath.FromPath(result.Path);
    }

    public async Task<Optional<FullPath>> PickOpenFileAsync(string extension)
    {
        var picker = new FileOpenPicker(WindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        // FileTypeFilter must have at least one entry or the dialog throws.
        picker.FileTypeFilter.Add(extension);

        var result = await picker.PickSingleFileAsync();

        return result is null ? Optional<FullPath>.None : FullPath.FromPath(result.Path);
    }

    public async Task<Optional<FullPath>> PickSaveFileAsync(
        string suggestedName,
        string extension,
        string filterName)
    {
        var picker = new FileSavePicker(WindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };

        picker.FileTypeChoices.Add(filterName, [extension]);

        var result = await picker.PickSaveFileAsync();

        return result is null ? Optional<FullPath>.None : FullPath.FromPath(result.Path);
    }
}
```

- [ ] **Step 2: Register it and publish the WindowId**

In `App.xaml.cs` `BuildHost`:

```csharp
        builder.Services.AddSingleton<PickerService>();
```

In `MainWindow.xaml.cs`, in the constructor after `AppWindow.SetIcon(...)`:

```csharp
        // ViewModels have no XAML sender to derive a WindowId from, so it is published once here.
        App.Services.GetRequiredService<PickerService>().WindowId = AppWindow.Id;
```

- [ ] **Step 3: Build**

Run: `dotnet build TheOmenDen.PixelForge.slnx`

If `Microsoft.Windows.Storage.Pickers` does not resolve, confirm the WinAppSDK Foundation package is restored — the projection ships in `microsoft.windowsappsdk.foundation`, not the metapackage. No new `PackageReference` is needed.

- [ ] **Step 4: Commit**

```bash
git add src/TheOmenDen.PixelForge/Services/PickerService.cs src/TheOmenDen.PixelForge/App.xaml.cs src/TheOmenDen.PixelForge/MainWindow.xaml.cs
git commit -m "feat(app): add packaged-safe folder and file pickers"
```

---

### Task 11: Ramp service

**Files:**
- Create: `src/TheOmenDen.PixelForge/Services/RampService.cs`
- Modify: `src/TheOmenDen.PixelForge/App.xaml.cs` (register)

**Interfaces:**
- Consumes: `RampStore`, `RampFailure`, `SkinRamp`, `SkinRamps` (Task 6); `AppPaths.RampStoreFile` (Task 8).
- Produces:
  - `RampService.Ramps` → `ObservableCollection<SkinRamp>` — **the single source**, built-ins first, wrapped by Task 13's `AdvancedCollectionView`
  - `RampService.BuiltIn` → `ImmutableArray<SkinRamp>`
  - `RampService.Custom` → `IEnumerable<SkinRamp>` (computed: everything in `Ramps` that is not built-in)
  - `RampService.IsBuiltIn(SkinRamp)` → `bool`
  - `RampService.Load()` → `Result<int, RampFailure>`
  - `RampService.Save()` → `Result<int, RampFailure>`
  - `RampService.Add(SkinRamp)` / `Replace(string name, SkinRamp)` / `Remove(string name)` → `Result<int, RampFailure>`
  - `RampService.Import(FullPath)` / `Export(FullPath)` → `Result<int, RampFailure>`

  Task 13 binds to all of these.

- [ ] **Step 1: Write `RampService`**

Create `src/TheOmenDen.PixelForge/Services/RampService.cs`:

```csharp
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using DotNext;
using Meziantou.Framework;
using Microsoft.Extensions.Logging;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// The ramps the app knows about: the seven shipped ones plus whatever the user has added.
/// <para>
/// Built-ins are the Corvus contract and are never written to the store or edited in place —
/// editing one is offered as "duplicate to edit" in the UI. Names identify a ramp, and
/// uniqueness is enforced across both sets so a custom cannot shadow a built-in.
/// </para>
/// </summary>
public sealed class RampService(ILogger<RampService> logger)
{
    private readonly RampStore _store = new(AppPaths.RampStoreFile);

    /// <summary>
    /// Every ramp, built-ins first. <strong>One</strong> collection, not a built-in array plus a
    /// custom collection plus a computed concatenation — this is the stable source an
    /// <c>AdvancedCollectionView</c> observes, and a view over a collection that is rebuilt on
    /// every change is a view that loses its selection on every change.
    /// </summary>
    public ObservableCollection<SkinRamp> Ramps { get; } = [.. SkinRamps.All];

    public ImmutableArray<SkinRamp> BuiltIn => SkinRamps.All;

    /// <summary>The user's ramps — everything that is not shipped.</summary>
    public IEnumerable<SkinRamp> Custom
    {
        get
        {
            foreach (var ramp in Ramps)
            {
                if (!IsBuiltIn(ramp))
                {
                    yield return ramp;
                }
            }
        }
    }

    public bool IsBuiltIn(SkinRamp ramp) =>
        BuiltIn.AsSpan().Any(r => string.Equals(r.Name, ramp.Name, StringComparison.OrdinalIgnoreCase));

    public Result<int, RampFailure> Load()
    {
        var loaded = _store.Load();

        if (!loaded.TryGet(out var ramps))
        {
            logger.LogWarning("Could not load custom ramps: {Failure}", loaded.Error);

            return new(loaded.Error);
        }

        // Drop only the customs — the built-ins stay put so the view never sees an empty list.
        for (var i = Ramps.Count - 1; i >= 0; i--)
        {
            if (!IsBuiltIn(Ramps[i]))
            {
                Ramps.RemoveAt(i);
            }
        }

        foreach (var ramp in ramps)
        {
            Ramps.Add(ramp);
        }

        return ramps.Length;
    }

    public Result<int, RampFailure> Save() => _store.Save([.. Custom]);

    public Result<int, RampFailure> Add(SkinRamp ramp)
    {
        var rejected = Validate(ramp, replacing: null);

        if (rejected.HasValue)
        {
            return new(rejected.Value);
        }

        Ramps.Add(ramp);

        return Save();
    }

    /// <summary>Replaces the custom ramp called <paramref name="name"/> — the rename path too.</summary>
    public Result<int, RampFailure> Replace(string name, SkinRamp ramp)
    {
        var index = IndexOfCustom(name);

        if (index < 0)
        {
            return new(RampFailure.NotFound);
        }

        var rejected = Validate(ramp, replacing: name);

        if (rejected.HasValue)
        {
            return new(rejected.Value);
        }

        Ramps[index] = ramp;

        return Save();
    }

    public Result<int, RampFailure> Remove(string name)
    {
        var index = IndexOfCustom(name);

        if (index < 0)
        {
            return new(RampFailure.NotFound);
        }

        Ramps.RemoveAt(index);

        return Save();
    }

    /// <summary>Merges a CSV in. Existing names are replaced rather than duplicated.</summary>
    public Result<int, RampFailure> Import(FullPath file)
    {
        var imported = new RampStore(file).Load();

        if (!imported.TryGet(out var ramps))
        {
            return new(imported.Error);
        }

        var added = 0;

        foreach (var ramp in ramps)
        {
            if (IsBuiltIn(ramp))
            {
                // A built-in's name is taken. Skip rather than fail the whole import.
                logger.LogInformation("Skipped imported ramp {Name}: name is a built-in", ramp.Name);
                continue;
            }

            var index = IndexOfCustom(ramp.Name);

            if (index >= 0)
            {
                Ramps[index] = ramp;
            }
            else
            {
                Ramps.Add(ramp);
            }

            added++;
        }

        var saved = Save();

        return saved.IsSuccessful ? added : new(saved.Error);
    }

    public Result<int, RampFailure> Export(FullPath file) => new RampStore(file).Save([.. Custom]);

    /// <summary>Index into <see cref="Ramps"/>, or -1. Built-ins are never a match.</summary>
    private int IndexOfCustom(string name)
    {
        for (var i = 0; i < Ramps.Count; i++)
        {
            if (!IsBuiltIn(Ramps[i]) && string.Equals(Ramps[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Null means acceptable.</summary>
    private RampFailure? Validate(SkinRamp ramp, string? replacing)
    {
        if (string.IsNullOrWhiteSpace(ramp.Name))
        {
            return RampFailure.NameEmpty;
        }

        if (ramp.Steps.Length != SkinRamps.StepCount)
        {
            return RampFailure.WrongStepCount;
        }

        if (IsBuiltIn(ramp))
        {
            return RampFailure.DuplicateName;
        }

        var clash = IndexOfCustom(ramp.Name);

        if (clash >= 0 && !string.Equals(Ramps[clash].Name, replacing, StringComparison.OrdinalIgnoreCase))
        {
            return RampFailure.DuplicateName;
        }

        return null;
    }
}
```

- [ ] **Step 2: Register it and load at startup**

In `App.xaml.cs` `BuildHost`:

```csharp
        builder.Services.AddSingleton<RampService>();
```

In `OnLaunched`, beside the pack-settings load:

```csharp
        Services.GetRequiredService<RampService>().Load();
```

- [ ] **Step 3: Build**

Run: `dotnet build TheOmenDen.PixelForge.slnx`

`BuiltIn.AsSpan().Any(...)` is deliberate: `ImmutableArray<T>` is not one of the types the ZLinq drop-in generator covers, so a bare `.Any(...)` would bind to System.Linq. A span is covered.

- [ ] **Step 4: Commit**

```bash
git add src/TheOmenDen.PixelForge/Services/RampService.cs src/TheOmenDen.PixelForge/App.xaml.cs
git commit -m "feat(app): add ramp service over built-ins and custom ramps"
```

---

### Task 12: Settings page with pack directories

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/TheOmenDen.PixelForge/TheOmenDen.PixelForge.csproj`
- Modify: `src/TheOmenDen.PixelForge/ViewModels/SettingsViewModel.cs`
- Modify: `src/TheOmenDen.PixelForge/Views/SettingsPage.xaml{,.cs}`

**Interfaces:**
- Consumes: `SourcePackService` (Task 9), `PickerService` (Task 10), existing `IThemeService`.
- Produces: `SettingsViewModel.CorePackPath` / `Expansion1PackPath` / `Expansion2PackPath` → `string`; `BrowseCorePackCommand` / `BrowseExpansion1PackCommand` / `BrowseExpansion2PackCommand`; `AllPacksResolved` → `bool`.

- [ ] **Step 1: Add the toolkit packages**

All six are used across Tasks 12–17. Adding them in one step keeps the restore churn to a single commit. Every version is the `8.3.260402-preview2` train the existing seven toolkit packages already sit on — verified published on nuget.org, not guessed.

In `Directory.Packages.props`, add to the MVVM / UI toolkit `ItemGroup`:

```xml
    <PackageVersion Include="CommunityToolkit.WinUI.Collections" Version="8.3.260402-preview2" />
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.ColorPicker" Version="8.3.260402-preview2" />
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.Segmented" Version="8.3.260402-preview2" />
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.SettingsControls" Version="8.3.260402-preview2" />
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.Sizers" Version="8.3.260402-preview2" />
```

In `src/TheOmenDen.PixelForge/TheOmenDen.PixelForge.csproj`, add to the WinUI toolkit `ItemGroup`:

```xml
    <PackageReference Include="CommunityToolkit.WinUI.Collections" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.ColorPicker" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.Segmented" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.SettingsControls" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.Sizers" />
```

No `Version=` attribute on a `PackageReference` — that is a restore error under CPM.

`CommunityToolkit.WinUI.Behaviors` needs no change: it is **already** in both files. It was simply never used, which is what Tasks 15 and 17 fix.

All five controls packages project into the single namespace `CommunityToolkit.WinUI.Controls`, so one `xmlns:controls="using:CommunityToolkit.WinUI.Controls"` covers `SettingsCard`, `ColorPickerButton`, `Segmented`, `PropertySizer` and `GridSplitter` together.

- [ ] **Step 1b: Verify the restore**

Run: `dotnet restore TheOmenDen.PixelForge.slnx`

Expected: succeeds with no NU1010 (missing PackageVersion) and no NU1008 (Version on a PackageReference under CPM).

- [ ] **Step 2: Extend `SettingsViewModel`**

Replace `src/TheOmenDen.PixelForge/ViewModels/SettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly SourcePackService _packs;
    private readonly PickerService _picker;

    public SettingsViewModel(IThemeService themeService, SourcePackService packs, PickerService picker)
    {
        _themeService = themeService;
        _packs = packs;
        _picker = picker;

        _packs.Changed += OnPacksChanged;
    }

    /// <summary>
    /// Index into the theme Segmented: 0 = System, 1 = Light, 2 = Dark. Segmented binds
    /// SelectedIndex rather than the enum, so no converter is needed.
    /// </summary>
    public int SelectedThemeIndex
    {
        get => _themeService.Theme switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0,
        };
        set
        {
            var theme = value switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            if (theme == _themeService.Theme)
            {
                return;
            }

            _themeService.Apply(theme);
            OnPropertyChanged();
        }
    }

    public string CorePackPath => Describe(_packs.Core);

    public string Expansion1PackPath => Describe(_packs.Expansion1);

    public string Expansion2PackPath => Describe(_packs.Expansion2);

    /// <summary>Drives the batch page's blocking InfoBar, so it lives where the paths do.</summary>
    public bool AllPacksResolved => _packs.Resolved.HasValue;

    [RelayCommand]
    private Task BrowseCorePackAsync() => BrowseAsync(ElementsPack.Core);

    [RelayCommand]
    private Task BrowseExpansion1PackAsync() => BrowseAsync(ElementsPack.CharacterExpansion1);

    [RelayCommand]
    private Task BrowseExpansion2PackAsync() => BrowseAsync(ElementsPack.CharacterExpansion2);

    private async Task BrowseAsync(ElementsPack pack)
    {
        var picked = await _picker.PickFolderAsync();

        if (picked.TryGet(out var path))
        {
            _packs.Set(pack, path);
        }
    }

    private void OnPacksChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CorePackPath));
        OnPropertyChanged(nameof(Expansion1PackPath));
        OnPropertyChanged(nameof(Expansion2PackPath));
        OnPropertyChanged(nameof(AllPacksResolved));
    }

    private static string Describe(DotNext.Optional<Meziantou.Framework.FullPath> path) =>
        path.TryGet(out var value)
            ? Directory.Exists(value.Value) ? value.Value : $"{value.Value} (missing)"
            : "Not set";
}
```

**Note:** this drops the primary constructor because the type now needs a field for `_packs` to subscribe to `Changed`. A primary constructor parameter cannot be unsubscribed from in a handler, and capturing it into a field is the same amount of code with less indirection.

- [ ] **Step 3: Rewrite `SettingsPage.xaml`**

Replace the content of `src/TheOmenDen.PixelForge/Views/SettingsPage.xaml`. Keep the existing `x:Class` and root `Page` attributes, adding the toolkit namespace:

```xml
    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
```

Body:

```xml
    <ScrollViewer>
        <StackPanel Padding="24" Spacing="4">
            <StackPanel Margin="0,0,0,16" Spacing="4">
                <TextBlock Style="{StaticResource TitleTextBlockStyle}" Text="Settings" />
                <TextBlock
                    Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                    Text="Appearance and source asset locations." />
            </StackPanel>

            <TextBlock
                Margin="0,0,0,8"
                Style="{StaticResource BodyStrongTextBlockStyle}"
                Text="Appearance" />

            <controls:SettingsCard Description="Applies immediately." Header="App theme">
                <controls:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE771;" />
                </controls:SettingsCard.HeaderIcon>
                <!--  Three short mutually-exclusive toggles — the same case as the export mode.  -->
                <controls:Segmented
                    AutomationProperties.AutomationId="ThemeSelector"
                    AutomationProperties.Name="App theme"
                    SelectedIndex="{x:Bind ViewModel.SelectedThemeIndex, Mode=TwoWay}"
                    SelectionMode="Single">
                    <controls:SegmentedItem Content="System" />
                    <controls:SegmentedItem Content="Light" />
                    <controls:SegmentedItem Content="Dark" />
                </controls:Segmented>
            </controls:SettingsCard>

            <TextBlock
                Margin="0,24,0,8"
                Style="{StaticResource BodyStrongTextBlockStyle}"
                Text="Source packs" />

            <InfoBar
                x:Name="PacksHintBar"
                Margin="0,0,0,8"
                AutomationProperties.AutomationId="PacksHintBar"
                IsClosable="False"
                IsOpen="True"
                Message="Time Elements packs are licensed art and are not shipped with PixelForge. Point each row at that pack's assets folder."
                Severity="Informational" />

            <controls:SettingsCard Description="{x:Bind ViewModel.CorePackPath, Mode=OneWay}" Header="Core pack">
                <controls:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE8B7;" />
                </controls:SettingsCard.HeaderIcon>
                <Button
                    AutomationProperties.AutomationId="BtnBrowseCorePack"
                    AutomationProperties.Name="Browse for core pack folder"
                    Command="{x:Bind ViewModel.BrowseCorePackCommand}"
                    Content="Browse" />
            </controls:SettingsCard>

            <controls:SettingsCard Description="{x:Bind ViewModel.Expansion1PackPath, Mode=OneWay}" Header="Character Expansion 1">
                <controls:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE8B7;" />
                </controls:SettingsCard.HeaderIcon>
                <Button
                    AutomationProperties.AutomationId="BtnBrowseExpansion1Pack"
                    AutomationProperties.Name="Browse for expansion 1 folder"
                    Command="{x:Bind ViewModel.BrowseExpansion1PackCommand}"
                    Content="Browse" />
            </controls:SettingsCard>

            <controls:SettingsCard Description="{x:Bind ViewModel.Expansion2PackPath, Mode=OneWay}" Header="Character Expansion 2">
                <controls:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE8B7;" />
                </controls:SettingsCard.HeaderIcon>
                <Button
                    AutomationProperties.AutomationId="BtnBrowseExpansion2Pack"
                    AutomationProperties.Name="Browse for expansion 2 folder"
                    Command="{x:Bind ViewModel.BrowseExpansion2PackCommand}"
                    Content="Browse" />
            </controls:SettingsCard>
        </StackPanel>
    </ScrollViewer>
```

The `SettingsCard.Description` bindings carry the `AutomationId`s the spec named (`CorePackPath` and friends) via the card's own automation peer; if `ui-tests.ps1` cannot find them, add `AutomationProperties.AutomationId="CorePackPath"` to each `SettingsCard`.

- [ ] **Step 4: Confirm the code-behind exposes `ViewModel`**

`src/TheOmenDen.PixelForge/Views/SettingsPage.xaml.cs` must expose a `ViewModel` property for `x:Bind`. If it does not already:

```csharp
    public SettingsViewModel ViewModel { get; } = App.Services.GetRequiredService<SettingsViewModel>();
```

- [ ] **Step 5: Build and run**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet run --project src/TheOmenDen.PixelForge`

Navigate to Settings. Pick a folder for each pack; close and relaunch; confirm the paths persisted. Check the page in Light, Dark and HighContrast.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props src/TheOmenDen.PixelForge/TheOmenDen.PixelForge.csproj src/TheOmenDen.PixelForge/ViewModels/SettingsViewModel.cs src/TheOmenDen.PixelForge/Views/SettingsPage.xaml src/TheOmenDen.PixelForge/Views/SettingsPage.xaml.cs
git commit -m "feat(app): configure source pack directories from settings"
```

---

## Phase D — App: palette page

### Task 13: Palette view model

**Files:**
- Create: `src/TheOmenDen.PixelForge/ViewModels/PaletteViewModel.cs`
- Modify: `src/TheOmenDen.PixelForge/App.xaml.cs` (register)

**Interfaces:**
- Consumes: `RampService` (Task 11), `PickerService` (Task 10), `SourcePackService` (Task 9), `SkinRamp`, `SkinRamps`, `RampConversions.Hex` / `TryParseHex` (Task 6), `RoostSheets.Bodies`.
- Produces:
  - `RampStepViewModel { int Index; SKColor Color; Windows.UI.Color PickerColor; string Hex; string Label; }` — own file
  - `StatusLevel` enum — own file
  - `StatusNotice` readonly record struct — own file
  - `PaletteViewModel.RampView` → `AdvancedCollectionView`
  - `PaletteViewModel.SelectedRamp` → `SkinRamp?`
  - `PaletteViewModel.Steps` → `ObservableCollection<RampStepViewModel>`
  - `PaletteViewModel.EditedName` → `string`
  - `PaletteViewModel.IsEditable` / `IsBuiltInSelected` → `bool`
  - `PaletteViewModel.PreviewRamp` → `SkinRamp?` (what the view renders)
  - `PaletteViewModel.PreviewRecipe` → `Optional<SheetRecipe>`
  - `PaletteViewModel.Notified` → `event EventHandler<StatusNotice>?`
  - Commands: `NewRampCommand`, `DuplicateRampCommand`, `DeleteRampCommand`, `SaveRampCommand`, `ImportCommand`, `ExportCommand`

  Task 15 binds to all of these. See the **File Split Map** — the four types above go in four files.

**Note on types.** The view model works in `SKColor` throughout; `PickerColor` is a thin projection so `ColorPickerButton.SelectedColor` can two-way bind. `Windows.UI.Color` is a four-byte WinRT struct, not a `Microsoft.UI.*` type, and carries no dispatcher or window affinity — so the view model stays unit-testable while the toolkit control does the work a hand-rolled flyout, `Tag` and `ColorChanged` handler would otherwise have needed.

- [ ] **Step 1: Write `RampStepViewModel` and `PaletteViewModel`**

Create `src/TheOmenDen.PixelForge/ViewModels/PaletteViewModel.cs`:

```csharp
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNext;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// One editable step of a ramp. <see cref="Color"/> and <see cref="Hex"/> are two views of the
/// same value and each keeps the other in step, so typing a hex moves the swatch and the picker
/// moves the text.
/// </summary>
public sealed partial class RampStepViewModel(int index, SKColor color) : ObservableObject
{
    /// <summary>0 = darkest shadow, 4 = lightest highlight.</summary>
    public int Index { get; } = index;

    public string Label => $"Step {Index + 1}";

    /// <summary>Raised when either representation changes, so the preview can re-render.</summary>
    public event EventHandler? Changed;

    public SKColor Color
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(Hex));
            OnPropertyChanged(nameof(PickerColor));

            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = color;

    /// <summary>
    /// The same colour as <see cref="Color"/>, in the type <c>ColorPickerButton.SelectedColor</c>
    /// binds to.
    /// <para>
    /// <c>Windows.UI.Color</c> is a four-byte WinRT struct — no dispatcher, no XAML dependency, no
    /// window affinity — so it does not compromise this view model's testability. Exposing it is
    /// what lets the picker two-way bind directly and removes the <c>ColorChanged</c> handler,
    /// the <c>Tag</c>-carried step index, and the <c>SetStepColor</c> hop through the parent view
    /// model that the first draft needed.
    /// </para>
    /// </summary>
    public Windows.UI.Color PickerColor
    {
        get => Windows.UI.Color.FromArgb(Color.Alpha, Color.Red, Color.Green, Color.Blue);
        set => Color = new SKColor(value.R, value.G, value.B, value.A);
    }

    /// <summary>
    /// Round-trips through the store's own parser, so what the editor accepts is exactly what a
    /// saved file can contain. An unparseable value is ignored rather than throwing — the user is
    /// mid-keystroke, not wrong.
    /// </summary>
    public string Hex
    {
        get => RampConversions.Hex(Color);
        set
        {
            if (RampConversions.TryParseHex(value, out var parsed))
            {
                Color = parsed;
            }
            else
            {
                // Push the canonical form back so the TextBox does not keep invalid text.
                OnPropertyChanged();
            }
        }
    }
}

/// <summary>
/// How serious a status message is. A plain enum rather than <c>InfoBarSeverity</c>, which is a
/// <c>Microsoft.UI.*</c> type and would put XAML in the view models. The page maps it.
/// </summary>
public enum StatusLevel
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>One thing worth telling the user about.</summary>
public readonly record struct StatusNotice(string Message, StatusLevel Level);

/// <summary>
/// The palette editor. Built-ins are shown but not editable — selecting one offers Duplicate.
/// </summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private readonly RampService _ramps;
    private readonly PickerService _picker;
    private readonly SourcePackService _packs;

    public PaletteViewModel(RampService ramps, PickerService picker, SourcePackService packs)
    {
        _ramps = ramps;
        _picker = picker;
        _packs = packs;

        // AdvancedCollectionView observes the service's single collection directly, so adding or
        // deleting a ramp updates the list without a clear-and-rebuild — and therefore without
        // the selection-restore dance a rebuild forces.
        RampView = new AdvancedCollectionView(_ramps.Ramps, isLiveShaping: true);
        RampView.SortDescriptions.Add(new SortDescription(nameof(SkinRamp.Name), SortDirection.Ascending));

        SelectedRamp = _ramps.Ramps.Count > 0 ? _ramps.Ramps[0] : null;
    }

    /// <summary>Sorted, live-shaped view over every ramp. Bound directly as the ListView source.</summary>
    public AdvancedCollectionView RampView { get; }

    public ObservableCollection<RampStepViewModel> Steps { get; } = [];

    public SkinRamp? SelectedRamp
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

            field = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(IsBuiltInSelected));

            LoadSteps(value);

            EditedName = value?.Name ?? string.Empty;

            DeleteRampCommand.NotifyCanExecuteChanged();
            SaveRampCommand.NotifyCanExecuteChanged();
            DuplicateRampCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Editable name. Renaming a custom ramp is a Save of a differently-named ramp.</summary>
    public string EditedName
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            OnPropertyChanged();

            SaveRampCommand.NotifyCanExecuteChanged();
        }
    } = string.Empty;

    public bool IsBuiltInSelected => SelectedRamp is not null && _ramps.IsBuiltIn(SelectedRamp);

    public bool IsEditable => SelectedRamp is not null && !IsBuiltInSelected;

    /// <summary>
    /// What the preview renders: the selected ramp with the current, possibly unsaved, step
    /// edits applied. Rebuilt from <see cref="Steps"/> so dragging the picker updates the sprite
    /// before anything is committed.
    /// </summary>
    public SkinRamp? PreviewRamp
    {
        get
        {
            if (SelectedRamp is null || Steps.Count != SkinRamps.StepCount)
            {
                return SelectedRamp;
            }

            var steps = ImmutableArray.CreateBuilder<SKColor>(SkinRamps.StepCount);

            foreach (var step in Steps)
            {
                steps.Add(step.Color);
            }

            return SelectedRamp with { Steps = steps.ToImmutable() };
        }
    }

    /// <summary>
    /// The body recipe the preview bakes from. Absent until the packs are configured, which is
    /// what the page uses to show its hint instead of an empty frame.
    /// </summary>
    public Optional<SheetRecipe> PreviewRecipe
    {
        get
        {
            if (!_packs.Resolved.TryGet(out var packs))
            {
                return Optional<SheetRecipe>.None;
            }

            var bodies = RoostSheets.Bodies(packs);

            return bodies.Length > 0 ? bodies[0] : Optional<SheetRecipe>.None;
        }
    }

    /// <summary>
    /// Raised for anything worth telling the user. The page feeds these to a
    /// <c>StackedNotificationsBehavior</c>, which queues and auto-dismisses them — so a run that
    /// produces several messages shows all of them instead of clobbering one string.
    /// </summary>
    public event EventHandler<StatusNotice>? Notified;

    private void Notify(string message, StatusLevel level) =>
        Notified?.Invoke(this, new StatusNotice(message, level));

    [RelayCommand]
    private void NewRamp()
    {
        var ramp = new SkinRamp
        {
            Name = UniqueName("New Ramp"),
            IsHuman = false,
            Steps = SkinRamps.Source.Steps,
        };

        Apply(_ramps.Add(ramp), $"Created {ramp.Name}", () => SelectByName(ramp.Name));
    }

    [RelayCommand(CanExecute = nameof(CanDuplicate))]
    private void DuplicateRamp()
    {
        if (SelectedRamp is null)
        {
            return;
        }

        var copy = SelectedRamp with { Name = UniqueName($"{SelectedRamp.Name} copy") };

        Apply(_ramps.Add(copy), $"Duplicated to {copy.Name}", () => SelectByName(copy.Name));
    }

    private bool CanDuplicate() => SelectedRamp is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void DeleteRamp()
    {
        if (SelectedRamp is null)
        {
            return;
        }

        var name = SelectedRamp.Name;

        Apply(_ramps.Remove(name), $"Deleted {name}", () => SelectedRamp = Ramps.Count > 0 ? Ramps[0] : null);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveRamp()
    {
        if (SelectedRamp is null)
        {
            return;
        }

        var steps = ImmutableArray.CreateBuilder<SKColor>(SkinRamps.StepCount);

        foreach (var step in Steps)
        {
            steps.Add(step.Color);
        }

        var edited = SelectedRamp with
        {
            Name = EditedName.Trim(),
            Steps = steps.ToImmutable(),
        };

        Apply(_ramps.Replace(SelectedRamp.Name, edited), $"Saved {edited.Name}", () => SelectByName(edited.Name));
    }

    private bool CanSave() => IsEditable && !string.IsNullOrWhiteSpace(EditedName);

    private bool CanEditSelection() => IsEditable;

    [RelayCommand]
    private async Task ImportAsync()
    {
        var picked = await _picker.PickOpenFileAsync(".csv");

        if (!picked.TryGet(out var file))
        {
            return;
        }

        var imported = _ramps.Import(file);

        if (imported.TryGet(out var count))
        {
            Notify($"Imported {count} ramp(s).", StatusLevel.Success);
        }
        else
        {
            Notify($"Import failed: {imported.Error}.", StatusLevel.Error);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var picked = await _picker.PickSaveFileAsync("ramps", ".csv", "Palette CSV");

        if (!picked.TryGet(out var file))
        {
            return;
        }

        var exported = _ramps.Export(file);

        if (exported.TryGet(out var count))
        {
            Notify($"Exported {count} ramp(s).", StatusLevel.Success);
        }
        else
        {
            Notify($"Export failed: {exported.Error}.", StatusLevel.Error);
        }
    }

    private void Apply(Result<int, RampFailure> result, string success, Action onSuccess)
    {
        if (result.IsSuccessful)
        {
            Notify(success, StatusLevel.Success);
            onSuccess();
        }
        else
        {
            Notify($"Failed: {result.Error}.", StatusLevel.Error);
        }
    }

    private void SelectByName(string name)
    {
        foreach (var ramp in _ramps.Ramps)
        {
            if (string.Equals(ramp.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                SelectedRamp = ramp;
                return;
            }
        }
    }

    private string UniqueName(string proposed)
    {
        var candidate = proposed;
        var suffix = 2;

        while (Exists(candidate))
        {
            candidate = $"{proposed} {suffix++}";
        }

        return candidate;
    }

    private bool Exists(string name)
    {
        foreach (var ramp in _ramps.Ramps)
        {
            if (string.Equals(ramp.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void LoadSteps(SkinRamp? ramp)
    {
        foreach (var step in Steps)
        {
            step.Changed -= OnStepChanged;
        }

        Steps.Clear();

        if (ramp is not null)
        {
            for (var i = 0; i < ramp.Steps.Length; i++)
            {
                var step = new RampStepViewModel(i, ramp.Steps[i]);

                step.Changed += OnStepChanged;

                Steps.Add(step);
            }
        }

        OnPropertyChanged(nameof(PreviewRamp));
    }

    private void OnStepChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(PreviewRamp));
}
```

- [ ] **Step 2: Register it**

In `App.xaml.cs` `BuildHost`:

```csharp
        builder.Services.AddTransient<PaletteViewModel>();
```

- [ ] **Step 3: Build**

Run: `dotnet build TheOmenDen.PixelForge.slnx`

Expected: succeeds. Note the `field` keyword is used for `Color`, `SelectedRamp` and `EditedName` — these all need change-notification side effects (cascading `OnPropertyChanged` calls, `NotifyCanExecuteChanged`), which `[ObservableProperty]` cannot express as compactly. `Hex` and `PickerColor` are pure projections of `Color` and have no backing field at all.

- [ ] **Step 4: Commit**

```bash
git add src/TheOmenDen.PixelForge/ViewModels/PaletteViewModel.cs src/TheOmenDen.PixelForge/App.xaml.cs
git commit -m "feat(app): add palette view model"
```

---

### Task 14: SKBitmap to WriteableBitmap — use the first-party package

**This task was rewritten.** The original hand-rolled a COM `IBufferByteAccess` interop to copy an `SKBitmap` into a `WriteableBitmap`, and the plan's own risk list named that marshalling as its least certain element. It is unnecessary: **`SkiaSharp.Views.WinUI` ships `ToWriteableBitmap()`**, from the same vendor, at `4.151.0-rc.1.1` — the exact version this project already pins for `SkiaSharp`.

Verified in the package assembly (`lib/net10.0-windows10.0.19041/SkiaSharp.Views.Windows.dll`): `ToWriteableBitmap`, `ToSKBitmap`, `ToSKImage`, plus `SKXamlCanvas`, `SKSwapChainPanel` and `SKPaintSurfaceEventArgs`.

There is no custom type in this task any more. It is a package reference and a using directive.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/TheOmenDen.PixelForge/TheOmenDen.PixelForge.csproj`

**Interfaces:**
- Produces: the extension method `SKBitmap.ToWriteableBitmap()` (namespace `SkiaSharp.Views.Windows`), available to Task 15. **No `SkiaImageSource` type exists** — delete it from the File Split Map expectations.

- [ ] **Step 1: Add the package**

In `Directory.Packages.props`, in the graphics/colour `ItemGroup` beside the existing `SkiaSharp` entry:

```xml
    <PackageVersion Include="SkiaSharp.Views.WinUI" Version="4.151.0-rc.1.1" />
```

Version-matched to `SkiaSharp` deliberately — the views package binds against the core package's `SKBitmap`, so a version skew is a runtime type-identity failure, not a compile error.

In `src/TheOmenDen.PixelForge/TheOmenDen.PixelForge.csproj`, in the WinUI toolkit `ItemGroup`:

```xml
    <PackageReference Include="SkiaSharp.Views.WinUI" />
```

No `Version=` attribute — restore error under CPM.

- [ ] **Step 2: Verify the extension method resolves**

Run: `dotnet restore TheOmenDen.PixelForge.slnx` then `dotnet build TheOmenDen.PixelForge.slnx`

Expected: succeeds, 0 warnings. The extension lives in namespace `SkiaSharp.Views.Windows` (note: **Windows**, not WinUI — the package id and the namespace differ). Task 15's code-behind adds `using SkiaSharp.Views.Windows;` and calls `bitmap.ToWriteableBitmap()`.

Nothing else changes in this task — no new file, no code. If the build is green the extension is available; Task 15 is where it is actually exercised.

- [ ] **Step 3: Commit**

```bash
git add Directory.Packages.props src/TheOmenDen.PixelForge/TheOmenDen.PixelForge.csproj
git commit -m "build(app): add SkiaSharp.Views.WinUI for the Skia-to-XAML bridge"
```

**Noted for later, not now:** `SKXamlCanvas` would let the palette preview draw straight into a XAML surface with no intermediate bitmap at all, which matters because the preview re-renders on every colour-picker drag. That is a larger change — `PalettePreview` would expose "draw into this surface" instead of "return a bitmap", and it is already built and reviewed. Take `ToWriteableBitmap()` now; revisit `SKXamlCanvas` only if drag responsiveness actually disappoints.

---

### Task 15: Palette page

**Files:**
- Modify: `src/TheOmenDen.PixelForge/ViewModels/PaletteViewModel.cs` (add automation-id properties)
- Create: `src/TheOmenDen.PixelForge/Views/PalettePage.xaml`
- Create: `src/TheOmenDen.PixelForge/Views/PalettePage.xaml.cs`
- Modify: `src/TheOmenDen.PixelForge/MainWindow.xaml`
- Modify: `src/TheOmenDen.PixelForge/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: everything from Task 13, `PalettePreview` (Task 7), and `SkiaSharp.Views.WinUI`'s `ToWriteableBitmap()` extension (Task 14).
- Produces: `PalettePage` (navigable), `RampStepViewModel.SwatchAutomationId` / `.HexAutomationId`.

**Note on the step rows:** these are laid out with an `ItemsControl` + `DataTemplate`, not five hand-written rows bound to `Steps[0]`…`Steps[4]`. Hand-written rows would give free automation ids, but `x:Bind` on an indexer does not re-evaluate when the collection is cleared and refilled — which is exactly what selecting a different ramp does, so the editor would show stale colours. The automation ids come off the item view model instead.

- [ ] **Step 1: Add automation-id properties to `RampStepViewModel`**

In `src/TheOmenDen.PixelForge/ViewModels/PaletteViewModel.cs`, add to `RampStepViewModel` beside `Label`:

```csharp
    /// <summary>
    /// Automation ids come off the item, not the template: a DataTemplate cannot give each
    /// generated row a distinct id, and ui-tests.ps1 addresses these by name.
    /// </summary>
    public string SwatchAutomationId => $"SwatchStep{Index + 1}";

    public string HexAutomationId => $"HexStep{Index + 1}";
```

- [ ] **Step 2: Write `PalettePage.xaml`**

Create `src/TheOmenDen.PixelForge/Views/PalettePage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Page
    x:Class="TheOmenDen.PixelForge.Views.PalettePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:behaviors="using:CommunityToolkit.WinUI.Behaviors"
    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
    xmlns:interactivity="using:Microsoft.Xaml.Interactivity"
    xmlns:local="using:TheOmenDen.PixelForge.Views"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:palettes="using:TheOmenDen.PixelForge.Core.Palettes"
    xmlns:vm="using:TheOmenDen.PixelForge.ViewModels"
    mc:Ignorable="d">

    <!--
        The ramp column is user-resizable via GridSplitter rather than a hardcoded width.
        GridSplitter, not PropertySizer: a ColumnDefinition.Width is a GridLength, and
        PropertySizer manipulates a double (its canonical target is
        NavigationView.OpenPaneLength). Both ship in CommunityToolkit.WinUI.Controls.Sizers.
    -->
    <Grid Padding="24">
        <Grid.ColumnDefinitions>
            <ColumnDefinition
                x:Name="RampColumn"
                Width="320"
                MinWidth="240"
                MaxWidth="480" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Grid.ColumnSpan="3" Margin="0,0,0,16" Spacing="4">
            <TextBlock Style="{StaticResource TitleTextBlockStyle}" Text="Palette" />
            <TextBlock
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                Text="Skin ramps. Five steps, darkest first — a recolour is a straight index-for-index substitution." />
        </StackPanel>

        <!--  Ramp list  -->
        <Grid Grid.Row="1" RowSpacing="8">
            <Grid.RowDefinitions>
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <ListView
                x:Name="RampList"
                AutomationProperties.AutomationId="RampList"
                AutomationProperties.Name="Skin ramps"
                ItemsSource="{x:Bind ViewModel.RampView, Mode=OneWay}"
                SelectedItem="{x:Bind ViewModel.SelectedRamp, Mode=TwoWay}">
                <ListView.ItemTemplate>
                    <DataTemplate x:DataType="palettes:SkinRamp">
                        <StackPanel Padding="0,8" Spacing="6">
                            <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="{x:Bind Name}" />
                            <StackPanel Orientation="Horizontal" Spacing="2">
                                <Border
                                    Width="32"
                                    Height="16"
                                    Background="{x:Bind local:PalettePage.StepBrush(., 0)}"
                                    CornerRadius="{StaticResource ControlCornerRadius}" />
                                <Border
                                    Width="32"
                                    Height="16"
                                    Background="{x:Bind local:PalettePage.StepBrush(., 1)}" />
                                <Border
                                    Width="32"
                                    Height="16"
                                    Background="{x:Bind local:PalettePage.StepBrush(., 2)}" />
                                <Border
                                    Width="32"
                                    Height="16"
                                    Background="{x:Bind local:PalettePage.StepBrush(., 3)}" />
                                <Border
                                    Width="32"
                                    Height="16"
                                    Background="{x:Bind local:PalettePage.StepBrush(., 4)}"
                                    CornerRadius="{StaticResource ControlCornerRadius}" />
                            </StackPanel>
                        </StackPanel>
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>

            <CommandBar
                Grid.Row="1"
                DefaultLabelPosition="Right"
                HorizontalAlignment="Left">
                <AppBarButton
                    AutomationProperties.AutomationId="BtnNewRamp"
                    Command="{x:Bind ViewModel.NewRampCommand}"
                    Icon="Add"
                    Label="New" />
                <AppBarButton
                    AutomationProperties.AutomationId="BtnDuplicateRamp"
                    Command="{x:Bind ViewModel.DuplicateRampCommand}"
                    Icon="Copy"
                    Label="Duplicate" />
                <AppBarButton
                    AutomationProperties.AutomationId="BtnDeleteRamp"
                    Command="{x:Bind ViewModel.DeleteRampCommand}"
                    Icon="Delete"
                    Label="Delete" />
                <CommandBar.SecondaryCommands>
                    <AppBarButton
                        AutomationProperties.AutomationId="BtnImportRamps"
                        Command="{x:Bind ViewModel.ImportCommand}"
                        Label="Import CSV" />
                    <AppBarButton
                        AutomationProperties.AutomationId="BtnExportRamps"
                        Command="{x:Bind ViewModel.ExportCommand}"
                        Label="Export CSV" />
                </CommandBar.SecondaryCommands>
            </CommandBar>
        </Grid>

        <controls:GridSplitter
            Grid.Row="1"
            Grid.Column="1"
            Width="16"
            AutomationProperties.AutomationId="RampColumnSplitter"
            AutomationProperties.Name="Resize ramp list"
            ResizeBehavior="BasedOnAlignment"
            ResizeDirection="Auto" />

        <!--  Editor  -->
        <ScrollViewer Grid.Row="1" Grid.Column="2" Padding="16,0,0,0">
            <StackPanel Spacing="16">

                <InfoBar
                    AutomationProperties.AutomationId="BuiltInRampInfoBar"
                    IsClosable="False"
                    IsOpen="{x:Bind ViewModel.IsBuiltInSelected, Mode=OneWay}"
                    Message="This is a shipped ramp and cannot be edited. Duplicate it to make changes."
                    Severity="Informational" />

                <!--
                    One InfoBar, driven by StackedNotificationsBehavior. It queues messages and
                    dismisses each after its Duration, so a run that produces several does not
                    clobber them down to one string. The behavior is the reason the view model
                    has no StatusMessage/HasStatus pair.
                -->
                <InfoBar x:Name="StatusBar" AutomationProperties.AutomationId="PaletteStatusBar">
                    <interactivity:Interaction.Behaviors>
                        <behaviors:StackedNotificationsBehavior x:Name="StatusNotifications" />
                    </interactivity:Interaction.Behaviors>
                </InfoBar>

                <StackPanel Spacing="4">
                    <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="Name" />
                    <TextBox
                        AutomationProperties.AutomationId="RampName"
                        AutomationProperties.Name="Ramp name"
                        IsEnabled="{x:Bind ViewModel.IsEditable, Mode=OneWay}"
                        Text="{x:Bind ViewModel.EditedName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="Steps" />
                    <ItemsControl ItemsSource="{x:Bind ViewModel.Steps, Mode=OneWay}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:RampStepViewModel">
                                <Grid
                                    Margin="0,0,0,8"
                                    ColumnSpacing="12"
                                    VerticalAlignment="Center">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="72" />
                                        <ColumnDefinition Width="Auto" />
                                        <ColumnDefinition Width="140" />
                                    </Grid.ColumnDefinitions>

                                    <TextBlock VerticalAlignment="Center" Text="{x:Bind Label}" />

                                    <!--
                                        ColorPickerButton is the button-plus-flyout wrapper, so
                                        there is no Flyout, no Tag-carried index and no
                                        ColorChanged handler here. SelectedColor two-way binds
                                        straight to the step.
                                    -->
                                    <controls:ColorPickerButton
                                        Grid.Column="1"
                                        AutomationProperties.AutomationId="{x:Bind SwatchAutomationId}"
                                        AutomationProperties.Name="{x:Bind Label}"
                                        SelectedColor="{x:Bind PickerColor, Mode=TwoWay}">
                                        <controls:ColorPickerButton.ColorPickerStyle>
                                            <Style TargetType="controls:ColorPicker">
                                                <Setter Property="IsAlphaEnabled" Value="False" />
                                                <Setter Property="IsColorPaletteVisible" Value="True" />
                                            </Style>
                                        </controls:ColorPickerButton.ColorPickerStyle>
                                    </controls:ColorPickerButton>

                                    <TextBox
                                        Grid.Column="2"
                                        AutomationProperties.AutomationId="{x:Bind HexAutomationId}"
                                        AutomationProperties.Name="{x:Bind Label} hex"
                                        Text="{x:Bind Hex, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}">
                                        <interactivity:Interaction.Behaviors>
                                            <!--  Retyping a hex should not need a manual select-all first.  -->
                                            <behaviors:AutoSelectBehavior />
                                        </interactivity:Interaction.Behaviors>
                                    </TextBox>
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <Button
                        AutomationProperties.AutomationId="BtnSaveRamps"
                        Command="{x:Bind ViewModel.SaveRampCommand}"
                        Content="Save ramp"
                        Style="{StaticResource AccentButtonStyle}" />
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="Preview" />
                    <Border
                        Padding="16"
                        Background="{ThemeResource CanvasBackdropBrush}"
                        BorderBrush="{ThemeResource ToolPanelBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="{StaticResource OverlayCornerRadius}">
                        <Grid>
                            <Image
                                x:Name="PreviewImage"
                                AutomationProperties.AutomationId="RampPreviewImage"
                                AutomationProperties.Name="Recoloured sprite preview"
                                HorizontalAlignment="Center"
                                Stretch="None" />
                            <TextBlock
                                x:Name="PreviewHint"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center"
                                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                Style="{StaticResource CaptionTextBlockStyle}"
                                Text="Set the source pack folders in Settings to see a live preview." />
                        </Grid>
                    </Border>
                </StackPanel>

            </StackPanel>
        </ScrollViewer>
    </Grid>
</Page>
```

- [ ] **Step 3: Write `PalettePage.xaml.cs`**

Create `src/TheOmenDen.PixelForge/Views/PalettePage.xaml.cs`:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Palette editor. All Skia and <c>Windows.UI.Color</c> conversion lives here rather than in the
/// view model, which deals only in <see cref="SKColor"/> — that is what keeps it free of UI types.
/// </summary>
public sealed partial class PalettePage : Page
{
    private PalettePreview? _preview;

    public PalettePage()
    {
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Notified += OnNotified;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public PaletteViewModel ViewModel { get; } = App.Services.GetRequiredService<PaletteViewModel>();

    /// <summary>Swatch for one step of a ramp. Constant index arguments are legal in x:Bind.</summary>
    public static Brush StepBrush(SkinRamp ramp, int index) =>
        ramp is null || index >= ramp.Steps.Length
            ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            : new SolidColorBrush(Windows.UI.Color.FromArgb(
                ramp.Steps[index].Alpha, ramp.Steps[index].Red, ramp.Steps[index].Green, ramp.Steps[index].Blue));

    /// <summary>
    /// Maps the view model's UI-free <see cref="StatusLevel"/> onto the toolkit's notification
    /// queue. The behavior handles stacking and timed dismissal.
    /// </summary>
    private void OnNotified(object? sender, StatusNotice notice) =>
        StatusNotifications.Show(new Notification
        {
            Message = notice.Message,
            Severity = notice.Level switch
            {
                StatusLevel.Success => InfoBarSeverity.Success,
                StatusLevel.Warning => InfoBarSeverity.Warning,
                StatusLevel.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            },
            Duration = notice.Level is StatusLevel.Error ? null : TimeSpan.FromSeconds(4),
        });

    private void OnLoaded(object sender, RoutedEventArgs e) => RenderPreview();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Notified -= OnNotified;

        _preview?.Dispose();
        _preview = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaletteViewModel.PreviewRamp))
        {
            RenderPreview();
        }
    }

    /// <summary>
    /// Re-renders the recoloured sprite. The <see cref="PalettePreview"/> is built once per
    /// session — it caches the curated, un-recoloured sheet, so a colour change costs only the
    /// substitution and the upscale.
    /// </summary>
    private void RenderPreview()
    {
        if (ViewModel.PreviewRamp is not { } ramp)
        {
            ShowHint(visible: true);
            return;
        }

        if (_preview is null)
        {
            if (!ViewModel.PreviewRecipe.TryGet(out var recipe))
            {
                ShowHint(visible: true);
                return;
            }

            var created = PalettePreview.Create(recipe);

            if (!created.TryGet(out _preview))
            {
                PreviewHint.Text = $"Preview unavailable: {created.Error}.";
                ShowHint(visible: true);
                return;
            }
        }

        var rendered = _preview.RenderIdleRow(ramp, scale: 4);

        if (!rendered.TryGet(out var bitmap))
        {
            PreviewHint.Text = $"Preview unavailable: {rendered.Error}.";
            ShowHint(visible: true);
            return;
        }

        using (bitmap)
        {
            // Extension from SkiaSharp.Views.WinUI (namespace SkiaSharp.Views.Windows).
            // First-party bridge — no hand-rolled COM interop.
            PreviewImage.Source = bitmap.ToWriteableBitmap();
        }

        ShowHint(visible: false);
    }

    private void ShowHint(bool visible)
    {
        PreviewHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewImage.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

- [ ] **Step 4: Add the nav item**

In `src/TheOmenDen.PixelForge/MainWindow.xaml`, add after the Assets item:

```xml
                <NavigationViewItem
                    AutomationProperties.AutomationId="NavPalette"
                    Content="Palette"
                    Tag="Palette">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE790;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
```

In `src/TheOmenDen.PixelForge/MainWindow.xaml.cs`, add the case to the routing switch:

```csharp
        var page = (args.SelectedItem as NavigationViewItem)?.Tag switch
        {
            "Assets" => typeof(AssetsPage),
            "Palette" => typeof(PalettePage),
            "Pipeline" => typeof(PipelinePage),
            _ => typeof(CanvasPage),
        };
```

- [ ] **Step 5: Build and run**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet run --project src/TheOmenDen.PixelForge`

Check by hand:
- The list shows seven built-in ramps with five swatches each.
- Selecting a built-in shows the "cannot be edited" bar and disables the name box.
- **New** creates an editable ramp; a colour picker changes a swatch and the hex box follows.
- Typing a hex moves the swatch.
- With packs configured, the preview shows three faces and recolours live as the picker moves.
- Without packs configured, the hint text shows instead of an empty frame.
- Restart the app: custom ramps are still there.
- Check Light, Dark and HighContrast.

- [ ] **Step 6: Run the test suite**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

- [ ] **Step 7: Commit**

```bash
git add src/TheOmenDen.PixelForge/Views/PalettePage.xaml src/TheOmenDen.PixelForge/Views/PalettePage.xaml.cs src/TheOmenDen.PixelForge/ViewModels/PaletteViewModel.cs src/TheOmenDen.PixelForge/MainWindow.xaml src/TheOmenDen.PixelForge/MainWindow.xaml.cs
git commit -m "feat(app): add palette page with live recolour preview"
```

---

## Phase E — App: batch export page

### Task 16: Batch export view model

**Files:**
- Create: `src/TheOmenDen.PixelForge/ViewModels/BatchExportViewModel.cs`
- Modify: `src/TheOmenDen.PixelForge/App.xaml.cs` (register)

**Interfaces:**
- Consumes: `BatchBaker.RunAsync`, `BakeProgress`, `BatchSummary` (Task 4); `SheetIndex.WriteTo` (Task 5); `RoostSheets.Bodies` / `.Hair` / `.Flattened` (Task 2); `SourcePackService` (Task 9); `PickerService` (Task 10).
- Produces:
  - `enum ExportMode { Layered, Flattened, Both }`
  - `SheetSelectionItem { SheetRecipe Recipe; string Name; bool IsSelected; string Status; string AutomationId; }`
  - `BatchExportViewModel.Bodies` / `.Hair` → `ObservableCollection<SheetSelectionItem>`
  - `.SelectedModeIndex` → `int` (bound to `Segmented.SelectedIndex`), `.ModeDescription` → `string`
  - `.OutputFolder` → `string`, `.BrowseOutputCommand`
  - `.ExportCommand` / `.ExportCancelCommand`
  - `.ProgressValue` → `double`, `.ProgressText` → `string`, `.IsExporting` → `bool`
  - `.PacksMissing` → `bool`
  - `.Notified` → `event EventHandler<StatusNotice>?` (reuses Task 13's types)
  - `.PlannedCount` → `int`

  Task 17 binds to all of these. `ExportMode`, `SheetSelectionItem` and `BatchExportViewModel` go in three files — see the **File Split Map**.

- [ ] **Step 1: Write `BatchExportViewModel`**

Create `src/TheOmenDen.PixelForge/ViewModels/BatchExportViewModel.cs`:

```csharp
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;
using TheOmenDen.PixelForge.Services;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>What a run emits.</summary>
public enum ExportMode
{
    /// <summary>One file per recipe. Hair stays its own texture — the Corvus contract.</summary>
    Layered,

    /// <summary>Body and hair composited into one texture per pair.</summary>
    Flattened,

    Both,
}

/// <summary>One selectable sheet, and how its bake went.</summary>
public sealed partial class SheetSelectionItem(SheetRecipe recipe) : ObservableObject
{
    public SheetRecipe Recipe { get; } = recipe;

    public string Name => Recipe.Name;

    public string AutomationId => $"Sheet_{Recipe.Name}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;
}

/// <summary>
/// Selects sheets, runs the batch, and reports each result as it lands.
/// </summary>
public sealed partial class BatchExportViewModel : ObservableObject
{
    private readonly SourcePackService _packs;
    private readonly PickerService _picker;

    public BatchExportViewModel(SourcePackService packs, PickerService picker)
    {
        _packs = packs;
        _picker = picker;

        _packs.Changed += (_, _) => Reload();

        Reload();
    }

    public ObservableCollection<SheetSelectionItem> Bodies { get; } = [];

    public ObservableCollection<SheetSelectionItem> Hair { get; } = [];

    /// <summary>Index into the mode Segmented — no converter needed.</summary>
    public int SelectedModeIndex
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeDescription));
            OnPropertyChanged(nameof(PlannedCount));
        }
    }

    public ExportMode Mode => (ExportMode)SelectedModeIndex;

    /// <summary>
    /// The tradeoff, stated where the choice is made. Layered keeps hair a separate texture so a
    /// style can be swapped at runtime and the engine keeps control of z-order; flattened is one
    /// texture per pair — fewer draw calls, but the hair is baked in.
    /// </summary>
    public string ModeDescription => Mode switch
    {
        ExportMode.Layered =>
            "One file per sheet. Hair stays a separate texture, so a style can be swapped at runtime without rebaking.",
        ExportMode.Flattened =>
            "Body and hair composited into one texture per pair. Fewer draw calls, but the hairstyle is baked in.",
        _ =>
            "Both: layered sheets for runtime swapping, plus a flattened texture for every body and hair pair.",
    };

    public string OutputFolder
    {
        get => field;
        private set
        {
            field = value;

            OnPropertyChanged();

            ExportCommand.NotifyCanExecuteChanged();
        }
    } = string.Empty;

    public bool PacksMissing => !_packs.Resolved.HasValue;

    public bool IsExporting
    {
        get => field;
        private set
        {
            field = value;

            OnPropertyChanged();

            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    public double ProgressValue
    {
        get => field;
        private set
        {
            field = value;

            OnPropertyChanged();
        }
    }

    public string ProgressText
    {
        get => field;
        private set
        {
            field = value;

            OnPropertyChanged();
        }
    } = string.Empty;

    /// <summary>
    /// Raised for anything worth telling the user. The page feeds these to a
    /// <c>StackedNotificationsBehavior</c>, which queues them — a 79-sheet run can report several
    /// distinct failures, and a single string property would show only the last.
    /// </summary>
    public event EventHandler<StatusNotice>? Notified;

    private void Notify(string message, StatusLevel level) =>
        Notified?.Invoke(this, new StatusNotice(message, level));

    /// <summary>How many files the current selection and mode would produce.</summary>
    public int PlannedCount => Plan().Length;

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var picked = await _picker.PickFolderAsync();

        if (picked.TryGet(out var folder))
        {
            OutputFolder = folder.Value;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport), IncludeCancelCommand = true)]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        var recipes = Plan();

        if (recipes.IsDefaultOrEmpty)
        {
            Notify("Nothing selected.", StatusLevel.Warning);
            return;
        }

        IsExporting = true;
        ProgressValue = 0;
        ProgressText = $"0 / {recipes.Length}";

        ResetStatuses();

        // Constructed on the UI thread, so Report marshals back to it. Every status write below
        // therefore happens on the dispatcher without any explicit queueing.
        var progress = new Progress<BakeProgress>(OnBakeProgress);

        try
        {
            var summary = await BatchBaker.RunAsync(
                recipes,
                FullPath.FromPath(OutputFolder),
                progress,
                Environment.ProcessorCount,
                cancellationToken);

            var index = SheetIndex.WriteTo(FullPath.FromPath(OutputFolder));

            // Separate notices rather than one concatenated string — the behavior stacks them,
            // so a failed manifest stays visible next to an otherwise successful run.
            if (summary.Cancelled)
            {
                Notify(
                    $"Cancelled. {summary.Succeeded} written, {summary.TotalWritten} total.",
                    StatusLevel.Warning);
            }
            else
            {
                Notify(
                    $"{summary.Succeeded} written, {summary.Failed} failed, {summary.TotalWritten} total.",
                    summary.Failed is 0 ? StatusLevel.Success : StatusLevel.Warning);
            }

            if (!index.IsSuccessful)
            {
                Notify($"Manifest failed: {index.Error}.", StatusLevel.Error);
            }
        }
        finally
        {
            IsExporting = false;
        }
    }

    private bool CanExport() => !IsExporting && !PacksMissing && !string.IsNullOrWhiteSpace(OutputFolder);

    private void OnBakeProgress(BakeProgress report)
    {
        ProgressValue = report.Total is 0 ? 0 : report.Completed * 100.0 / report.Total;
        ProgressText = $"{report.Completed} / {report.Total}";

        var status = report.IsSuccess
            ? report.Written.TryGet(out var size) ? size.ToString() : "written"
            : report.Failure.ToString();

        // A flattened sheet's name is "body-NN_hair-NN", so a body row matches its own prefix.
        Mark(Bodies, report.Name, status);
        Mark(Hair, report.Name, status);
    }

    private static void Mark(ObservableCollection<SheetSelectionItem> items, string name, string status)
    {
        foreach (var item in items)
        {
            if (name == item.Name || name.StartsWith(item.Name + "_", StringComparison.Ordinal) || name.EndsWith("_" + item.Name, StringComparison.Ordinal))
            {
                item.Status = status;
            }
        }
    }

    private void ResetStatuses()
    {
        foreach (var item in Bodies)
        {
            item.Status = string.Empty;
        }

        foreach (var item in Hair)
        {
            item.Status = string.Empty;
        }
    }

    /// <summary>The recipes the current selection and mode imply.</summary>
    private ImmutableArray<SheetRecipe> Plan()
    {
        var bodies = Selected(Bodies);
        var hair = Selected(Hair);

        return Mode switch
        {
            ExportMode.Layered => [.. bodies, .. hair],
            ExportMode.Flattened => RoostSheets.Flattened(bodies, hair),
            _ => [.. bodies, .. hair, .. RoostSheets.Flattened(bodies, hair)],
        };
    }

    private static ImmutableArray<SheetRecipe> Selected(ObservableCollection<SheetSelectionItem> items)
    {
        var chosen = ImmutableArray.CreateBuilder<SheetRecipe>(items.Count);

        foreach (var item in items)
        {
            if (item.IsSelected)
            {
                chosen.Add(item.Recipe);
            }
        }

        return chosen.ToImmutable();
    }

    private void Reload()
    {
        Bodies.Clear();
        Hair.Clear();

        if (_packs.Resolved.TryGet(out var packs))
        {
            foreach (var recipe in RoostSheets.Bodies(packs))
            {
                Bodies.Add(Track(new SheetSelectionItem(recipe)));
            }

            foreach (var recipe in RoostSheets.Hair(packs))
            {
                Hair.Add(Track(new SheetSelectionItem(recipe)));
            }
        }

        OnPropertyChanged(nameof(PacksMissing));
        OnPropertyChanged(nameof(PlannedCount));

        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Selection drives the planned count, so each item's changes are watched.</summary>
    private SheetSelectionItem Track(SheetSelectionItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SheetSelectionItem.IsSelected))
            {
                OnPropertyChanged(nameof(PlannedCount));
            }
        };

        return item;
    }
}
```

- [ ] **Step 2: Register it**

In `App.xaml.cs` `BuildHost`:

```csharp
        builder.Services.AddTransient<BatchExportViewModel>();
```

- [ ] **Step 3: Build**

Run: `dotnet build TheOmenDen.PixelForge.slnx`

Expected: succeeds. `[RelayCommand(IncludeCancelCommand = true)]` generates `ExportCancelCommand` alongside `ExportCommand`, so no `CancellationTokenSource` appears in this file.

- [ ] **Step 4: Commit**

```bash
git add src/TheOmenDen.PixelForge/ViewModels/BatchExportViewModel.cs src/TheOmenDen.PixelForge/App.xaml.cs
git commit -m "feat(app): add batch export view model"
```

---

### Task 17: Batch export page

**Files:**
- Modify: `src/TheOmenDen.PixelForge/Views/PipelinePage.xaml`
- Modify: `src/TheOmenDen.PixelForge/Views/PipelinePage.xaml.cs`

**Interfaces:**
- Consumes: everything produced by Task 16.
- Produces: the `PipelinePage` UI. No new types.

- [ ] **Step 1: Rewrite `PipelinePage.xaml`**

Replace `src/TheOmenDen.PixelForge/Views/PipelinePage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Page
    x:Class="TheOmenDen.PixelForge.Views.PipelinePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:behaviors="using:CommunityToolkit.WinUI.Behaviors"
    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
    xmlns:interactivity="using:Microsoft.Xaml.Interactivity"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:ui="using:CommunityToolkit.WinUI"
    xmlns:vm="using:TheOmenDen.PixelForge.ViewModels"
    mc:Ignorable="d">

    <Grid Padding="24" RowSpacing="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <StackPanel Spacing="4">
            <TextBlock Style="{StaticResource TitleTextBlockStyle}" Text="Batch export" />
            <TextBlock
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                Text="Bake the selected sheets to lossless WebP, verified by round-trip." />
        </StackPanel>

        <InfoBar
            Grid.Row="1"
            AutomationProperties.AutomationId="PacksMissingInfoBar"
            IsClosable="False"
            IsOpen="{x:Bind ViewModel.PacksMissing, Mode=OneWay}"
            Message="Source pack folders are not configured. Set all three in Settings before exporting."
            Severity="Warning" />

        <!--  Output and mode  -->
        <Grid Grid.Row="2" ColumnSpacing="16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <TextBox
                AutomationProperties.AutomationId="OutputFolderText"
                AutomationProperties.Name="Output folder"
                Header="Output folder"
                IsReadOnly="True"
                PlaceholderText="Choose a folder"
                Text="{x:Bind ViewModel.OutputFolder, Mode=OneWay}" />

            <Button
                Grid.Column="1"
                Margin="0,28,0,0"
                AutomationProperties.AutomationId="BtnBrowseOutput"
                AutomationProperties.Name="Browse for output folder"
                Command="{x:Bind ViewModel.BrowseOutputCommand}"
                Content="Browse" />

            <StackPanel
                Grid.Row="1"
                Grid.ColumnSpan="2"
                Margin="0,16,0,0"
                Spacing="4">
                <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="Mode" />

                <!--
                    Segmented, not RadioButtons: three short mutually-exclusive toggles is the
                    control's stated use (2-5 items, no overflow), and it reads as a mode switch
                    rather than a settings choice.
                -->
                <controls:Segmented
                    AutomationProperties.AutomationId="ExportModeSegmented"
                    AutomationProperties.Name="Export mode"
                    SelectedIndex="{x:Bind ViewModel.SelectedModeIndex, Mode=TwoWay}"
                    SelectionMode="Single">
                    <controls:SegmentedItem Content="Layered" Icon="{ui:FontIcon Glyph=&#xE81E;}" />
                    <controls:SegmentedItem Content="Flattened" Icon="{ui:FontIcon Glyph=&#xE7C4;}" />
                    <controls:SegmentedItem Content="Both" Icon="{ui:FontIcon Glyph=&#xE8C6;}" />
                </controls:Segmented>
                <TextBlock
                    Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                    Style="{StaticResource CaptionTextBlockStyle}"
                    Text="{x:Bind ViewModel.ModeDescription, Mode=OneWay}"
                    TextWrapping="Wrap" />
            </StackPanel>
        </Grid>

        <!--  Selection  -->
        <Grid Grid.Row="3">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" MinWidth="200" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" MinWidth="200" />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>

            <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="Bodies" />
            <TextBlock
                Grid.Column="2"
                Style="{StaticResource BodyStrongTextBlockStyle}"
                Text="Hair" />

            <!--  The 50/50 split is a starting point, not a constraint.  -->
            <controls:GridSplitter
                Grid.Row="1"
                Grid.Column="1"
                Width="16"
                AutomationProperties.AutomationId="SheetListSplitter"
                AutomationProperties.Name="Resize sheet lists"
                ResizeBehavior="BasedOnAlignment"
                ResizeDirection="Auto" />

            <ListView
                Grid.Row="1"
                AutomationProperties.AutomationId="BodySheetList"
                AutomationProperties.Name="Body sheets"
                ItemsSource="{x:Bind ViewModel.Bodies, Mode=OneWay}"
                SelectionMode="None">
                <ListView.ItemTemplate>
                    <DataTemplate x:DataType="vm:SheetSelectionItem">
                        <Grid ColumnSpacing="12" Padding="0,4">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <CheckBox
                                AutomationProperties.AutomationId="{x:Bind AutomationId}"
                                Content="{x:Bind Name}"
                                IsChecked="{x:Bind IsSelected, Mode=TwoWay}" />
                            <TextBlock
                                Grid.Column="1"
                                VerticalAlignment="Center"
                                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                Style="{StaticResource CaptionTextBlockStyle}"
                                Text="{x:Bind Status, Mode=OneWay}" />
                        </Grid>
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>

            <ListView
                Grid.Row="1"
                Grid.Column="2"
                AutomationProperties.AutomationId="HairSheetList"
                AutomationProperties.Name="Hair sheets"
                ItemsSource="{x:Bind ViewModel.Hair, Mode=OneWay}"
                SelectionMode="None">
                <ListView.ItemTemplate>
                    <DataTemplate x:DataType="vm:SheetSelectionItem">
                        <Grid ColumnSpacing="12" Padding="0,4">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <CheckBox
                                AutomationProperties.AutomationId="{x:Bind AutomationId}"
                                Content="{x:Bind Name}"
                                IsChecked="{x:Bind IsSelected, Mode=TwoWay}" />
                            <TextBlock
                                Grid.Column="1"
                                VerticalAlignment="Center"
                                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                Style="{StaticResource CaptionTextBlockStyle}"
                                Text="{x:Bind Status, Mode=OneWay}" />
                        </Grid>
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>
        </Grid>

        <!--  Run  -->
        <Grid Grid.Row="4" ColumnSpacing="12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <ProgressBar
                VerticalAlignment="Center"
                AutomationProperties.AutomationId="ExportProgress"
                AutomationProperties.Name="Export progress"
                Maximum="100"
                Value="{x:Bind ViewModel.ProgressValue, Mode=OneWay}" />

            <TextBlock
                Grid.Column="1"
                VerticalAlignment="Center"
                AutomationProperties.AutomationId="ExportProgressText"
                Text="{x:Bind ViewModel.ProgressText, Mode=OneWay}" />

            <StackPanel
                Grid.Column="2"
                Orientation="Horizontal"
                Spacing="8">
                <Button
                    AutomationProperties.AutomationId="BtnExport"
                    Command="{x:Bind ViewModel.ExportCommand}"
                    Content="Export"
                    Style="{StaticResource AccentButtonStyle}" />
                <Button
                    AutomationProperties.AutomationId="BtnCancelExport"
                    Command="{x:Bind ViewModel.ExportCancelCommand}"
                    Content="Cancel"
                    IsEnabled="{x:Bind ViewModel.IsExporting, Mode=OneWay}" />
            </StackPanel>

            <!--
                Queued rather than a single caption line: a 79-sheet run can produce a summary
                and a manifest failure, and the behavior stacks and times out each one.
            -->
            <InfoBar
                x:Name="StatusBar"
                Grid.Row="1"
                Grid.ColumnSpan="3"
                Margin="0,8,0,0"
                AutomationProperties.AutomationId="ExportStatusBar">
                <interactivity:Interaction.Behaviors>
                    <behaviors:StackedNotificationsBehavior x:Name="StatusNotifications" />
                </interactivity:Interaction.Behaviors>
            </InfoBar>
        </Grid>
    </Grid>
</Page>
```

**Note on `SelectionMode="None"`:** the spec called for `SelectionMode="Multiple"`, which supplies checkboxes for free. It is not used here because each row also needs a status column and its own automation id, so the row template is explicit anyway — and `Multiple` would give a second, competing selection model on top of `IsSelected`.

- [ ] **Step 2: Update `PipelinePage.xaml.cs`**

Replace the class body of `src/TheOmenDen.PixelForge/Views/PipelinePage.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

public sealed partial class PipelinePage : Page
{
    public PipelinePage()
    {
        InitializeComponent();

        ViewModel.Notified += OnNotified;
        Unloaded += (_, _) => ViewModel.Notified -= OnNotified;
    }

    public BatchExportViewModel ViewModel { get; } = App.Services.GetRequiredService<BatchExportViewModel>();

    /// <summary>
    /// Maps the view model's UI-free <see cref="StatusLevel"/> onto the toolkit's notification
    /// queue. Errors have no Duration, so they stay until dismissed.
    /// </summary>
    private void OnNotified(object? sender, StatusNotice notice) =>
        StatusNotifications.Show(new Notification
        {
            Message = notice.Message,
            Severity = notice.Level switch
            {
                StatusLevel.Success => InfoBarSeverity.Success,
                StatusLevel.Warning => InfoBarSeverity.Warning,
                StatusLevel.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            },
            Duration = notice.Level is StatusLevel.Error ? null : TimeSpan.FromSeconds(6),
        });
}
```

Add `using CommunityToolkit.WinUI.Behaviors;` for `Notification`.

- [ ] **Step 3: Build and run**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet run --project src/TheOmenDen.PixelForge`

Check by hand, with the packs configured:
- The warning bar is gone; body and hair lists are populated (7 and 9).
- Switching mode changes the description text underneath.
- Picking an output folder enables Export.
- Export fills the progress bar and each row gains a size; the summary line reports totals.
- `index.csv` appears in the output folder alongside the `.webp` files.
- Cancel mid-run stops it and the summary says cancelled, with the already-written files kept.
- Check Light, Dark and HighContrast.

- [ ] **Step 4: Run the test suite**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

- [ ] **Step 5: Commit**

```bash
git add src/TheOmenDen.PixelForge/Views/PipelinePage.xaml src/TheOmenDen.PixelForge/Views/PipelinePage.xaml.cs
git commit -m "feat(app): rebuild the pipeline page as batch sheet export"
```

---

## Phase F — Verification

### Task 18: UI automation coverage

**Files:**
- Modify: `tests/ui-tests.ps1`

- [ ] **Step 1: Add the palette and batch blocks**

Append `Test-UI` blocks to `tests/ui-tests.ps1`, following the file's existing block style. Read the existing blocks first and match their helper names — the pseudo-code below shows what each must assert, not the exact harness syntax:

```powershell
Test-UI "Palette page lists the built-in ramps" {
    Invoke-UIA -Click 'NavPalette'
    $items = Get-UIAChildren 'RampList'
    Assert-True ($items.Count -ge 7) "expected at least the 7 built-in ramps, got $($items.Count)"
}

Test-UI "Selecting a built-in ramp disables editing" {
    Invoke-UIA -Click 'NavPalette'
    Invoke-UIA -Select 'RampList' -Index 0
    Assert-True (Get-UIAProperty 'BuiltInRampInfoBar' 'IsOffscreen') -eq $false
    Assert-False (Get-UIAProperty 'RampName' 'IsEnabled')
}

Test-UI "A new ramp is editable and its hex commits on keystroke" {
    Invoke-UIA -Click 'NavPalette'
    Invoke-UIA -Click 'BtnNewRamp'
    Invoke-UIA -SetValue 'HexStep1' '#102030'
    Invoke-UIA -Click 'BtnSaveRamps'
    Assert-Equal (Get-UIAValue 'HexStep1') '#102030'
}

Test-UI "Batch page blocks export until the packs are set" {
    Invoke-UIA -Click 'NavPipeline'
    # With no packs configured the warning bar is open and Export is disabled.
    Assert-False (Get-UIAProperty 'BtnExport' 'IsEnabled')
}

Test-UI "Export mode description changes with the selection" {
    Invoke-UIA -Click 'NavPipeline'
    Invoke-UIA -Select 'ExportModeSegmented' -Index 1
    Assert-Contains (Get-UIAText 'ExportModeSegmented') 'Flattened'
}
```

- [ ] **Step 2: Run the app and the UI suite**

Run: `dotnet run --project src/TheOmenDen.PixelForge` — note the PID it prints.

Then: `.\tests\ui-tests.ps1 -AppPid <PID>`

- [ ] **Step 3: Look at the screenshots**

Open `tests/ui-results/`. UIA assertions pass while a page is visually broken — clipping, overlap, wrong theme, truncated text. Specifically check:

- The palette swatch strip is not clipped in the 320px list column.
- The five step rows align and the hex boxes are not truncated.
- The batch page's two lists are equal width and neither scrolls horizontally.
- The mode description wraps rather than clipping.
- Nothing on either new page uses a hardcoded colour — verify by switching to HighContrast.

- [ ] **Step 4: Full verification**

Run: `dotnet build TheOmenDen.PixelForge.slnx` then `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: build succeeds with zero warnings (they are errors), all Core tests pass.

- [ ] **Step 5: Memory check on a full run**

With all seven bodies and all nine hair selected and mode set to **Both** (79 sheets), watch the app's working set in Task Manager during the run. It should plateau rather than climb — the pooled stream manager and the bounded parallelism are what hold it. If it climbs steadily, the pooled streams are not being disposed per recipe; check `BatchBaker.BakeOne`.

- [ ] **Step 6: Commit**

```bash
git add tests/ui-tests.ps1
git commit -m "test: add UI automation for palette and batch export"
```

---

## Self-Review

Run against the spec after writing. Findings and fixes are recorded here rather than silently applied.

**1. Spec coverage.** Every spec section maps to a task:

| Spec section | Task |
|---|---|
| `SheetRecipe.Overlays` + recolour-before-overlay | 1 |
| `FlattenedSheets` cross product | 2 (folded into `RoostSheets`) |
| `SheetWriter` + two `BakeFailure` members | 3 |
| `BatchBaker`, `BakeProgress`, `BatchSummary` | 4 |
| Sheet index manifest (lens addition) | 5 |
| `RampFailure`, `RampCsv`, `RampStore` | 6 (`RampCsv` folded in) |
| `PalettePreview` | 7 |
| `AppPaths.LocalState` | 8 |
| `SourcePackService` + `JsonSerializerContext` | 9 |
| `PickerService` | 10 |
| `RampService` | 11 |
| `SettingsPage` pack rows | 12 |
| `PaletteViewModel` | 13 |
| Skia→XAML bridge (package, not custom code) | 14 |
| `PalettePage` + nav item | 15 |
| `BatchExportViewModel` | 16 |
| `PipelinePage` batch UI | 17 |
| UI tests + screenshots | 18 |

**1b. Toolkit-first revision.** After the first draft, five more toolkit packages were adopted (see Lens Notes). What changed:

| Was hand-rolled | Now | Tasks touched |
|---|---|---|
| `Button` + `Flyout` + `ColorPicker` + `Tag` + `OnStepColorChanged` + `SetStepColor` | `ColorPickerButton` with two-way `SelectedColor` | 13, 15 |
| `RadioButtons` for export mode; `ComboBox` for theme | `Segmented` / `SegmentedItem` in both | 12, 16, 17 |
| `StatusMessage` + `HasStatus` + manual `InfoBar.IsOpen`, in two view models | `StatusNotice` event → `StackedNotificationsBehavior` | 13, 15, 16, 17 |
| `RampService.BuiltIn` + `Custom` + `All`; `PaletteViewModel.RefreshRamps` clear-and-rebuild | single `RampService.Ramps` + `AdvancedCollectionView` | 11, 13, 15 |
| `Width="320"` and a fixed 50/50 split | `GridSplitter` in both pages | 15, 17 |
| manual select-all before retyping a hex | `AutoSelectBehavior` | 15 |

**Honest note on the first draft.** Four of these five were hand-rolled in a plan whose own Global Constraints open with "check the library before writing anything". `ColorPickerButton` is the sharpest miss: the first pass looked at the toolkit ColorPicker package, judged it as "only adds accent swatches", and never enumerated the rest of its surface — which is exactly the failure mode standing rule 0 describes.

**One control deliberately not used.** `PropertySizer` manipulates a `double`; a `ColumnDefinition.Width` is a `GridLength`. `GridSplitter`, from the same package, is the correct control for both two-column layouts. `PropertySizer`'s canonical target is `NavigationView.OpenPaneLength` — available if a resizable nav pane is ever wanted, but nothing here needs it.

**1c. One type per file.** Applied project-wide per the File Split Map, including four pre-existing files that already violated it. Task 0 fixes those first, as a pure refactor whose only verification is an unchanged test count.

**2. Deviations from the spec, and why.**

- `RampCsv` and `FlattenedSheets` no longer exist as separate types — folded into `RampStore` and `RoostSheets`. Ponytail lens; recorded in Lens Notes.
- The three service interfaces are gone. Ponytail lens.
- `SheetIndex` is new and not in the spec. 2d-games lens.
- `SettingsViewModel` loses its primary constructor because it must subscribe to `SourcePackService.Changed`, which needs a field.
- `PipelinePage` uses `SelectionMode="None"` with explicit `CheckBox` rows rather than the spec's `SelectionMode="Multiple"` — noted in Task 17, because `Multiple` would create a second selection model competing with `IsSelected`.
- The spec's Settings automation ids (`CorePackPath` and friends) land on the `SettingsCard.Description` binding; Task 12 notes the fallback if UIA cannot reach them.

**3. Type consistency.** Names used in later tasks match earlier definitions: `SheetRecipe.Overlays` (1 → 2, 4, 5), `SheetWriter.Write` (3 → 4), `BakeProgress.IsSuccess` (4 → 16), `RoostSheets.Flattened` (2 → 16), `RampStore.Read`/`Write`/`Load`/`Save` (6 → 11), `RampConversions.Hex`/`TryParseHex` (6 → 13), `PalettePreview.RenderIdleRow` (7 → 15), `AppPaths.RampStoreFile` (8 → 11), `SourcePackService.Resolved` (9 → 12, 16), `PickerService.PickFolderAsync` (10 → 12, 16), `SKBitmap.ToWriteableBitmap()` from SkiaSharp.Views.WinUI (14 → 15), `RampStepViewModel.SwatchAutomationId` (15 → 15 XAML).

**4. Known risks carried into implementation.**

- ~~`IBufferByteAccess` marshalling under CsWinRT~~ **RESOLVED.** Task 14 was rewritten to use `SkiaSharp.Views.WinUI`'s first-party `ToWriteableBitmap()`. No hand-rolled COM interop remains, and the plan's least-certain element is gone.
- `RecipeBaker.Finish` gains a parameter change (`Optional<SkinRamp>` → `SheetRecipe`) in Task 1. It is private, so no external caller breaks.
- Task 7 refactors `RecipeBaker.Bake` to call the new `AssembleLayers`. Task 1's tests are the regression guard and must still pass.
- A full "Both" run is 79 sheets. Task 18 Step 5 is the memory check.

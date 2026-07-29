# Palette editing and batch sheet export

**Date:** 2026-07-28
**Status:** Approved for planning

## Context

Core ships a complete, tested single-sheet bake path:

```
RoostSheets.All(packs) → 16 SheetRecipe → RecipeBaker.Bake → Result<RecyclableMemoryStream, BakeFailure>
```

Two gaps make it unusable from the app:

1. **Nothing writes a baked stream to disk.** `RecipeBaker.Bake` hands back a pooled stream and stops.
2. **Nothing runs more than one recipe.** There is no batch runner, no progress, no cancellation.

The app is a shell: `CanvasPage` has three inert tool buttons, `AssetsPage` and `PipelinePage` are
empty-state placeholders, `SettingsPage` has a theme radio. `SkinRamps.All` holds seven built-in
ramps that no UI surfaces.

## Goals

- Preview all skin ramps as swatches, with a live recoloured sprite.
- Create, rename, duplicate and delete **custom** ramps; edit any step's colour; persist across restarts.
- Import and export ramps as CSV.
- Select any subset of body and hair sheets and export them in one run, with progress, cancellation
  and per-sheet failure reporting.
- Export **layered** (one file per recipe, the current Corvus contract) and/or **flattened**
  (body × hair composited into one sheet).
- Configure the three Time Elements pack directories from the app and persist them.

## Non-goals

- Editing sprite pixels. The canvas stays inert; this is palette and pipeline work.
- Changing the curated sheet geometry or the Corvus layered contract. Flattened output is additive.
- Editing the seven built-in ramps. They are the shipped contract — customs are additive, and
  editing a built-in offers "Duplicate to edit".

## Library findings (standing rule 0)

Enumerated before designing. Recorded including the cases where a package genuinely lacks the thing.

| Need | Found | Decision |
|---|---|---|
| Colour editing UI | `CommunityToolkit.WinUI.Controls.ColorPicker` → **`ColorPickerButton`** | **Superseded — use the toolkit.** The first pass chose the built-in `ColorPicker` on the grounds that the toolkit only adds accent-colour swatches. That missed `ColorPickerButton`, the button-plus-flyout wrapper, which is precisely the control this design was about to hand-roll out of a `Button`, a `Flyout`, a `Tag`-carried index and a `ColorChanged` handler. Exactly the failure mode standing rule 0 exists to prevent. |
| Windows 11 settings rows | `CommunityToolkit.WinUI.Controls.SettingsControls` `8.3.260402-preview2` | **Add.** Same preview train as the seven toolkit packages already pinned. Hand-rolling `StackPanel` + label is the documented anti-pattern. |
| Folder / file picking | `Microsoft.Windows.Storage.Pickers.FolderPicker` — confirmed present in `microsoft.windowsappsdk.foundation/2.3.5` (`Microsoft.Windows.Storage.Pickers.Projection.dll`, `.winmd`) | **Use it.** Ships with WinAppSDK, no package. The legacy `Windows.Storage.Pickers` + `InitializeWithWindow` silently shows no dialog in packaged builds; this replacement works packaged and unpackaged. |
| Ramp persistence + interchange | `CsvHelper` 33.1.0, already referenced | One CSV format serves LocalState persistence *and* Import/Export. |
| Bounded-parallel batch with progress | `Parallel.ForEachAsync` (BCL) | **Use it.** Not in `BannedSymbols.txt` — that bans `SemaphoreSlim`, `Semaphore`, `ReaderWriterLockSlim`, `Monitor`, `Mutex`, `ManualResetEventSlim`, `CountdownEvent`, `Barrier`, `Lazy<T>`. DotNext.Threading was checked first: `TaskCompletionPipe<T>` (`Add(T, object)` / `GetAsyncEnumerator` / `TryRead(out T, out object)`) streams results in completion order and carries a correlation token, but **does not bound concurrency** — every added task starts immediately. At 79 sheets × ~828 KiB decoded partials that is the memory failure mode. `Parallel.ForEachAsync` gives bounding *and* completion-order reporting in one call, so no throttle primitive is needed at all. |
| `SKBitmap` → XAML `Image` | `SKPixmap.ReadPixels` targeting `Bgra8888`/`Premul`; `WindowsRuntimeStreamExtensions`, `AsRandomAccessStream` and `IBufferByteAccess` all confirmed in `Microsoft.Windows.SDK.NET.dll` | Skia does the channel conversion. No hand-rolled swap loop. |
| `SkinRamp` ↔ CSV row mapping | `Riok.Mapperly` | **Hand-written, stated exception.** The mapping is a shape change — `ImmutableArray<SKColor>` to five named hex columns — plus hex formatting. Mapperly would need a user-implemented mapping method whose body is the whole conversion, so the generator adds indirection and no generated code. ~10 lines in `RampCsv`. |

## Correctness: flattening must recolour before compositing

`RecipeBaker` composites every layer, *then* recolours the whole assembly. `RoostSheets` documents
why that is safe today: both garment partials carry zero skin-ramp pixels, so the substitution can
only reach the face — and it explicitly names `hair1` and `hat4` as partials that use skin-ramp
hexes as hair and trim.

Compositing hair before the recolour would therefore rewrite those hair pixels. Naive flattening
corrupts the art.

Fix — overlays are drawn *after* the recolour:

```csharp
public sealed record SheetRecipe
{
    public required string Name { get; init; }
    public required ImmutableArray<FullPath> Layers { get; init; }
    public Optional<SkinRamp> Recolor { get; init; } = Optional<SkinRamp>.None;

    /// <summary>
    /// Layers drawn AFTER the recolour, so their authored colours survive. Empty for layered
    /// output. This is what makes flattening safe for hair partials that reuse skin-ramp hexes.
    /// </summary>
    public ImmutableArray<FullPath> Overlays { get; init; } = [];
}
```

An optional property with a default keeps every existing construction site and test compiling.

`RecipeBaker.Finish` grows one step:

```
assemble body layers → canonical
  → recolour (if any)
  → composite overlays on a premul surface, convert back to canonical   ← new
  → curate
  → encode verified
```

Overlay geometry is validated the same way layers are, yielding `LayerGeometryMismatch`.

## Core changes (`net10.0`, no Windows types)

### `Palettes/RampFailure.cs`

Numbered from 1 so `default` is never a real failure, matching `BakeFailure`.

```csharp
public enum RampFailure
{
    StoreUnreadable = 1,   // file exists, could not be opened
    StoreMalformed,        // CSV parsed but rows are not ramps
    StoreUnwritable,
    WrongStepCount,        // not exactly SkinRamps.StepCount colours
    NameEmpty,
    DuplicateName,
    BuiltInImmutable,      // attempt to mutate a shipped ramp
    NotFound,
}
```

### `Palettes/RampCsv.cs`

Header: `Name,IsHuman,Step1,Step2,Step3,Step4,Step5`. Steps are `#RRGGBB`, darkest first, matching
the hex literals already in `SkinRamps` so a file diffs directly against the source. Parsing is
`ColorConverter.HexToRgb`, as `SkinRamps.FromHex` already does.

```csharp
public static Result<ImmutableArray<SkinRamp>, RampFailure> Read(TextReader reader);
public static Result<int, RampFailure> Write(TextWriter writer, IReadOnlyList<SkinRamp> ramps);
```

Pure `TextReader`/`TextWriter` so tests need no filesystem.

### `Palettes/RampStore.cs`

```csharp
public sealed class RampStore(FullPath file)
{
    public Result<ImmutableArray<SkinRamp>, RampFailure> Load();   // missing file → empty, not a failure
    public Result<int, RampFailure> Save(IReadOnlyList<SkinRamp> customs);
}
```

Path is injected, so the app passes LocalState and tests pass a `TemporaryDirectory`. Built-ins are
never written — the store holds customs only, and the app concatenates.

### `Palettes/PalettePreview.cs`

Derives from `DotNext.Disposable` (idempotent-dispose rule; it owns an `SKBitmap`).

```csharp
public sealed class PalettePreview : Disposable
{
    public static Result<PalettePreview, BakeFailure> Create(SheetRecipe body);
    public Result<SKBitmap, BakeFailure> RenderIdleRow(SkinRamp ramp, int scale);
}
```

`Create` bakes the body **once**, un-recoloured, and caches the curated bitmap. `RenderIdleRow`
applies only `ramp.SubstitutionFrom(SkinRamps.Source)` to a crop, then upscales. The returned
`SKBitmap` is the caller's to dispose; the cached source bitmap is the `PalettePreview`'s and is
released by its `Dispose(bool)` override.

Two deliberate choices:

- **Recolour after curate.** Both operations are pixel-local, so they commute — the output is
  identical to the export path's recolour-then-curate. Recolouring the curated 240×1152 instead of
  the 1104×192 source is roughly a third of the work, which is what makes dragging a colour picker
  feel live.
- **Idle row only.** `SheetLayout.Clips[1]` is idle; rows `RowFor(1, 0..2)` are its three facings.
  Frame 0 of each is a 48×48 cell, giving a 144×48 strip — the three faces, which is what a skin
  ramp is judged on. Upscaled with the existing `PixelExact` (`SKFilterMode.Nearest`) sampling so
  the XAML `Image` never blurs it, since WinUI 3 `Image` has no nearest-neighbour option.

### `Baking/SheetWriter.cs`

```csharp
public static Result<ByteSize, BakeFailure> Write(FullPath directory, string name, RecyclableMemoryStream sheet);
```

`sheet.WriteTo(fileStream)` — zero-copy, and the only pooled-stream API that is, since the manager
sets `ThrowExceptionOnToArray = true`. Returns `ByteSize` rather than `long`, per the
types-over-primitives rule. Writes `<name>.webp`.

Two new `BakeFailure` members, appended so existing values keep their numbers:

```csharp
OutputDirectoryUnavailable,
OutputWriteFailed,
```

### `Baking/BatchBaker.cs`

```csharp
public readonly record struct BakeProgress
{
    public required string Name { get; init; }
    public required Optional<ByteSize> Written { get; init; }
    public required BakeFailure Failure { get; init; }   // default (0) means success
    public required int Completed { get; init; }
    public required int Total { get; init; }
}

public sealed record BatchSummary
{
    public required int Succeeded { get; init; }
    public required int Failed { get; init; }
    public required ByteSize TotalWritten { get; init; }
    public required bool Cancelled { get; init; }
}

public static Task<BatchSummary> RunAsync(
    ImmutableArray<SheetRecipe> recipes,
    FullPath outputDirectory,
    IProgress<BakeProgress>? progress,
    int maxParallelism,
    CancellationToken ct);
```

`BakeFailure` numbering from 1 is load-bearing here: `Failure == default` *is* the success signal,
so no separate bool is needed.

`Parallel.ForEachAsync` with `MaxDegreeOfParallelism = maxParallelism`. `RecipeBaker.Bake` is fully
synchronous CPU work, so each body is a synchronous bake inside the async loop. `maxParallelism`
defaults to `Environment.ProcessorCount` at the call site, not inside Core.

A failed recipe is reported and the run continues — one missing partial must not abort 78 good
sheets. Cancellation surfaces as `Cancelled = true`, not an exception.

### `Baking/FlattenedSheets.cs`

```csharp
public static ImmutableArray<SheetRecipe> CrossProduct(
    IReadOnlyList<SheetRecipe> bodies,
    IReadOnlyList<SheetRecipe> hair);
```

For each pair: `Name = $"{body.Name}_{hair.Name}"` (`body-01_hair-01`), `Layers = body.Layers`,
`Recolor = body.Recolor`, `Overlays = hair.Layers`. Seven bodies × nine hair = 63 sheets; with
layered output selected too, a full run is 79.

## App changes

### `Services/AppPaths.cs`

`App.LogDirectory` already computes `IsPackaged ? ApplicationData.Current.LocalFolder.Path :
AppContext.BaseDirectory`. That logic moves here as `static FullPath LocalState`, and `App` consumes
it. Ramps and pack settings then live beside the logs with no packaged/unpackaged branch anywhere
else — and notably no `LocalSettings`, which throws without package identity.

```
<LocalState>/logs/pixelforge-<date>.log     (existing)
<LocalState>/ramps.csv                       (custom ramps)
<LocalState>/packs.json                      (three pack directories)
```

### `Services/ISourcePackService`

Holds `Optional<SourcePacks> Current`, setters per pack, and a `Changed` event. Persisted to
`packs.json` through a `JsonSerializerContext` (reflection-based `JsonSerializer` is banned and
trim-unsafe). `FullPath` serialises through its own `FullPathJsonConverter`.

### `Services/IRampService`

Wraps `RampStore`. Exposes built-ins from `SkinRamps.All`, an `ObservableCollection` of customs, and
save / import / export. Name uniqueness is enforced across both sets.

### `Services/IPickerService`

`Microsoft.Windows.Storage.Pickers`, constructed with a `WindowId` cached on `App` so ViewModels can
call it without a XAML sender:

```csharp
Task<Optional<FullPath>> PickFolderAsync();
Task<Optional<FullPath>> PickOpenFileAsync(params string[] extensions);
Task<Optional<FullPath>> PickSaveFileAsync(string suggestedName, string extension, string filterName);
```

### `PalettePage` — new, fourth nav item

Two columns: a 320px ramp list, and the editor.

- **List** — `ListView` of every ramp, each row the name plus a five-swatch strip. Built-ins carry a
  "Built-in" caption and are read-only.
- **Editor** — five rows, each a swatch `Button` opening a `ColorPicker` flyout, with a hex `TextBox`
  beside it (`TwoWay`, `UpdateSourceTrigger=PropertyChanged` — without it UIA `set-value` silently
  does not commit).
- **Preview** — the recoloured idle row, updating as the picker moves.
- **Commands** — New, Duplicate, Rename, Delete, Import, Export, Save.

### `PipelinePage` — rewritten as batch export

Rows: header; output folder + Browse; mode `RadioButtons` (Layered / Flattened / Both); two
multi-select `ListView`s (bodies, hair) with per-row status (pending / baking / ✓ size /
✗ failure name); `ProgressBar` with an `n/total` caption; Export and Cancel.

`SelectionMode="Multiple"` gives checkboxes without a custom template. An `InfoBar` of severity
`Warning` blocks export until all three pack directories resolve, with a button navigating to
Settings.

`[RelayCommand(IncludeCancelCommand = true)]` supplies the cancel command; no hand-rolled
`CancellationTokenSource` plumbing in the ViewModel's public surface.

### `SettingsPage`

Three `SettingsCard` rows for the pack directories, each with the resolved path as its description
and a Browse button, plus a validity glyph. The existing theme radio moves into a `SettingsCard`.

### `MainWindow.xaml`

One `NavigationViewItem` — Palette, glyph `&#xE790;`, `AutomationId="NavPalette"` — and its case in
`OnNavigationSelectionChanged`. Window stays 1360×900: the widest new row is nav pane (320) +
padding (48) + two 300px lists + 16px spacing + a ~280px status column ≈ 1264.

### ViewModels

`PaletteViewModel` and `BatchExportViewModel`, both free of `Microsoft.UI.*`. `PaletteViewModel`
works in `SKColor`; the `ColorPicker`'s `Windows.UI.Color` is converted at the view edge, and a small
`Views/SkiaImageSource` helper turns an `SKBitmap` into a `WriteableBitmap` in code-behind. Render
and colour-type conversion are platform glue, which is what code-behind is for.

`SettingsViewModel` gains the three pack paths and their browse commands.

## Automation IDs

`ui-tests.ps1` fails a run without them.

| Page | Ids |
|---|---|
| Shell | `NavPalette` |
| Palette | `RampList`, `RampColumnSplitter`, `RampName`, `BtnNewRamp`, `BtnDuplicateRamp`, `BtnDeleteRamp`, `BtnImportRamps`, `BtnExportRamps`, `BtnSaveRamps`, `SwatchStep1`–`SwatchStep5`, `HexStep1`–`HexStep5`, `RampPreviewImage`, `BuiltInRampInfoBar`, `PaletteStatusBar` |
| Pipeline | `OutputFolderText`, `BtnBrowseOutput`, `ExportModeSegmented`, `BodySheetList`, `HairSheetList`, `SheetListSplitter`, `BtnExport`, `BtnCancelExport`, `ExportProgress`, `ExportProgressText`, `ExportStatusBar`, `PacksMissingInfoBar` |
| Settings | `CorePackPath`, `BtnBrowseCorePack`, `Expansion1PackPath`, `BtnBrowseExpansion1Pack`, `Expansion2PackPath`, `BtnBrowseExpansion2Pack` |

## Error handling

Expected failure stays a return value (`Result<T, TError>`), never an exception:

| Condition | Surface |
|---|---|
| Pack directories unset or missing | `InfoBar` on Pipeline; Export disabled |
| Layer partial missing | `BakeFailure.LayerNotFound` on that row; run continues |
| Encoder went lossy / round-trip mismatch | `EncoderProducedLossyOutput` / `RoundTripMismatch` on that row |
| Output directory gone | `OutputDirectoryUnavailable` before the run starts |
| Ramp CSV malformed | `RampFailure.StoreMalformed`; `InfoBar` on Palette, built-ins still load |
| Duplicate ramp name | `RampFailure.DuplicateName`; inline validation, save blocked |
| User cancels | `BatchSummary.Cancelled`, partial results kept |

`Guard.IsNotNull` stays for argument-null at public boundaries — a null there is a caller bug.

## Testing

Logic tests in `Core.Tests`, naming `Method_Scenario_Expectation`:

- `RampCsvTests` — round-trip; wrong step count; malformed rows; empty name.
- `RampStoreTests` — save/load through a `TemporaryDirectory`; missing file loads empty.
- `SheetWriterTests` — bytes on disk match the stream; returned `ByteSize`; missing directory.
- `FlattenedSheetsTests` — cross-product count and `body-NN_hair-NN` naming.
- `BatchBakerTests` — progress count equals recipe count; one bad recipe does not abort the rest;
  cancellation yields `Cancelled` with partial results.
- `RecipeBakerTests` — **the overlay guard**: an overlay partial painted in a source-ramp hex must
  come out unchanged after the body is recoloured. This is the test that holds the flatten fix.
- `PalettePreviewTests` — idle-row geometry is `144 × 48 × scale`; recolour applied; second
  `Dispose()` is a no-op.

UI behaviour goes in `ui-tests.ps1` as `Test-UI` blocks: navigate to Palette, edit a hex, assert the
swatch updates; select sheets, export to a temp folder, assert files appear. Screenshots in
`tests/ui-results/` get looked at — UIA passes while a page is visually broken.

## Risks

- **Flattened runs are large.** 63 sheets, each decoding four 828 KiB partials. Bounded parallelism
  and the pooled-stream manager are what keep this off the LOH; worth watching actual memory on a
  full "Both" run.
- **`ColorPicker` in a flyout fires continuously while dragging.** If per-move recolour of a
  144×48 crop proves visible, throttle to the `Flyout`'s close rather than adding a timer.
- **Built-in vs custom ramp identity is by name.** A custom named `Tone 1` would shadow a built-in,
  so `DuplicateName` is validated across both sets rather than within customs.

# JSON run manifest

**Date:** 2026-07-29
**Status:** implemented

## Problem

A bake run writes three CSVs beside its sheets:

| File | Describes |
|---|---|
| `index.csv` | curated row map — clip, facing, row, frame count, source column, cell size |
| `clips.csv` | full geometry — one row per clip/facing/frame, with frame duration |
| `sheets.csv` | one row per baked sheet: run id, slot stems, tone name, geometry |

CSV is right for the spreadsheet use case — "every sheet wearing `hat4`" is a column filter and
nothing else does that as well. It is wrong for the consumer use case. Corvus consumes baked
artifacts only: no package reference, no submodule, no build coupling, and therefore **no compiler
anywhere on the seam**. A consumer gets three flat files, has to stitch them together, and has to
be told out of band what the columns mean.

Three concrete gaps:

1. **No playback rate for curated sheets.** `frameDurationMs` (300) lives only in `clips.csv`,
   which describes the *other* geometry. A consumer of the curated sheet had to guess the cadence
   the art was authored for.
2. **`firstColumn` is a source column.** `Curate` left-aligns every clip, so its frames occupy
   output columns `0..frameCount-1`. A consumer reading `index.csv` can easily take `firstColumn`
   for an output column.
3. **Colours are never exported at all.** `sheets.csv` carries a tone *name*. The five hex steps
   behind it exist only in `SkinRamps`, so nothing downstream can match a UI swatch to the art.

## Decision

Add `manifest.json` — one schema-validated document per run — and ship the JSON Schema beside it.
**The three CSVs are unchanged.** This is additive.

### Packages

Of the five `Corvus.Text.Json` 5.2.10 packages considered, three are referenced:

| Package | Referenced | Why |
|---|---|---|
| `Corvus.Text.Json` | yes | Runtime. Carries its own `Utf8JsonWriter`/`JsonElement`/`Utf8JsonReader` — it is not a layer over System.Text.Json. Pulls in NodaTime 3.3.1. |
| `Corvus.Text.Json.SourceGenerator` | yes, `PrivateAssets="all"` | Schema → `readonly partial struct` with typed accessors, `EvaluateSchema()`, `Builder`, `Mutable`. |
| `Corvus.Text.Json.Compatibility` | yes | The System.Text.Json bridge: one type, `CorvusTextJsonPolyfills`, with `WriteTo(Utf8JsonWriter)`, `AsSTJsonElement`, `AsJsonNode`, `AsCorvusJsonAny`, `CreateFromSerializedInstance`, `AsOptional`. |
| `Corvus.Text.Json.CodeGeneration` | **no** | Its own README: *"Most users should use either the source generator or the CLI instead. This package is for building custom code generation tooling."* The generator already embeds it. |
| `Corvus.Text.Json.Validator` | **no** | Compiles *unknown* schemas at run time via Roslyn. Ours is known at build time and `EvaluateSchema()` already validates. Referencing it would put the C# compiler inside a shipping MSIX. |

Note `Corvus.Text.Json` already ships a built-in `Corvus.Text.Json.Compatibility` *namespace*
(`ValidationResult`, `ValidationContext`, `Polyfills`); the separate package is specifically the
System.Text.Json bridge.

### The schema is the contract

`src/TheOmenDen.PixelForge.Core/Schemas/pixelforge-manifest-v1.json`, JSON Schema draft 2020-12,
`$id: https://schemas.corvusconnection.app/pixelforge-manifest-v1.json`. It is listed twice in the
csproj on purpose:

- `AdditionalFiles` — how the source generator reads it at compile time.
- `EmbeddedResource` — how `RunManifest` copies it into the export directory at run time.

The export folder therefore carries its own contract. The manifest's `$schema` is the *relative*
filename, so an editor validates the pair with no network access.

### Document shape

```json
{
  "$schema": "pixelforge-manifest-v1.json",
  "schemaVersion": "1.0.0",
  "runId": "019faf60-3e18-7a4c-915b-a1f8362b4552",
  "palette": {
    "sourceRamp": { "name": "Default Tone", "isHuman": true, "steps": ["#73172D", "..."] },
    "ramps":      [ { "name": "Tone 4 (Green)", "isHuman": false, "steps": ["#184E3A", "..."] } ]
  },
  "layouts": {
    "curated": {
      "width": 240, "height": 1152, "cellSize": 48, "columns": 5, "rows": 24,
      "frameDurationMs": 300,
      "facings": ["south", "west", "east"],
      "clips": [ { "name": "walk", "frameCount": 3, "sourceColumn": 0,
                   "rows": { "south": 0, "west": 1, "east": 2 } } ]
    },
    "full": {
      "width": 1104, "height": 192, "cellSize": 48, "columns": 23, "rows": 4,
      "frameDurationMs": 300,
      "facingRows": { "south": 0, "west": 1, "east": 2, "north": 3 },
      "clips": [ { "name": "walk", "columns": [1, 2, 1, 0],
                   "isRenderedByDefault": true, "reverseDrawOrder": false } ]
    }
  },
  "sheets": [
    { "name": "body-01", "file": "body-01.webp", "geometry": "curated",
      "tone": "Default Tone",
      "slots": { "bottom": "bottom1", "top": "top11", "head": "head1" } }
  ]
}
```

Decisions inside that shape:

- **`layouts` members are optional, `minProperties: 1`.** A layout appears only when the run
  produced that geometry — the same rule that already keeps a curated-only export from leaving a
  `clips.csv` describing files that are not there.
- **Curated clips carry `rows` per facing; full clips do not.** In curated geometry the row is
  `clip * 3 + facing`, so it varies per clip and is stated per clip. In full geometry the facing
  *is* the row for every clip, so it sits once on the layout as `facingRows`.
- **`sourceColumn` is documented as provenance only**, with the schema description saying so.
- **Full `columns` is playback order** — `walk` is `[1, 2, 1, 0]` — and the schema says it must
  not be re-sorted or de-duplicated, because doing so is the obvious mistake.
- **A sheet's `geometry` is the key under `layouts` that describes it.** Both come from the same
  generated constant, so they cannot drift to `"Curated"` against a schema saying `"curated"`.
- **Absent beats blank.** An unfilled slot and an unapplied tone are omitted. The CSV writes an
  empty cell because a spreadsheet renders the text `null` as data; JSON can say "not applicable".
- **`palette.ramps` is the distinct tones the run actually applied**, in first-use order, matched
  case-insensitively — the identity rule `SkinRamps.IsBuiltIn` enforces. `sourceRamp` is always
  present, because a consumer needs to know what was substituted *from*.

### Composed by hand, released only if it validates

`RunManifest` writes through Corvus's `Utf8JsonWriter` using the generated `JsonPropertyNames.*Utf8`
spans, then parses the composed bytes back and calls `EvaluateSchema()` before anything reaches
disk. Two different guarantees:

- **Names** are compile-checked — renaming a property in the schema breaks `RunManifest.cs`.
- **Shape** is run-time-verified — an invalid document returns
  `BakeFailure.ManifestSchemaViolation` and writes nothing at all.

The generated `Source`/`Builder` API was the alternative. It makes an invalid document
*unrepresentable* rather than merely undeliverable, which is stronger — but its values are
`ref struct`s, so the nested arrays here (sheets of slots, layouts of clips of rows) cannot be held
in locals or projected with ZLinq. The verified path buys the same guarantee at the boundary that
actually matters, which is the file, and it is the bargain `LosslessWebp.EncodeVerified` already
strikes elsewhere in this pipeline.

## Files

| File | Change |
|---|---|
| `Directory.Packages.props` | three `Corvus.Text.Json` 5.2.10 versions, with the two rejections documented |
| `Core.csproj` | package refs, `AdditionalFiles` + `EmbeddedResource`, `NoWarn` for generated-code doc diagnostics |
| `Core/Schemas/pixelforge-manifest-v1.json` | new — the contract |
| `Core/Baking/RunManifestDocument.cs` | new — `[JsonSchemaTypeGenerator]` anchor |
| `Core/Baking/RunManifest.cs` | new — compose, verify, write |
| `Core/Baking/BakeFailure.cs` | `ManifestSchemaViolation` |
| `Core/Baking/BatchManifest.cs` | folder→slot mapping extracted to `StemsBySlot`, shared with `RunManifest` |
| `App/ViewModels/BatchExportViewModel.cs` | run id hoisted, one `NotifyIfFailed` added |
| `.editorconfig` | `CTJ001` off under `tests/` |
| `Core.Tests/Baking/RunManifestTests.cs` | new — 16 tests |

## Build traps found

Four, all of which cost a build cycle and are worth recording:

1. **`[JsonSchemaTypeGenerator]`'s path resolves relative to the source file, not the project.**
   A project-relative path fails with `CRV1000: Unable to locate the root document`. Hence
   `"../Schemas/pixelforge-manifest-v1.json"` from `Baking/`.
2. **The generator emits ~1400 `CS1572`/`CS1573`** (`<param>` tags that do not match the
   signatures beside them) plus one `CS1574`. With `GenerateDocumentationFile` and
   `TreatWarningsAsErrors` these are build errors.
3. **That suppression cannot be scoped to generated code.** `.editorconfig`'s
   `generated_code = true` exempts *analyzer* diagnostics; the compiler's own `CS`-prefixed
   warnings on source-generated trees are not configurable per file. It is a project-level
   `NoWarn` on `Core` — the only project hosting the generator.
4. **Corvus ships a `CTJ001` analyzer that flows transitively** and requires `"name"u8` over
   `"name"` wherever a `ReadOnlySpan<byte>` overload exists — including on System.Text.Json's own
   `JsonElement.GetProperty`. It is why `RunManifest` uses the generated `*Utf8` spans throughout,
   and it is disabled under `tests/` on the same reasoning that already disables `CA1861` there.

## Verification

- `dotnet build TheOmenDen.PixelForge.slnx` — 0 warnings, 0 errors.
- `dotnet test` — 211 passed, 0 failed.
- A three-sheet run (two curated with different tones, one full) was written and inspected: 6,525
  bytes, `walk` columns preserved as `[1, 2, 1, 0]`, unfilled slots absent, the full-geometry sheet
  carrying no `tone`.

## Not done

- **Corvus has not been updated to read this.** The manifest is written; nothing consumes it yet.
- **`schemaVersion` is not enforced against the schema's own `$id`.** They are two literals that
  must be bumped together when the shape breaks.
- **The `$id` host does not serve the schema.** `https://schemas.corvusconnection.app/` is a stable
  identifier, not a live URL; the copy in the export directory is what consumers actually resolve.

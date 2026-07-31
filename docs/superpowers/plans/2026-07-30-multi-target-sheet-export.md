# Multi-target sheet export — implementation plan

**Date:** 2026-07-30
**Spec:** `docs/superpowers/specs/2026-07-30-multi-target-sheet-export-design.md`
**Method:** TDD per phase. Each phase ends green (`dotnet build` Debug *and* Release, `dotnet test`)
and is committed before the next begins.

## Architecture

Two-project split. `Core` is `net10.0`, `IsAotCompatible`, and holds no Windows types; the app
project holds Views, ViewModels and platform glue. Phases 1-5 are entirely `Core` and are verifiable
by `dotnet test` with no window. Phase 6 is the only UI work, and it is last so that every phase
before it can be proven headless.

## Decisions on the plan's open questions

| Question | Decision | Why |
|---|---|---|
| Does `sheets.csv` gain a `Format` column? | **Yes** | `Geometry` is already a column and `Format` is its exact peer. Without it, telling a WebP row from a PNG row means parsing the extension out of `File`, which is precisely what the spreadsheet use case cannot do. Additive; existing readers ignore it. |
| Where do Time Fantasy sheets land? | **A sibling top-level folder**, added to `ExportNames.Reserved` and `OrphanScan`'s folder list | Keeps `heroes/` and `attachments/` exactly as Corvus knows them. The packs render at different scales (x4 against x3), so mixing them under `heroes/` would put incompatible art in one folder. |
| Phase ordering | **UI last**, as planned | Every Core phase is provable by `dotnet test` before any XAML exists. |

## Phase 1 — Output format

Unblocks both engines. No new art, no new geometry.

1. `SheetFormat` enum in `Core/Baking` — `Webp = 0`, `Png = 1`. `Webp` is `0` for the same reason
   `SheetGeometry.Curated` is: a recipe that says nothing cannot change the Corvus contract. Member
   order matches the `Segmented` added in step 23, following `ExportMode`'s convention that the
   control's index *is* the enum value.
2. `SheetWriter.Extension` (a `const`) becomes `ExtensionFor(SheetFormat)` **and**
   `IsSheetFile(string)`. One mapping answering two questions, so a third format cannot half-land.
3. `SheetRecipe.Format` property, defaulting to `Webp`; `RelativePath` derives its extension from it.
4. `LosslessPng` beside `LosslessWebp`, mirroring `EncodeVerified`: encode, decode, compare.
5. `RecipeBaker` / `BatchBaker` route to the encoder the recipe names.
6. **`OrphanScan.Collect` filters through `IsSheetFile`, not the WebP literal.** It currently matches
   `EndsWith(SheetWriter.Extension)`, so once PNGs exist every stale PNG becomes invisible and the
   scan reports a clean tree while orphans accumulate.
7. `BatchManifestRow` / `sheets.csv` gain the `Format` column.

**Tests:** golden byte-identical curated+WebP output (the one that matters); `LosslessPng` round
trip; `OrphanScan` seeing both extensions. `SheetRecipeTests` asserts the literal `"villager_01.webp"`
in three places and must be updated.

## Phase 2 — Correct `Assemble`'s invariant

8. `SheetBaker.Assemble` validates that layers agree **with each other** rather than that they match
   Time Elements, still returning `LayerGeometryMismatch`.
9. `LayerComposite` takes its surface size from the first layer instead of `SheetLayout`.

`Curate` is untouched and keeps its hard `SourceGeometryMismatch` check.

**Tests:** accepts consistent non-Time-Elements geometry; still rejects mismatched layers; `Curate`
still rejects a non-23x4 assembly.

## Phase 3 — Time Fantasy palette

10. `TimeFantasyRamps` in `Core/Palettes` — the four skin steps plus the `#354048` outline.
11. `TimeFantasyTone` record (`Outline`, `Contrast`, `InputBlack`) with
    `Derive(palette) -> RampSubstitution`, evaluated over distinct colours rather than per pixel.
12. Skin mapping `{0,1,2,3} -> {0,1,2,4}`, producing a substitution onto any `SkinRamp`.

**Tests:** the equivalence test — a derived substitution applied to a sheet equals the tone curve
applied per pixel. That equivalence is the entire justification for the design, because it is what
keeps `EncodeVerified` byte-exact. Plus the skin mapping table, and `IsIdentity` behaviour on a
five-entry table.

## Phase 4 — Time Fantasy geometry

13. `TimeFantasyLayout` — 26x36 cells, 6x4 grid, direction table, ping-pong `0 -> 1 -> 2 -> 1` with
    column 1 the stand pose.
14. Bearing-invariant test: every diagonal is its row's cardinal minus 45 degrees of compass bearing.
15. Time Fantasy pack root, **optional** — see Risks.
16. ~~Recipes for Time Fantasy sheets~~ — **superseded**. The goal is eight-way motion for the
    *Time Elements* characters; the Time Fantasy sheet is the reference for what that looks like,
    not art to ship in its place. Delivered instead:
    - `SourcePack` on the recipe, naming the palette a recolour reads **from**. `AssembleLayers`
      had `SkinRamps.Source` hard-coded, so a Time Fantasy recipe would have matched nothing and
      returned the sheet in its authored palette silently — the same failure class as the
      `OrphanScan` bug, one layer deeper.
    - `FacingResolution`, answering any of the eight compass points from whatever facings a pack
      has. Time Elements ships no diagonals in any pack and they cannot be synthesised, so eight-way
      movement is served by resolving each heading to the nearest available facing and publishing
      the table. See the spec's "Eight-way movement for Time Elements".

    A curated Time Fantasy *selection* is deliberately not built. `RoostSheets` exists because spec
    079 names exactly what ships; there is no equivalent here, and picking from the pack's 21 sheets
    would be inventing product decisions.

## Phase 5 — `manifest.json` 1.2.0

17. Bump `src/TheOmenDen.PixelForge.Schema/Schemas/pixelforge-manifest-v1.json`, adding
    `cellWidth`, `cellHeight`, `columns`, `rows`, `format`, `recommendedScale` and the
    row-to-direction table.
18. `RunManifest` writes the new fields.

**Tests:** schema validation, and that a Corvus-shaped run still validates.

## Phase 6 — UI

19. `ExportFormat` enum in `ViewModels`, member order matching the XAML.
20. PipelinePage `Segmented` for format — **code-behind `SelectionChanged`, never a TwoWay
    `x:Bind`**, copying `ExportModeSegmented` exactly. The existing comment records why: `Segmented`
    applies its initial index from `OnApplyTemplate`, after this page's bindings are live, so a
    TwoWay binding races the user and silently overwrites their choice.
21. ~~SettingsPage `SettingsCard` + Browse button for the Time Fantasy pack~~ — **deferred**. After
    the phase-4 reframe no Time Fantasy art is baked, so this row would let a user configure a pack
    nothing consumes. `SourcePacks.FantasyRoot` exists and is tested; the picker lands when
    something reads it. The risk note about keeping it outside the three-pack readiness gate still
    applies whenever that happens.
22. `AutomationId` on both new controls — `ui-tests.ps1` fails the run without them — plus `Test-UI`
    blocks, and a look at `tests/ui-results/` screenshots, since UIA assertions pass while a page is
    visually broken.

## Risks

- **The three-pack readiness gate.** SettingsPage's InfoBar states the Assets and Pipeline pages stay
  empty until all three Time Elements packs are set. If the Time Fantasy root joins that gate, every
  existing user's app goes blank until they supply a pack they may not own. It must be optional and
  outside the gate.
- **`SourcePacks` has three `required` `FullPath` members.** A fourth `required` field breaks every
  construction site, tests included. The Time Fantasy root should be `Optional<FullPath>` or a
  separate type.
- **Build Release, not only Debug.** `PublishAot` enables the trim/AOT analyzers only outside Debug,
  so a violation in the new encoder path is invisible in a Debug build.
- **The SIMD substitution assumes distinct source colours.** The Time Fantasy table has five entries
  mapping to five distinct targets, so it holds — but the Phase 3 equivalence test is what proves
  that rather than assuming it.
- **A second `ui-tests.ps1` run crashes the app** (`docs` records this as an open native issue), so
  the suite is run twice before Phase 6 is called green, and the `SlotExpander_*` flake is baselined
  on stashed changes first.

## Definition of done, per phase

`dotnet build TheOmenDen.PixelForge.slnx` clean in Debug **and** Release, `dotnet test` green, then a
commit. No phase is left partially applied across a commit boundary.

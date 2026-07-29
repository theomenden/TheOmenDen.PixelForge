# Full-library batch baking

**Date:** 2026-07-29
**Status:** Approved for planning

## Context

The bake path works end to end — `RoostSheets` → `RecipeBaker` → `BatchBaker` → `SheetWriter`, 45
green tests, verified lossless WebP. What it bakes is a **hard-coded table of sixteen recipes**:
one outfit (`bottom1` + `top11` + `head1`), nine hand-picked hairstyles, seven skin tones.

The three Time Elements packs hold far more than that. Scanned on 2026-07-29:

| slot | bases | files | draw order |
|---|---:|---:|---:|
| shadow | 1 | 1 | 0 |
| backextra | 9 | 57 | 1 |
| backhair | 11 | 112 | 2 |
| bottom | 20 | 159 | 3 |
| top | 28 | 186 | 4 |
| head | 20 | 66 | 5 |
| hair | 25 | 236 | 6 |
| frontextra | 6 | 38 | 7 |
| hat | 14 | 74 | 8 |
| weapon | 22 | 66 | 9 |
| **total** | **156** | **995** | |

Base names **never collide across the three packs** — core is `hair1-12`, `top0-12`, `bottom0-8`,
`head1-8`; expansion 1 continues `hair13-21`, `top13-23`, `bottom9-15`, `head9-17`; expansion 2
continues `hair22-25`, `top24-27`, `bottom16-19`, `head18-20`. Expansion slots that add named rather
than numbered pieces (`crown1`, `backpack1`, `tail1`, `daggerL`, `bow1arrow1`, `shield1L`) do not
collide either. So one flat catalog keyed by `slot` + base name is unambiguous, and the owning pack
is derivable rather than something the user must state.

`_cN` suffixed files are **colour variants**, verified by pixel diff: `top1_c1..c4` change garment
pixels and leave every skin-ramp pixel and the silhouette untouched (0 ramp pixels changed, 0 alpha
changed). On heads they are eye colours. `hat4_c1`/`_c3` deliberately recolour the ramp-coloured trim
(1,074 pixels), and `hair1_c3`/`_c4` alter the silhouette slightly (269 alpha changes) — variants are
not guaranteed to be shape-identical, only shape-similar.

## Goals

- Enumerate the whole library from disk instead of a hard-coded table.
- Select any number of bases per slot, with or without colour variants, and bake the cross product
  across skin tones.
- Bake **complete characters** — equipment, hats, weapons, back and front extras — not just body and
  hair.
- Emit the full 23×4 source geometry alongside the curated sheet, so the bow draw, climb and the
  north facing survive for consumers that want them.
- Keep the Corvus spec-079 curated sheet byte-identical to what has already shipped.

## Non-goals

- Editing sprite pixels. The canvas stays inert.
- Changing the curated sheet geometry, `index.csv`, or the Corvus layered contract.
- An animated preview. A single composed still is in scope; playback is not.
- Redistributing pack art. The packs stay outside the repo; only baked output is committed.

## Library findings (standing rule 0)

Enumerated before designing, including where a package genuinely lacks the thing.

| Need | Found | Decision |
|---|---|---|
| Directory walk for the catalog | `ZLinq.FileSystem` `1.5.6` — `FileSystemInfoExtensions.Children()` / `.Descendants()`, already referenced by `Core` | **Use it.** Value-enumerable walk, no new package, and it is the project's stated replacement for `Directory.EnumerateFiles` + LINQ. |
| Natural ordering (`hair2` before `hair10`) | **Absent.** None of the referenced Meziantou packages (`Globbing`, `FullPath`, `ByteSize`, `TemporaryDirectory`, StronglyTypedId pair) ships a natural or logical string comparer. BCL has no portable one; `StrCmpLogicalW` is a Win32 P/Invoke and `Core` must stay Windows-free. | **No comparer needed.** The catalog already parses each file name; ordering by the parsed `(prefix, number, suffix)` tuple is correct by construction and also handles `shield1L`, `bow1arrow1`, `daggerL`, `crown1`. |
| Per-pixel palette substitution | `System.Numerics.Vector<T>` — `Vector.Equals`, `Vector.ConditionalSelect`, `Vector<uint>.Count`, span ctor and `CopyTo`; `MemoryMarshal.Cast<byte, uint>` for a zero-copy view | **Use it.** See "SIMD recolour". |
| — same need, colour library | `ColorHelper` 1.8.1 — full public surface is `ColorConverter` (RGB/HSL/HSV/CMYK/XYZ/YIQ/YUV/HEX conversion), **`ColorComparer.Equals`**, `ColorGenerator`, and the colour structs | **Not for this.** `ColorComparer.Equals` compares two *individual* colours across models — a scalar helper, not an image operation. Driving 10⁸ pixels through it would construct an `RGB` per pixel and convert per comparison. Genuinely useful for palette-editor equality checks; wrong tool for the bake loop. |
| — same need, Skia | **`SKRuntimeEffect`** (`CreateColorFilter`, `ToColorFilter`, `SKRuntimeColorFilterBuilder`) — present in SkiaSharp 4.151 | **Available, and rejected on merit — not absence.** SkSL *can* express an arbitrary RGB→RGB lookup, so the claim "no library can do this" (carried in `SheetBaker`'s remarks today) is **wrong and must be corrected in the code comment too**. Rejected because colour filters operate in float: an exact index-for-index substitution would need tolerance comparisons, and this pipeline's round-trip verification demands byte-exactness. It also adds a shader-compile failure mode and makes the substitution untestable as a pure function in `Core`. `SKColorFilter.CreateTable` (four independent per-channel lookups) and `CreateColorMatrix` (linear transform) genuinely cannot express it. |
| Filename parsing | BCL `ReadOnlySpan<char>` + `LastIndexOf` | `Meziantou.Framework.Globbing` matches paths, it does not decompose names. A span split on a trailing `_c<digits>` is three lines. |
| Cross-product expansion | **Absent** from ZLinq and System.Linq alike — neither ships a cartesian product over a variable number of sequences. | Hand-rolled odometer over the ten slot lists, ~15 lines. Stated because rolling our own is the exception. |
| Manifests | `CsvHelper` 33.1.0, already used by `SheetIndex` | Same writer for the two new manifests. |
| Bounded parallel bake | `Parallel.ForEachAsync`, already chosen in `BatchBaker` with its reasoning recorded | Unchanged. |
| Decoded-layer cache | `DotNext.RandomAccessCache<,>` exists and would fit | **Not used.** See "Deliberately skipped". |
| Collapsible per-slot groups | `CommunityToolkit.WinUI.Controls.SettingsControls` — **`SettingsExpander`** (`ItemsSource`, `ItemTemplate`, header content) and **`SettingsCard`**, already referenced | **Use them.** A slot is a header (name, count, variants toggle) plus an expandable item list — the control's exact shape. Hand-rolling `Expander` + `StackPanel` is the documented anti-pattern. |
| Live filter + sort of slot lists | `CommunityToolkit.WinUI.Collections` — **`AdvancedCollectionView`**, `SortDescription<T>`, already referenced | **Use it.** Filtering by a search box without rebuilding `ObservableCollection`s by hand. **`SortDescription<T>` (generic), never the string-property overload** — the package's own message says the latter resolves the property by reflection and is not trim-safe, and Release publishes `PublishTrimmed=true`. |
| Tone swatch grid, variant chips | `CommunityToolkit.WinUI.Controls.Primitives` — **`UniformGrid`**, **`WrapPanel`**, already referenced | **Use them.** No hand-rolled `Grid` row/column arithmetic for a swatch grid that reflows. |
| Mode → description switching | `Primitives` **`SwitchPresenter`** / `CaseCollection` | **Use it** in place of the current `ModeDescription` string property plus three consts — the mapping becomes declarative XAML with no converter. |
| Search box | Built-in `AutoSuggestBox` | 156 bases across ten slots needs a filter; nothing to add. |
| Queued run notifications | `CommunityToolkit.WinUI.Behaviors` `StackedNotificationsBehavior`, already wired on this page | Unchanged. |

## The pixel evidence that drives the design

Every base partial was scanned for pixels drawn in the five source-ramp hexes
(`73172D BB7547 DBA463 F4D29C FAF4D6`). The scan reproduces the two facts already on record —
`hair1` at 2.7% and `hat4` at 9.7% — which is what validates it.

| slot | bases carrying ramp pixels | reading |
|---|---|---|
| head | 20 / 20 | faces — skin |
| top | **23 / 28** | bare arms and hands. Only `top11`, `top12`, `top17`, `top22`, `top27` are fully covered |
| bottom | 3 / 20 | `bottom0` bare legs 56.5%, `bottom10` 19.8%, `bottom11` 7.6% |
| weapon | 13 / 22 | **not skin** — see below |
| hat | 2 / 14 | `hat4` 9.7%, `hat13` 2.7% — trim |
| hair | 1 / 25 | `hair1` 2.7% — highlights |
| shadow, backextra, backhair, frontextra | 0 / 27 | nothing |

**Hands are on the `top` layer, not the `weapon` layer.** `arrow1` is 10.7% ramp and has no hand on
it; `shield1L` is 22.1%; meanwhile `sword1`, `sword2`, `sword4`, `sword5`, `daggerL`, `daggerR`,
`gun1` and `wand1` are all 0%. The ramp hexes on weapons are wooden shafts, bow limbs and shield
trim — material, not skin.

Two conclusions follow, and they are the load-bearing part of this design:

1. **The current whole-assembly recolour is wrong for the full library.** It is safe today only
   because the one hand-picked outfit has zero ramp pixels outside the face. Against 23 of 28 tops it
   would leave bare arms and hands in the source tone.
2. **The weapon layer must not be recoloured.** Recolouring it turns a Bone-toned character's wooden
   bow white and a Green-toned character's shield trim green.

Point 2 **diverges from the generator**, which applies its `PaletteSwaps` globally with no per-layer
opt-out. Decided in favour of the game-correct result over generator fidelity. Moving `Weapon` into
the skin set is a one-line change to `AssetSlot.IsSkinBearing` if that trade is ever reversed.

## Design

### 1. `Core/Catalog` — the asset catalog

```
AssetSlot        enum; value IS the generator draw order (Shadow 0 … Weapon 9)
AssetPartial     readonly record struct: Slot, Pack, Base, Variant, Path, sort key
AssetCatalog     Scan(SourcePacks) → Result<AssetCatalog, CatalogFailure>
CatalogFailure   enum, numbered from 1
```

`AssetSlot`'s lowercase member name is the folder name in all three packs, so no slot→folder table is
needed. Declaring the enum in draw order means compositing is `OrderBy(slot)` rather than a second
list that can drift from `Settings.json`'s `CharacterLayers`.

`AssetPartial.Variant` is `0` for the base file and `N` for `_cN`. Parsing splits on a trailing
`_c<digits>`; anything else is part of the base name, which is what keeps `bow1arrow1` and `daggerL`
intact.

The scan reads directory entries only — no image is decoded — so it stays fast enough to run on every
pack-path change, exactly where `BatchExportViewModel.Reload` runs today.

Optional slots come from `Settings.json` `IsOptional`: everything except **Bottom, Top, Head** may be
absent. Optional slot lists therefore carry an explicit `(none)` entry, so one run can produce both a
hatted and a hatless character.

### 2. Per-layer recolour

`AssetSlot.IsSkinBearing` is true for **Bottom, Top, Head** and false for everything else. Each layer
is recoloured *before* compositing, in draw order.

This **deletes** `SheetRecipe.Overlays` and the "drawn after the recolour" workaround. Overlays
existed to protect hair from a substitution that ran over the flattened assembly; per-layer recolour
makes hair, hat and back-hair safe by construction. It is also the only formulation that handles
`backhair`, which draws *below* the body (order 2) and therefore could never have been an
after-the-fact overlay.

`RoostSheets.Flattened` and `RecipeBakerOverlayTests` go with it.

### 3. SIMD recolour

`SheetBaker.Recolor` currently does a `FrozenDictionary<uint, SKColor>` lookup per pixel. That is the
wrong structure for a five-entry table, and per-layer recolour triples how often it runs: three
skin-bearing layers × 211,968 px × a 168-sheet run ≈ 10⁸ lookups.

Replaced with `System.Numerics.Vector<uint>`:

- `MemoryMarshal.Cast<byte, uint>` gives a zero-copy `Span<uint>` view of the pixel buffer.
- Five `Vector.Equals` + `Vector.ConditionalSelect` pairs per vector width — 8 pixels at a time under
  AVX2, 16 under AVX-512. `Vector<T>` rather than `Vector256<T>` so the JIT picks the width instead
  of the source pinning an ISA.
- A scalar tail handles the remainder, and that same scalar path is the reference the test compares
  the vector path against.

**Alpha is part of the comparison, not masked out of it.** The obvious formulation masks off alpha,
compares RGB, separately derives an "is opaque" mask, ANDs the two and re-ORs alpha into the result.
None of that is necessary here. The source art has **strictly binary alpha**, so every opaque pixel
has `A = 255`; packing the ramp colours with `A = 0xFF` and comparing the whole 32-bit pixel excludes
transparent pixels automatically. The loop becomes compare-and-select with no masking, no alpha
extraction and two fewer vector constants.

Verified rather than assumed — all 995 partials decoded through the pipeline's own
`Rgba8888`/`Unpremul` conversion:

```
files scanned            : 995
files w/ partial alpha   : 0
distinct partial alphas  : []
ramp pixels, old test    : 571,148     (alpha != 0 && rgb in ramp)
ramp pixels, full-32 test: 571,148     (pixel == 0xFF______)
DISAGREEMENTS            : 0
```

This introduces **no new assumption**: `SheetBaker.Assemble` already depends on binary alpha — it is
why its premultiplied round trip is documented as "exact rather than merely close". The failure mode
is shared and worth naming: a future pack authored with antialiased edges would break the premul
round trip *and* silently drop soft ramp pixels from the substitution. Binary alpha is a property of
this art, not of PNG.

**Byte-order trap.** The scalar key is `0xRRGGBB`. The same pixel read as a little-endian `uint` from
RGBA8888 is `0xAABBGGRR`. The vectors need the ramp packed R-in-low-byte with alpha set, so `SkinRamp`
gains `PackedRgba` beside the existing `Pack`, with a test pinning the two against each other.

`SubstitutionFrom` returns a `RampSubstitution` (two `ImmutableArray<uint>`, from and to) instead of a
`FrozenDictionary`; `System.Collections.Frozen` leaves `SheetBaker` entirely. Note that even the
*scalar* five-compare form beats the current per-pixel hash lookup — SIMD is the second win, not the
first.

### 4. Recipe and planning

```csharp
// IsSkin, not "Recolor" — the layer states whether it carries skin; the recipe states which
// tone to apply. Naming both ends "Recolor" would read as the same switch in two places.
readonly record struct AssetLayer(FullPath Path, bool IsSkin);

sealed record SheetRecipe
{
    required string Name { get; init; }
    required ImmutableArray<AssetLayer> Layers { get; init; }   // draw order
    Optional<SkinRamp> Tone { get; init; }                      // None = keep source tone
    SheetGeometry Geometry { get; init; }
}
```

A layer is substituted when `IsSkin` **and** `Tone` has a value. `AssetLayer.IsSkin` is seeded from
`AssetSlot.IsSkinBearing`, so the per-partial exclusion escape hatch named under "Open risk" is a
matter of clearing one flag, not a change to the baker.

`BatchPlan.Expand(selection)` is an odometer over the ten slot lists plus the tone list. Output stem
is the selected file stems in draw order plus the tone, omitting empty slots and the default tone:
`bottom1_top11_head1_hair15c3_hat4_tone-4`.

The planned count is shown live, as today. Past ~1,000 files an `InfoBar` warns; it does not block —
a large deliberate run is legitimate.

### 5. Two geometries

- **Curated** — unchanged. 240×1152, 8 clips × 3 facings, `row = clip * 3 + facing`, `index.csv`.
  The Corvus contract; this design does not touch `SheetLayout.Clips`, `SheetBaker.Curate` or
  `SheetIndex`.
- **Full** — the 1104×192 assembly encoded as-is: all 23 columns, all 4 facings. Nock/Bow, Climb and
  the north facing survive.

Full geometry ships `clips.csv`, carrying all twelve `Settings.json` animations with their **real
frame order** — `Walk` is `[1,2,1,0]` and `Arms Up` is `[4,5,4,3]`, playback orders rather than
contiguous spans, which the curated `SourceColumn`+`FrameCount` model cannot express — plus Climb's
`ReverseDrawOrder` flag.

`AnimationClip` gains a `Frames` array used by the full manifest. `SheetLayout.Clips` keeps
`SourceColumn`/`FrameCount` so the curated path cannot drift.

A third manifest, `sheets.csv`, maps every output file to its per-slot composition and tone. At 168
files the filename alone is not a usable index.

**Identifiers.** A partial's identity is `(slot, base, variant)` and a sheet's is its stem — both
natural keys, so neither needs a surrogate. The one identifier this adds is a **batch run id**,
stamped into `sheets.csv` and onto every log scope for the run, so a directory of output can be traced
back to the run that produced it. It is a **UUIDv7** via `Guid.CreateVersion7()` (BCL, .NET 9+), not
`Guid.NewGuid()` — v7 is time-ordered, so run ids sort chronologically in a log or a manifest instead
of scattering. Same rule applies to any `[StronglyTypedId]` over `Guid` added later.

### 6. UI — `PipelinePage`

The two `Bodies`/`Hair` `ListView`s become ten **`SettingsExpander`** groups, one per slot, built from
Toolkit controls rather than hand-assembled panels:

| Element | Control | Why not hand-rolled |
|---|---|---|
| Slot group | `SettingsExpander` (`ItemsSource` + `ItemTemplate`) | Header carries the slot name, live selected count and the variants `ToggleSwitch`; the expander supplies the collapse, keyboard model and automation peer. |
| Slot row | `SettingsCard` in the item template | Gives the Windows 11 row metrics, header/description slots and hit target for free. |
| Filtering | `AutoSuggestBox` + `AdvancedCollectionView` | Live filter over 25 hairs or 28 tops without rebuilding collections by hand. Ordering is a `SortDescription<T>` over the catalog's parsed `(prefix, number, suffix)` key — the generic, trim-safe form, never the string-property overload. |
| Tone swatches | `UniformGrid` | Reflows without row/column arithmetic. |
| Variant chips | `WrapPanel` | Up to eight `_cN` chips per base, wrapping. |
| Mode description | `SwitchPresenter` + `CaseCollection` | Replaces the `ModeDescription` property and its three string consts with declarative XAML. |
| Run notices | `StackedNotificationsBehavior` | Already wired; unchanged. |

The per-slot **"include colour variants"** toggle is what makes ticking `hair15` mean one file or
eight. Every interactive element keeps an `AutomationId`, as `ui-tests.ps1` requires — including the
`SettingsExpander` headers, which must be addressable to expand a group.

The mode `Segmented` is repurposed from Layered/Flattened/Both to **Curated / Full / Both**. Layering
is now expressed by *what is selected*: head+top+bottom is a body sheet, hair alone is a hair sheet —
which is exactly the Corvus two-texture contract. A **"Load Roost selection"** button ticks the
spec-079 set so that deliverable stays one click away.

`PalettePreview` is generalised to render the idle row of the current combination, giving a composed
still before committing to a run.

### 7. What this replaces

`RoostSheets`' hard-coded tables are subsumed by the picker: seven bodies is head+top+bottom × seven
tones, nine hair is nine hair bases with no tone. It survives as the committed spec-079 selection
behind the preset button, so the shipped contract stays reproducible.

## Testing

`Core.Tests`:

- Catalog parse — `_cN` split, non-numeric names (`bow1arrow1`, `shield1L`, `daggerL`, `crown1`),
  variant `0` for base files.
- Ordering — `hair2` before `hair10` across all slots.
- Cross-product expansion — count correctness, `(none)` on optional slots, required slots rejected
  when empty.
- Per-layer recolour — a bare-armed top recolours, `hat4` and `hair1` do not, `arrow1` keeps its
  wooden tan.
- SIMD recolour — vector path equals scalar path over a buffer whose length is deliberately not a
  multiple of `Vector<uint>.Count`; `Pack` and `PackedRgba` agree.
- Binary-alpha dependency, made explicit — a transparent pixel whose RGB happens to equal a ramp
  colour is left alone, and a pixel with `A = 128` and ramp RGB is **not** substituted. The second
  case cannot occur in the shipped packs (verified across all 995 partials) and the test exists to
  document the boundary rather than to guard live input.
- Full-geometry manifest — frame orders match `Settings.json`, Climb carries `ReverseDrawOrder`.
- Curated regression — a Roost recipe still produces the same bytes it produces today.

`ui-tests.ps1`: slot group expand/collapse, variant toggle changing the planned count, geometry mode
switch, preset button, export run. Screenshots reviewed in `tests/ui-results/` — UIA passes while a
layout is visually broken.

## What stays hand-written, and why

Rolling our own is the exception and needs a stated reason. After enumerating the packages, exactly
three pieces of this design have no library form:

1. **The palette substitution loop** — hand-written by *choice*, not by absence. `SKColorFilter`
   genuinely cannot express it (`CreateTable` is four independent per-channel lookups,
   `CreateColorMatrix` is a linear transform), and `ColorHelper.ColorComparer` is a scalar cross-model
   equality helper rather than an image operation. But **`SKRuntimeEffect` can** — SkSL expresses an
   arbitrary RGB→RGB lookup fine. It is rejected because colour filters work in float, this
   substitution must be byte-exact to survive round-trip verification, and a shader compile is a new
   failure mode for a loop that is ten integer vector operations. `SheetBaker.Recolor`'s existing
   remark claiming no library can do this is inaccurate and gets corrected as part of this work.
2. **The cross-product odometer.** Neither ZLinq nor System.Linq ships a cartesian product over a
   variable number of sequences. ~15 lines over the ten slot lists.
3. **The filename decomposition.** `Meziantou.Framework.Globbing` matches paths; it does not
   decompose names. A span split on a trailing `_c<digits>` is three lines, and it is what supplies
   the ordering key, so it removes the need for a natural-sort comparer rather than adding code.

Everything else — the walk, the collapsible groups, the filtering, the swatch grid, the notifications,
the manifests, the parallel run — is a package that is already referenced.

## Deliberately skipped

- **Decoded-layer cache.** A 168-sheet run is roughly 900 PNG decodes; the lossless WebP encode
  dominates. `DotNext.RandomAccessCache<,>` is the tool if a run ever measures decode-bound, and
  caching all 995 partials would be ~800 MB, so it would need bounding regardless.
- **Animated preview.** A composed still covers the "did I pick the right hat" question.
- **Path-length guard on long stems.** Ten slots plus a tone can approach `MAX_PATH` under a deep
  output directory; `sheets.csv` is the authoritative index if a name is ever truncated.
- **Per-batch weapon-recolour toggle.** One data-driven default, one line to change.

## Accepted trade-offs

Both confirmed 2026-07-29. Neither is an open question; they are recorded so the reasoning survives.

**Weapons keep their authored colour.** `AssetSlot.IsSkinBearing` is false for `Weapon`, so a
Bone-toned character carries a tan wooden bow and a Green-toned one a brown leather shield. This
diverges from the Elements generator, which applies its `PaletteSwaps` globally. The evidence is that
hands are not on the weapon layer at all — `arrow1` is 10.7% ramp with no hand on it, `shield1L` is
22.1%, while `sword1`, `sword2`, `sword4`, `sword5`, `daggerL`, `daggerR`, `gun1` and `wand1` are 0%.
Those hexes are wood, leather and shield trim.

**Tan garments on skin-bearing slots will take the tone.** `bottom10` (19.8% ramp) and `bottom11`
(7.6%) may be tan leather rather than bare legs; `bottom` is a skin-bearing slot, so they are
recoloured along with the skin. Accepted deliberately — the alternative is a per-partial exclusion
list maintained by eye against 156 bases, which is more upkeep than the artefact is worth. The escape
hatch remains one flag: `AssetLayer.IsSkin` is seeded from the slot but carried per layer, so a single
partial can be excluded later without touching the baker.

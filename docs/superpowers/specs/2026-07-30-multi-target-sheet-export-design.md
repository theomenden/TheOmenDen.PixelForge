# Multi-target sheet export: Unity, MonoGame, and the Time Fantasy pack

**Date:** 2026-07-30
**Status:** approved

## Problem

Every baked artifact today is shaped by one consumer. Corvus reads a 240x1152 WebP in the
`Curated` geometry, and the pipeline is built around that: `SheetWriter.Extension` is a
`const string = ".webp"`, `SheetLayout` is a static class of `const int`s describing a single
source shape, and `SheetBaker.Assemble` rejects any layer that is not exactly 1104x192.

Two things now need to come out of it that it cannot express.

**Unity 6.5 and MonoGame 3.8.5 as consumers.** Neither imports WebP. Unity's `TextureImporter`
handles BMP, EXR, GIF, HDR, IFF, JPEG, PICT, PNG, PSD, TGA and TIFF; MonoGame's runtime
`Texture2D.FromStream` goes through StbImageSharp (PNG, JPG, BMP, TGA, PSD, GIF) and MGCB's
`TextureImporter` has no WebP path either. The pipeline currently cannot write a file either
engine can open. Both Time Elements *and* Time Fantasy output must reach them.

Taken as an accepted working assumption rather than a verified one: this comes from the documented
importer format lists, not from a trial against the actual Unity and MonoGame projects. If either
turns out to have a WebP path, §1 gets simpler — nothing else in this design depends on it. Corvus
continues to receive WebP regardless.

**A second source pack.** finalbossblues' Time Fantasy characters, plus the 8-directional
diagonal addon, in a geometry unrelated to Time Elements:

| | Time Elements | Time Fantasy |
|---|---|---|
| Cell | 48x48 | 26x36 |
| Sheet | 1104x192 (23x4) | 78x144 (3x4); diagonal addon 156x144 (6x4) |
| Facings | S, W, E, N | D, L, R, U; addon carries 8 |
| Structure | paper-doll, 10 slot folders of aligned partials | finished characters, one PNG each |
| Outline | `#000000` | `#354048` |
| Skin ramp | 5 steps, `#73172D` to `#FAF4D6` | 4 steps, `#6C3C4A` to `#F2F0C5` |
| Figure size | 12-16 x 21-22 px | 17 x 29 px |

The two packs share no colour. `#BB6749` against `#BB7547` is the closest pair and is still three
channels apart, so today's exact-match `RampSubstitution` recolours **zero pixels, silently**,
if pointed at Time Fantasy art.

## Decision

Four additive changes. No source-geometry abstraction. The Corvus deliverable stays byte-identical
and a golden test enforces it.

### 1. Output format becomes a per-recipe choice

```csharp
public enum SheetFormat
{
    Webp = 0,   // default: a recipe that says nothing cannot change the Corvus contract
    Png  = 1,
}
```

`SheetFormat` joins `SheetRecipe`, defaulting to `Webp` for the same reason `SheetGeometry.Curated`
is `0`. `SheetWriter.Extension` (a `const`) becomes `SheetWriter.ExtensionFor(SheetFormat)`, and
`SheetRecipe.RelativePath` derives from it — preserving the "two writers, one mapping" property its
remarks already argue for.

A new `LosslessPng` mirrors `LosslessWebp`'s `EncodeVerified` shape: encode, decode, compare. PNG is
lossless by construction, but keeping the round trip means the two encoder paths cannot drift, and
`SKPngEncoderOptions` still exposes filter and level settings that can be got wrong.

No naming collision is introduced: `foo_full.webp` and `foo_full.png` already differ, so
`LayerPlan`'s `FullSuffix` needs no third axis.

**`SheetGeometry.Full` is what ships to the engines.** It already exists to keep "the nock/bow draw,
climb and the north facing, which the curated geometry drops" — precisely what a game with
8-directional movement needs and what Corvus's fixed-`y` wander model does not.

### 2. `Assemble` is over-constrained; correct the invariant

`SheetBaker.Assemble` rejects any layer that is not `SourceWidth x SourceHeight`. Compositing's
actual requirement is that **layers agree with each other**. It is `Curate` that genuinely needs the
23x4 grid, and it already checks that independently.

- `Assemble` validates layer-to-layer consistency, still returning `LayerGeometryMismatch`.
- `Curate` keeps its hard `SourceGeometryMismatch` check, unchanged.
- `LayerComposite` takes its surface size from the first layer instead of from `SheetLayout`.

This is what lets a 156x144 sheet through. It is a correction to an over-tight check, not a new
abstraction.

**Explicitly rejected:** turning `SheetLayout`'s constants into a `SourceProfile` record. It would
have exactly one non-Corvus implementation, and that implementation does no remapping at all — an
abstraction whose only new instance says "copy the image unchanged". Revisit when a third pack needs
a genuine remap; nothing here obstructs that.

### 3. Palette: two substitution sources, one substitution mechanism

Both produce a `RampSubstitution`. The vectorised `Substitute` loop, its binary-alpha assumption,
and `LosslessWebp.EncodeVerified`'s byte-exactness are all untouched.

`RampSubstitution` already supports tables of any length: `From`/`To` are `ImmutableArray<uint>` with
a derived `Length`. Five is stated only in `SkinRamps.StepCount`, which is about skin, not about
substitution.

#### 3a. Outline and shadow

```
#354048 -> #000000
```

Verified by pixel-mapping a cell: `#354048` traces the entire silhouette from y=7 downward *and*
fills solid at y=33-35. It is both outline and shadow. Time Elements draws opaque `#000000` outlines
and ships `shadow.png` as pure opaque `#000000` — so this single entry is correct for both roles at
once.

Recorded for a future consumer: Time Fantasy **bakes its shadow into the sprite**, where Time
Elements keeps it as a separate, omittable slot (`RoostSheets` deliberately excludes it). A consumer
wanting to fade, offset or suppress shadows can do so for Time Elements and cannot for Time Fantasy.
Extracting it is not in scope.

#### 3b. Skin: exact target

Time Fantasy's 4 shades onto a `SkinRamp`'s 5, dropping `#F4D29C`:

| TF | | TE step |
|---|---|---|
| `#6C3C4A` | -> | 0 `#73172D` |
| `#BB6749` | -> | 1 `#BB7547` |
| `#DEBC70` | -> | 2 `#DBA463` |
| `#F2F0C5` | -> | 4 `#FAF4D6` |
| | | 3 `#F4D29C` dropped |

Chosen from a rendered comparison of all three candidate mappings across Default Tone, Tone 3 and
Tone 6 (Bone). Collapsing a middle step preserves both endpoints. Dropping `#FAF4D6` instead
compressed the range visibly — at Tone 3 it flattened into a single muddy brown — and dropping
`#DBA463` lost shadow definition.

#### 3c. Everything else: tone-matched

For clothing, hair, weapons and tiles, where no target ramp exists, finalbossblues' own
"Matching Time Fantasy to Elements" procedure:

1. exact-replace the outline colour with `#000000`
2. raise contrast
3. lift the Levels input black point

**Evaluated once over the sheet's distinct palette, not per pixel.** Both packs use 5-11 colours, so
the tone curve collapses into a `From[]/To[]` table — which is exactly `RampSubstitution`. This is
the load-bearing idea of the whole design: it keeps `EncodeVerified`'s byte-exactness intact. A float
shader would destroy it, which is why `SheetBaker.Recolor`'s remarks already reject
`SKRuntimeEffect`.

Parameterised as a `TimeFantasyTone` record (`Outline`, `Contrast`, `InputBlack`) with the artist's
44 and 12 as **defaults, not constants**. Photoshop's non-legacy Brightness/Contrast is a soft
S-curve; a linear model of it clipped three of four steps to 255 in testing. The curve is fitted
against one reference sheet exported from Photoshop and locked with a test.

### 4. Sheet geometry reaches the engines through `manifest.json`

Bump `pixelforge-manifest-v1.json` to schema version **1.2.0**. Per sheet, add `cellWidth`,
`cellHeight`, `columns`, `rows`, `format`, `recommendedScale` (see Scale below), and a
row-to-direction table. One schema-validated,
already-versioned artifact rather than a second file describing the same sheet — that duplication is
the drift `SheetRecipe.RelativePath` was introduced to eliminate.

Unity slices from it via an editor script (Sprite Mode `Multiple`, Grid By Cell Size, pivot
`Bottom`); MonoGame reads the same JSON. Nothing engine-specific ships from PixelForge.

#### Time Fantasy diagonal layout

Cardinals on the left half, diagonals on the right, confirmed by pixel-exact silhouette correlation
against `$tf_template.png` (0 mismatched pixels of 936 for every cell in columns 0-2):

| Row | Cols 0-2 | Cols 3-5 |
|---|---|---|
| 0 | down | down-right |
| 1 | left | down-left |
| 2 | right | up-right |
| 3 | up | up-left |

**Invariant, to be asserted in a test:** every diagonal is its own row's cardinal minus 45 degrees of
compass bearing. S 180 to SE 135, W 270 to SW 225, E 90 to NE 45, N 360 to NW 315. A transcription
slip breaks the rule and fails the test rather than shipping a character that strafes.

Columns 3-5 are separately drawn poses, not mirrors — mirror-pairing scored 50-58 mismatched pixels
where a true pair scores 0.

#### Walk cadence

The 3-frame walk is **ping-pong, `0 -> 1 -> 2 -> 1`**, with column 1 the stand pose. Confirmed by the
pack's own `frames/base/` naming (`down_stand`, `down_walk1`, `down_walk2`). This belongs in the clip
table, not in each consumer.

#### Scale

Time Fantasy figures are 17x29; Time Elements figures are 12-16x21-22 inside a 48x48 cell, the
padding being headroom for hats, weapons and back-extras. The packs do **not** compose at 1:1 —
Time Fantasy characters are about 32% taller.

**Time Elements x4 : Time Fantasy x3** gives 88px against 87px, a 1.1% mismatch, with both factors
integer so pixel art stays crisp. The manifest records the recommended factor per sheet; it is **not
baked**, since baking x4 into a curated sheet is 16x the pixels for information the consumer can
apply at import. `SheetBaker.Upscale` already exists for a consumer that wants it baked.

## Testing

| Test | Guards |
|---|---|
| Golden: curated + WebP output byte-identical | The Corvus contract. The one that matters. |
| `LosslessPng` round trip | Mirrors the WebP encoder tests. |
| Derived substitution == per-pixel tone curve | The equivalence that justifies the entire palette design. |
| `Assemble` accepts consistent non-TE geometry, rejects mismatched layers | The corrected invariant in §2. |
| Diagonal bearing rule holds for all four rows | The direction table in §4. |
| `TimeFantasyTone` against the Photoshop reference sheet | That the fitted curve matches the artist's output. |

## Open items

None blocking. The `TimeFantasyTone` curve constants are fitted during implementation against a
reference sheet, which is a task input rather than an unresolved decision.

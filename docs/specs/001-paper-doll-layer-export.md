# Spec 001: Paper-doll layer export — hero, loadout and attachment directories

**Status:** Approved (2026-07-30) — re-approved after the `arms_up` correction. See Revision Log.
**Date:** 2026-07-30
**Source:** `docs/superpowers/prompts/2026-07-30-family-export-directories.md`, amended in session
**Consumer:** Corvus-Connection specs 072 (Ashen Rookery avatar overlay), 077 (sprite pack CDN), 079
(Roost avatar look customization)

---

## Problem

PixelForge exists so a person does not hand-author a hundred-odd sprite sheets. The Ashen Rookery
wants a **paper doll** — viewers equip and unequip cosmetics live with `!equip <slug>` against owned
inventory — and today the exporter cannot feed one. Three things stand in the way:

1. **Everything is baked flat, so nothing is composable.** Corvus consumes a two-layer contract
   today: `RoostSheets.Hair` bakes hair as its own sheet with no body under it, "a true stacked
   layer in the overlay, sharing the body's grid so one frame map describes both." That stops at two
   layers because ordinary export only ever emits fully-flattened cross products.
2. **A second run corrupts the first.** `RunArtifacts.WriteAllAsync` overwrites `index.csv`,
   `clips.csv`, `sheets.csv` and `manifest.json` while leaving the earlier run's `.webp` files on
   disk — present, and described by nothing.
3. **`ExportMode.Both` collides on disk today.** `BatchPlan.StemFor` does not encode geometry, so
   `ExportPlan.Recipes` returns two recipes with identical `Name` and different `Geometry`, and
   `BatchBaker` writes both to `directory / (name + ".webp")` from inside `Parallel.ForAsync`. Two
   workers race `File.Create` on one path; `sheets.csv` then claims two rows wrote the same file.
   The mode is user-selectable via `ExportModeSegmented`.

Success is an export folder the rookery can pin as a versioned CDN pack, whose index always
describes exactly what is on disk, and whose layers can be stacked at runtime for all ten slots.

---

## Scope

### In

- A nested export tree: `curated/`, `heroes/`, `loadouts/`, `attachments/`.
- A **paper-doll layer set**: every optional partial baked once, tone-independent, shared across
  every hero and every loadout.
- **Hero** directories keyed on the `AssetSlots.IsRequired` trio, with stable append-only numbering
  read back from a schema-validated `heroes.json`.
- **Loadouts**: a named pool of equipment per class, written once as `loadouts/<class>.json`.
- Root roll-ups (`heroes.csv`, `classes.csv`) and root manifests covering `heroes/` +
  `attachments/`.
- `curated/` as a self-contained deliverable with its own manifest set.
- Fixing the repeat-run orphan bug (report, do not delete) and the `ExportMode.Both` collision.
- Fixing the `File` column, which is composed independently in two writers today.
- Schema `1.1.0`: optional `hero` on `sheet`. (`curatedClip.drawOrder` was cut — see Revision Log.)
- Correcting two wrong comments about `ReverseDrawOrder` — see Revision Log.
- `RoostSheets` grows to name hats, weapons and extras for a ten-slot spec 079.
- UI: hero-prefix and class-name text boxes, validation, and a reworked planned-count label.

### Out

- **Baked composites of a hero wearing a loadout.** Cut deliberately — see Integrations. Nothing in
  the rookery can address one, and the cross product was ~99.6% of the output.
- **`SheetGeometry.Packed`.** A real win (1,080 KiB → 168 KiB decoded per sheet) but a different
  layout, not a trim: 240×1152 becomes ~512×84, so the overlay's `steps()` animation and `cellSize`
  handling must be retaught. **Its own spec, sequenced immediately after this one and before the
  rookery adopts ten slots**, so the overlay learns one geometry rather than two.
- Per-clip splitting. The atlas stays whole; every `.webp` is a full eight-clip sheet.
- Any change to curated or full **geometry**. Only paths, names and which recipes get planned.
- A class authoring UI beyond one text box.
- Agreeing the ten-slot contract with Corvus. Code lands first; the conversation follows.
- Byte-for-byte preservation of the existing sixteen deliverable sheets — explicitly dropped.

---

## Domain Model

### The tree

```
<OutputFolder>/
  heroes.json                      cumulative hero registry, schema-validated, read back
  heroes.csv                       same data, spreadsheet view
  classes.csv                      one row per class -> its equipment pool
  index.csv                        curated geometry
  clips.csv                        full geometry (only when the run produced full)
  sheets.csv                       every sheet under heroes/ and attachments/
  manifest.json                    this run's contract
  pixelforge-manifest-v1.json      schema copy manifest.json's $schema points at
  pixelforge-heroes-v1.json        schema copy heroes.json's $schema points at
  pixelforge-loadouts-v1.json      schema copy each loadouts/*.json points at

  curated/                         spec-079 deliverable, self-contained
    body-01.webp … hair-09.webp … + the new ten-slot picks
    index.csv  sheets.csv  manifest.json  pixelforge-manifest-v1.json

  heroes/
    villager_01/                   one base body trio
      villager_01.webp             source ramp
      villager_01_tone-2-olive.webp
      …one per tone
    noble_01/

  loadouts/
    ranger.json                    which attachment stems this class offers, per slot
    caster.json

  attachments/                     tone-independent, shared by every hero and every loadout
    hair/    hair1.webp  hair7.webp  hair1c2.webp
    hat/     hat3.webp
    weapon/  sword1.webp
    …one subdirectory per optional AssetSlot the run ticked
```

### The three concepts

| Concept | What it is | Named by |
|---|---|---|
| **hero** | one distinct base body — the `AssetSlots.IsRequired` trio (`Bottom`, `Top`, `Head`). **Tone is not part of hero identity**; it is a filename suffix. | slugged user prefix + per-prefix number: `villager_01` |
| **loadout** (class) | a named **pool** of equipment: which optional partials this class offers, per slot. Produces no sheets — it references attachment layers. | slugged user-typed class name: `ranger` |
| **attachment** | one optional partial, baked alone on transparency, tone-independent | the partial's `AssetPartial.Stem`: `hair1`, `hair1c2` |

The required trio is clothing — bottom is legs, top is torso, head is the face — so the trio names a
base outfit and equipment layers over it. **A loadout is hero-independent**: `ranger` stacks onto any
body, which is why it is written once at the root rather than under each hero.

Multiple choices per slot make a loadout a **pool, not a fixed kit** — which is what `!equip <slug>`
against owned inventory wants. A class offering `hair1;hair7` means either is legal for that class.

### Naming rules

- **Hero label** — `Slug.Create(prefix)` + `_` + number formatted `:00`, widening naturally past 99
  (`villager_99`, `villager_100`). Numbering is **per prefix**: each starts at `01`, so one prefix's
  heroes sort adjacently in a flat listing.
- **Hero base sheet** — the hero label plus the tone suffix: `villager_01_tone-2-olive.webp`. The
  tone segment is **omitted for `SkinRamps.Source` and for no tone at all**, reusing
  `BatchPlan.StemFor`'s existing rule and its `ToneSlug` options.
- **Attachment sheet** — `AssetPartial.Stem` + `.webp`, in `attachments/<AssetSlots.FolderName>/`.
  `AssetPartial`'s own docs state base names never collide across the three packs, and `Stem` renders
  variants as `hair1c2`, so per-slot subdirectories are collision-free without further work.
- **Geometry** — `SheetGeometry.Curated` is the default and stays unmarked; `Full` appends `_full`
  before the tone segment. This mirrors how the tone segment is itself omitted for the default ramp.

---

## API Contract

### Core, new

| Type | Shape |
|---|---|
| `LayerPlan` (new file, `Core/Baking/`) | `Expand(selections, tones, geometry, heroLabels)` → `Result<ImmutableArray<SheetRecipe>, PlanFailure>`. Bodies cross-product **within** the required trio and take the tone axis; every optional partial emits exactly one recipe with no tone and no multiplication. `Count` returns the label breakdown. Pure — hero labels arrive already resolved, so no I/O enters the planner. |
| `HeroRegistry` (new file, `Core/Baking/`) | Reads and writes `heroes.json` through the generated `HeroRegistryDocument`; writes `heroes.csv` through `Csv.Writer`. Assigns labels append-only. |
| `LoadoutWriter` (new file, `Core/Baking/`) | Writes `loadouts/<class>.json` through the generated `LoadoutDocument`, and `classes.csv` through `Csv.Writer`. |
| `HeroRegistryDocument`, `LoadoutDocument` (new, `Schema/`) | One-line `[JsonSchemaTypeGenerator] public readonly partial struct` each, matching `RunManifestDocument`. No hand-written code. |
| `BakeFailure.OutputDirectoryCreateFailed` | A destination directory could not be created. Appended after `ManifestSchemaViolation`. |
| `BakeFailure.HeroRegistrySchemaViolation` | The composed `heroes.json` or a `loadouts/*.json` failed its own schema. Nothing is written — same discipline as `ManifestSchemaViolation`, which keeps its existing narrower meaning. |
| `PlanFailure.HeroRegistryUnreadable` | `heroes.json` exists but is not valid JSON, or fails `EvaluateSchema()`. Appended after `RequiredSlotEmpty`. |

### Core, changed

| Type | Change |
|---|---|
| `SheetRecipe` | Gains `public string Directory { get; init; } = ""` — a root-relative, `/`-separated destination. `BatchBaker.RunAsync` therefore stays at **five** parameters. |
| `BatchBaker.RunAsync` | Before `Parallel.ForAsync`, walks the run's distinct `Directory` values and creates each one, single-threaded. Any failure returns before a single decode happens. |
| **The `File` rule, in *two* places** | `File` is composed both by `BatchManifest.RowFor` and by `RunManifest` (`writer.WriteString(SheetNames.FileUtf8, recipe.Name + SheetWriter.Extension)`). Both become `Directory + "/" + Name + ".webp"`, normalised to forward slashes. **They must share one helper**, for the same reason `StemsBySlot` is shared: two writers, one mapping, or `sheets.csv` and `manifest.json` disagree about where a sheet is and nothing catches it. |
| `RunManifest` | Emits optional `hero` on each `sheet` and optional `drawOrder` on each `curatedClip`, through generated `*Utf8` property-name constants — never string literals. |
| `RunArtifacts.WriteAllAsync` | Also writes `heroes.json`, `heroes.csv`, `classes.csv` and `loadouts/*.json`, and copies the two extra schema files. Four parameters today, five after — under the cap. |
| `BatchPlan` | Unchanged. `Expand` and `Count` survive because `ExportPlan.Still` still needs one dressed composite for the canvas preview. Export uses `LayerPlan`; the preview uses `BatchPlan`. |
| `RoostSheets` | Gains `HatPicks`, `WeaponPicks` and extra-slot picks beside `BodyLayers` and `HairPicks`; every recipe gains `Directory = "curated"`. |

### File contracts

**`heroes.json`** — the registry, cumulative across runs, schema-validated, and the file numbering
reads back from. Read → merge → validate → write; a document failing its own schema is
`BakeFailure.HeroRegistrySchemaViolation` and **nothing is written**.

```json
{
  "$schema": "pixelforge-heroes-v1.json",
  "schemaVersion": "1.0.0",
  "heroes": [
    { "name": "villager_01", "prefix": "villager", "number": 1,
      "body": { "bottom": "bottom1", "top": "top11", "head": "head1" },
      "assignedInRun": "019a4f…" }
  ]
}
```

`prefix` and `number` are stored **separately as well as joined into `name`**, so resolving the
per-prefix high-water mark never parses `villager_01` back into parts — a parse that can fail, and
that a prefix containing a digit or an underscore makes ambiguous. `body` is closed to extra
properties. `assignedInRun` is the run that **first minted this number**, not the last run to touch
the hero.

**`loadouts/<class>.json`** — the equipment pool. Slot keys are `AssetSlots.FolderName`; a slot the
class does not use is absent rather than empty.

```json
{
  "$schema": "../pixelforge-loadouts-v1.json",
  "schemaVersion": "1.0.0",
  "class": "ranger",
  "slots": { "hair": ["hair1", "hair7"], "hat": ["hat3"], "weapon": ["sword1", "bow1"] },
  "assignedInRun": "019a4f…"
}
```

**`heroes.csv`** / **`classes.csv`** — the same data, write-only, for the spreadsheet. Nothing reads
them back, which is why `sheets.csv` exists beside `manifest.json` today.

```
Hero,Prefix,Number,Bottom,Top,Head,AssignedInRun
villager_01,villager,1,bottom1,top11,head1,019a4f…

Class,Shadow,BackExtra,BackHair,Hair,FrontExtra,Hat,Weapon,AssignedInRun
ranger,,backextra2,,hair1;hair7,,hat3,sword1;bow1,019a4f…
```

**`manifest.json`** — `schemaVersion` `1.0.0` → `1.1.0`. **One** optional property, on an object
already open, which the schema's own policy makes a MINOR bump — same `$id`, same `-v1` filename, no
new generated type:

- `sheet.hero` (string) — present on a hero base sheet, absent on an attachment, which belongs to no
  hero.

There is deliberately **no ordering field of any kind**. A consumer stacks by `AssetSlots.DrawOrder`,
which is constant across every curated clip and every facing — see Integrations. A sheet already
names its slot via its single-key `slots` object, so the slot is all a consumer needs.

---

## Authorization

Not applicable. Single-user packaged desktop app, no accounts, roles or tenancy. The only access
boundary is the filesystem, and it is the OS's. Recorded rather than omitted so a reader knows it
was considered.

---

## Edge Cases & Failure Modes

| Case | Behaviour |
|---|---|
| `heroes.json` missing | First run. Numbering starts at `01` for each prefix. |
| `heroes.json` unparseable, or fails `EvaluateSchema()` | `PlanFailure.HeroRegistryUnreadable`. **Nothing is written.** A schema check catches what a column count cannot — a `number` that is a string, a `body` missing `head`, a duplicate name. |
| Composed `heroes.json` or `loadouts/*.json` fails its schema | `BakeFailure.HeroRegistrySchemaViolation`. Nothing is written. |
| `heroes.csv` / `classes.csv` malformed or missing | Irrelevant — write-only, regenerated every run. |
| Prefix blank, slugs to empty, or slugs to `curated` / `heroes` / `loadouts` / `attachments` | `CanExport` false with a notice. A bad prefix can never start a run. |
| Class name blank | Legal. The run writes hero base sheets and attachment layers; no `loadouts/` entry is written. |
| Class named but no optional slot ticked | `CanExport` false — an empty pool is an unfinished intent, not a loadout. |
| Same body trio in a later run | Keeps its existing label, whatever prefix is typed. Renaming would break every path referencing it. |
| Hero in `heroes.json` not produced by this run | Entry kept. The number stays reserved forever and is never reused. |
| 100th hero for one prefix | `villager_100`. Sorts wrong lexically at the boundary; `heroes.json` sorts on the integer `number`. |
| Repeat run into an occupied folder | Overwrite in place. Re-running the same selection is idempotent — the filenames are identical. |
| Sheets on disk this run did not write | After a **successful** run, walk `heroes/` and `attachments/`, diff against what was written, report as a notice. Nothing deleted. Skipped on a cancelled or failed run. |
| A `loadouts/*.json` for a class this run did not build | Left alone. Loadouts are keyed by name and overwritten by name; a stale one is reported by the same orphan notice. |
| A destination directory cannot be created | `BakeFailure.OutputDirectoryCreateFailed`, returned before any bake work. |
| Overlay-only run (all three required slots empty) | Legal, and now the *main* attachment path rather than an edge case. No `heroes/` directory is created. |
| Zero tones ticked with a body selected | Already gated: `BatchPlan.Count` returns 0, so `CanExport` is false. `LayerPlan.Count` must preserve this. |
| Two packs shipping the same stem | Cannot happen — `AssetPartial`: "base names never collide across the three packs." |

---

## Non-Functional Requirements

**The output is small, and that is the point.** A run selecting 9 hair, 5 hats and 22 weapons across
7 tones produces **43 sheets**: 7 hero base sheets (the trio is skin-bearing, so one per tone) plus
36 attachment layers (tone-independent, baked once each). The cross-product design this replaced
produced 9,660 for the same selection. That difference is what makes the output shippable as a
versioned CDN pack via `az storage blob upload-batch` (spec 077).

**MAX_PATH is no longer a concern.** The longest name is `villager_01_tone-2-olive.webp` under
`heroes/villager_01/`. The prompt's trap — seven stems plus a 52-character slot-set segment — only
existed for baked composites.

**Decoded memory is the binding constraint, not draw calls.** The rookery is pure DOM/CSS with a
bounded roster (`Cap` default 18); spec 072 rules out a canvas/WebGL engine because "DOM/CSS handles
the ~18-avatar cap comfortably." A browser decodes each unique image URL once and shares it across
avatars, so worst case is every layer on screen at once: 43 × 1,080 KiB ≈ **45 MB decoded** in an OBS
browser source. `SheetGeometry.Packed` takes that to ≈ 7 MB and is the next spec.

**Parallelism and pooling are unchanged.** `Parallel.ForAsync` bounded by `MaxDegreeOfParallelism`,
`PooledStreams.Manager` as the single manager. Directory pre-creation is single-threaded, once.

---

## Integrations

### How the rookery consumes this

`syncLayers` (`roost.ts:378`) creates one `.layer` div per equipped cosmetic. **`body` is not a
layer** — it replaces the background image on the existing `.sprite` div, which maps exactly onto
hero base sheets at `heroes/<hero>/` and equipment at `attachments/<slot>/`. Frame stepping is a CSS
`steps()` animation on `background-position-x` shared by `.sprite` and `.layer`, so a layer is
frame-locked to its body for free.

### Why there is no ordering field: `arms_up` does not need one

Spec 079 documents a defect it chose to accept: **`arms_up` on south/east/west draws `top` above
hair**, because raised arms occlude the head, and a `.layer` has one static `z-index`. It warned
*"Every additional runtime slot re-opens this."* An early draft of this spec answered that with a
per-clip `drawOrder` array.

**The premise does not hold.** Three independent checks against the Core pack agree:

1. The generator's own machine-readable spec, `generator/Elements Gen 2.1/Windows/Settings.json`,
   gives `"Arms Up"` `"ReverseDrawOrder": false` — it composites in the **normal** order.
2. Compositing `bottom1 + top11 + head1` with `hair1`, `hair9` and `hair10` in normal order across
   **all four facings** and all three `arms_up` columns renders correctly. The raised arms sit
   outboard of and below the hair mass and stay fully visible, including with twin buns — the
   largest core silhouette — and including north, where the character faces away.
3. Compositing with hair below `top` renders **badly wrong**: `head` then draws over `hair` and
   erases it almost entirely, leaving a bare face. The ordering constraint is transitive — `hair`
   below `top` forces `hair` below `head` — so the "fix" is strictly worse than the defect.

So `AssetSlots.DrawOrder` is correct for every curated clip and every facing, and a per-clip array
would ship a false contract. Two supporting facts were also verified:

- **Curated geometry drops north entirely**, so `back_hair` inverting when facing north — 079's other
  named hazard — never arises in the shipped subset anyway.
- **Climb is not curated.** The eight are walk, idle, arms_up, crouch, jump, attack, heavy_attack,
  sleep_ko. `GeneratorClip.ReverseDrawOrder` is true for climb alone.

### `ReverseDrawOrder` is a permutation, not a reversal

`Settings.json`'s `CharacterLayers` carries **two** explicit orderings per layer. `DrawOrder` is
`0 shadow … 9 weapon` and matches `AssetSlot` exactly. `ReverseDrawOrder` is:

```
0 shadow  1 weapon  2 frontextra  3 bottom  4 top  5 head  6 hair  7 backextra  8 backhair  9 hat
```

The body core — `bottom → top → head → hair` — is **unchanged**. Only the back/front accessory pairs
swap, which is exactly right for a character seen from behind: what was behind comes forward.

Two comments in this repo describe it wrongly and are corrected as part of this pass:

- `Core/Spritesheets/GeneratorClip.cs` — "climb alone, where the character faces away and the body
  must occlude the hair." Hair sits above the body in **both** orderings; it is never occluded.
- `pixelforge-manifest-v1.json`, `fullClip.reverseDrawOrder` — "Whether this clip's layers composite
  back to front." It is not a back-to-front reversal, and a consumer that implemented it by reversing
  the array would render climb wrong. The description must state the permutation.

Nothing in PixelForge applies the reverse ordering today — curated excludes climb, and the bakers
composite in supplied array order — so this is a shipped-documentation fix, not a rendering fix.

### Why composites were cut

There is no code path that loads a pre-baked hero-and-loadout composite, and there cannot be while
`!equip`/`!unequip` drive appearance per viewer against owned inventory. A cross product would have
been ~9,653 sheets per hero that the only existing consumer cannot address, shipped through a
versioned blob container.

### Corvus / spec 079

The contract is being renegotiated from two slots to ten. This pass ships the writer side; agreement
follows. Nothing shipped breaks an existing consumer: `1.1.0` is additive, both new properties are
optional, and the closed `slots` object already enumerated all ten slots. A consumer pinned to
`1.0.0` keeps reading `curated/` unaffected.

### The schema project and generated types

`TheOmenDen.PixelForge.Schema` is a quarantine, and adding schemas does not compromise it. Its
`NoWarn` for `CS1572`/`CS1573`/`CS1574` is scoped by project because the Corvus generator emits
around 1400 mismatched `<param>` tags for one schema; that is safe **only** because nothing
hand-written lives there. Each new schema keeps it true — three lines and no code a human wrote:

```xml
<AdditionalFiles   Include="Schemas\pixelforge-heroes-v1.json" />
<EmbeddedResource  Include="Schemas\pixelforge-heroes-v1.json" />
```

```csharp
[JsonSchemaTypeGenerator("Schemas/pixelforge-heroes-v1.json")]
public readonly partial struct HeroRegistryDocument;
```

Rules that carry over and are easy to breach:

- **The generator path resolves relative to the *source file*, not the project directory.** A path
  that does not resolve fails with `CRV1000 "Unable to locate the root document"`.
- **`AdditionalFiles` and `EmbeddedResource` are a pair with different jobs.** The first feeds the
  generator at compile time; the second is how the schema is copied into the export folder at run
  time, because Corvus consumes baked artifacts with no build coupling. Adding one without the other
  compiles and then ships a document whose `$schema` points at a file that is not there.
- **Property names come from generated `JsonPropertyNames` constants, never literals.**
  `RunManifest` already writes `SheetNames.NameUtf8`, `FileUtf8`, `GeometryUtf8`, `ToneUtf8`,
  `SlotsValueUtf8`. A literal compiles and drifts silently; a renamed schema property must break the
  build.
- **Validate before writing.** Every document is composed, parsed back and `EvaluateSchema()`d before
  a file is opened, so an invalid document never reaches disk.
- **Generated types under `obj/` are never edited.** The schema JSON files are the source of truth.
- `Corvus.Text.Json.Compatibility` is referenced by **Core**, not by the Schema project.

---

## Acceptance Criteria

**Tree and naming**

- **AC-1** Given a selection filling the required trio, when the run completes, then
  `heroes/<slug>_NN/` exists and holds one base sheet per ticked tone.
- **AC-2** Given tone `SkinRamps.Source`, when a base sheet is written, then the filename carries no
  tone segment; given any other ramp, then it carries the slugged ramp name.
- **AC-3** Given ticked optional slots, when the run completes, then
  `attachments/<AssetSlots.FolderName(slot)>/<stem>.webp` exists once per partial, with no tone
  multiplication and no hero in its path.
- **AC-4** Given a class name and ticked optional slots, when the run completes, then
  `loadouts/<class>.json` exists once, listing every ticked stem under its slot key.
- **AC-5** Given two heroes and one class, when the run completes, then exactly one
  `loadouts/<class>.json` is written — a loadout is never duplicated per hero.
- **AC-6** Given `ExportMode.Both`, when the run completes, then curated and full sheets have
  distinct filenames (`_full` on the non-default) and no two recipes target one path.
- **AC-7** Given a blank class name, when the run completes, then no `loadouts/` entry is written and
  base sheets plus attachment layers still are.
- **AC-8** Given all three required slots empty, when the run completes, then no `heroes/` directory
  is created and only `attachments/` and `loadouts/` are populated.

**Numbering and the registry**

- **AC-9** Given no `heroes.json`, when a run assigns labels, then numbering starts at `01` for each
  prefix.
- **AC-10** Given a `heroes.json` mapping a body trio to `villager_01`, when a later run includes
  that trio, then it resolves to `villager_01` regardless of the prefix typed.
- **AC-11** Given a `heroes.json` holding 99 heroes for one prefix, when a hundredth is assigned,
  then `name` is `<prefix>_100` and `number` is the integer `100`.
- **AC-12** Given a `heroes.json` that is not valid JSON, or that fails `EvaluateSchema()` — a string
  `number`, a `body` missing `head`, an unrecognised key inside `body` — when a run is started, then
  it returns `PlanFailure.HeroRegistryUnreadable` and writes nothing at all.
- **AC-13** Given a hero present in `heroes.json` but absent from this run, when the file is
  rewritten, then its entry survives and its number is not reused.
- **AC-14** Given a composed `heroes.json` or `loadouts/*.json` that fails its schema, when the write
  is attempted, then it returns `BakeFailure.HeroRegistrySchemaViolation` and no file is opened.

**Validation**

- **AC-15** Given a prefix of `curated`, `heroes`, `loadouts` or `attachments`, when it is entered,
  then `CanExport` is false and a notice names the reserved word.
- **AC-16** Given a prefix that slugs to the empty string, when it is entered, then `CanExport` is
  false.
- **AC-17** Given `Slug.Create("Villager Guard")`, when a hero is labelled, then the directory is
  `villager-guard_01`.
- **AC-18** Given a class name with no optional slot ticked, when it is entered, then `CanExport` is
  false.

**Manifests and schemas**

- **AC-19** Given a completed run, when the root is read, then `heroes.json`, `heroes.csv`,
  `classes.csv`, `sheets.csv`, `manifest.json` and all three schema copies are present, and the
  manifests describe every sheet under `heroes/` and `attachments/` — and none under `curated/`.
- **AC-20** Given a `sheets.csv` row and the `manifest.json` entry for the same sheet, when both
  `File` values are read, then they are byte-identical root-relative forward-slash paths resolving to
  a file that exists.
- **AC-21** Given `manifest.json`, when validated, then `schemaVersion` is `1.1.0` and every
  attachment sheet's `slots` object has exactly one key.
- **AC-22** Given a hero base sheet's `manifest.json` entry, when read, then `hero` matches its
  directory segment and `slots` holds exactly `bottom`, `top` and `head`.
- **AC-23** Given an attachment sheet's `manifest.json` entry, when read, then `hero` and `tone` are
  both absent — an attachment belongs to no hero and no tone.
- **AC-24** Given `heroes.json`, `manifest.json` and each `loadouts/*.json`, when each is validated
  against the schema copy shipped beside it, then all pass and each `$schema` resolves to a file that
  exists.
- **AC-25** Given a Roost export, when `curated/` is read, then it holds its own `index.csv`,
  `sheets.csv`, `manifest.json` and **only** `pixelforge-manifest-v1.json`, and a subsequent batch
  export does not modify any of them.
- **AC-26** Given the `arms_up` frames composited in `AssetSlots.DrawOrder` for all four facings,
  when compared against the same frames with `hair` below `top`, then the `AssetSlots.DrawOrder`
  result leaves the raised arms visible and the hair intact. *Regression guard for the premise this
  spec was returned to In Review over — assert on pixels, not on a manifest field.*

**Failure and repeat runs**

- **AC-27** Given a destination directory that cannot be created, when the run starts, then it
  returns `BakeFailure.OutputDirectoryCreateFailed` before any recipe is baked.
- **AC-28** Given a folder holding sheets from a wider earlier run, when a narrower run completes
  successfully, then the extra files are reported as a notice and left on disk.
- **AC-29** Given a cancelled run, when it ends, then no orphan scan runs and no orphan notice is
  raised.

**UI**

- **AC-30** Given the batch page, when it loads, then a hero-prefix box and a class-name box are
  present, each with an `AutomationId` and `UpdateSourceTrigger=PropertyChanged`, and UI automation
  `set-value` commits to the view model.
- **AC-31** Given a selection, when the planned-count label updates, then it breaks the run down —
  heroes × tones plus attachments — rather than reporting one total.

---

## Deferred Decisions

| Decision | Chosen fallback | Revisit trigger |
|---|---|---|
| `SheetGeometry.Packed` | Its own spec, immediately after this one, **before** the rookery adopts ten slots — so the overlay is taught one new geometry rather than two. | This spec is approved. |
| Byte-identity of the existing sixteen deliverable sheets | **Dropped.** No baseline hashing; `curated/` is open to change as part of the ten-slot renegotiation, and `RoostSheets` may re-pick art. | The ten-slot contract is agreed and `curated/` is frozen again. |
| The ten-slot contract itself | Writer ships now at `1.1.0`, additive and backward-compatible. Corvus keeps reading `1.0.0`. | Corvus is ready to consume ten slots. |
| Whether `loadouts/*.json` needs its own schema | **Yes** — `pixelforge-loadouts-v1.json`. It is a shipped contract another system reads, which is the same category as `manifest.json`; write-only CSVs like `sheets.csv` are not. Costs three lines and a one-line partial struct. **Author's call, not the developer's — overrule in review if two schemas are enough.** | The rookery decides to read `classes.csv` instead. |
| `classes.csv` schema | **No.** It is a write-only spreadsheet view of `loadouts/*.json`, the same relationship `sheets.csv` has to `manifest.json`. | Something starts reading it back. |
| `pixelforge-heroes-v1.json` / `-loadouts-v1.json` version policy | Mirror the manifest schema's stated policy verbatim: an optional property on an open object is MINOR; a change to a closed object, or a removed, retyped or newly-required property, mints a new `$id` and a `-vN` filename. | Either gains a second consumer. |
| `fullClip` per-clip ordering | Untouched. `reverseDrawOrder` already covers climb, and full geometry is for inspection, not the overlay. | The rookery starts consuming full geometry. |

---

## Open Questions

*(none — required to be empty before approval)*

---

## Revision Log

**2026-07-30 — Approved, then returned to In Review the same day.** Planning step 4 asked where the
`arms_up` draw-order override should live. Answering it against the Core pack falsified the premise:
`Settings.json` gives `"Arms Up"` `ReverseDrawOrder: false`, compositing in normal order renders
correctly across all four facings and every core hairstyle tried, and the proposed alternative
renders far worse because `hair` below `top` transitively forces `hair` below `head`.

Removed: `curatedClip.drawOrder`, and the AC asserting `top` after `hair`. Added: AC-26 as a pixel
regression guard, and the correction of two wrong `ReverseDrawOrder` comments. `manifest.json` `1.1.0`
now carries `sheet.hero` alone.

The cost of not catching this was one schema property and one plan step. The cost of shipping it
would have been a false ordering contract that a consumer implemented against.

---

## Notes for the reader

Three things were carried over or decided by me rather than answered directly, and are the likeliest
places for me to have got it wrong:

1. **"Per class" numbering was decided when the prefix *was* the class.** The prefix later became the
   base-body archetype, so it is recorded here as **per prefix**. That is the faithful reading of the
   preview shown at the time, but it was not re-confirmed.
2. **Every ordering field was ultimately dropped.** `sheet.drawOrder` first, because per-clip
   ordering subsumed it; then per-clip ordering itself, because the premise turned out to be false.
   A consumer stacks by `AssetSlots.DrawOrder`, unconditionally. `sheet.class` went too, because with
   composites cut no sheet belongs to a class.
3. **`loadouts/*.json` gets a schema** on the argument above. It is the one place this spec adds
   machinery the session did not explicitly ask for.

# Session prompt — hero / loadout export directories

Authored 2026-07-30 against `main` @ `6cac5f7`. Hand this to a fresh session.

---

## The task

Refactor batch export so a run writes into **nested directories** instead of one flat folder: a
fixed `curated/` for the spec-079 deliverable, and one directory per **hero**, each containing one
subdirectory per **attachment loadout**.

```
<OutputFolder>/
  heroes.csv                          <- NEW: root roll-up, hero -> body + tone
  curated/                            <- spec-079 Roost deliverable, fixed name, unchanged bytes
    body-01.webp … hair-09.webp
    index.csv  sheets.csv  manifest.json
  villager_01/                        <- one hero = one distinct body + tone
    index.csv  sheets.csv  manifest.json
    base/                             <- no optional slots filled
      villager_01.webp
    hair/                             <- hair only: every hair variant
      hair1.webp  hair7.webp  hair10.webp …
    hair+hat/                         <- hair and hat: every combination of the two
      hair1_hat1.webp  hair1_hat4.webp  hair7_hat1.webp …
    hair+hat+weapon/
      hair1_hat1_sword1.webp …
  villager_02/
    …
```

**The atlas stays whole.** Each `.webp` is still the full curated 240×1152 eight-clip sheet. This
is a directory and naming refactor — no new geometry, no per-clip splitting, and the Corvus
contract is untouched apart from the path it sits at.

### The three levels

| Level | What it is | Named by |
|---|---|---|
| hero | one distinct **body + tone** — the `AssetSlots.IsRequired` trio (`Bottom`, `Top`, `Head`) plus `Tone` | typed prefix + auto number: `villager_01`, `villager_02` |
| loadout | **which optional slots are filled** — not which partials fill them | the slot names in `AssetSlot` order joined with `+`; `base` when none are filled |
| file | the baked sheet | the optional partials' stems joined with `_` |

---

## Where things are now

Everything lands flat in `OutputFolder`. `BatchBaker.RunAsync` takes **one** `outputDirectory` for
the whole run, and `SheetWriter.Write` puts `recipe.Name + ".webp"` directly in it. Beside the
sheets, `RunArtifacts.WriteAllAsync` writes four fixed-name files into that same directory:

| File | Written by | Per-run or static? |
|---|---|---|
| `index.csv` | `Spritesheets.SheetIndex` | static — describes curated geometry, identical every run |
| `clips.csv` | `Spritesheets.ClipIndex` | static — describes full geometry; only when a `Full` recipe ran |
| `sheets.csv` | `Baking.BatchManifest` | per-run — one row per sheet, `RunId` column first |
| `manifest.json` | `Baking.RunManifest` | per-run — schema-validated via the `Schema` project |

**Pre-existing bug, in scope because this pass touches it:** a second run into the same folder
overwrites all four manifests while leaving the first run's `.webp` files on disk. Those files
become unindexed. `BatchManifest`'s `RunId` is a UUIDv7 and its docs say rows "stay attributable
when the files are concatenated" — the intent toward accumulation is already there, but nothing
appends. Decide and implement one of: overwrite (status quo, but say so), append rows, or fail
when the directory already holds a manifest for a different run.

### The files you will touch

- `Core/Baking/BatchBaker.cs` — `RunAsync` takes a single output directory
- `Core/Baking/SheetWriter.cs` — writes `directory / (name + ".webp")`
- `Core/Baking/RunArtifacts.cs` — the four-manifest orchestration
- `Core/Baking/BatchPlan.cs` — `StemFor` builds the flat name; `Expand` builds recipes
- `Core/Baking/RoostSheets.cs` — hard-names `body-01`…`hair-09`
- `Core/Baking/SheetRecipe.cs` — carries `Name`, `Layers`, `Tone`, `Geometry`; **no** directory
- `ViewModels/BatchExportViewModel.cs` — `RunAsync` (~line 305), `WriteManifestsAsync` (~line 403)
- `ViewModels/ExportPlan.cs` — turns selection + tones + mode into recipes

---

## Design notes

**Split `StemFor` along the seam the directories create.** `BatchPlan.StemFor` currently joins
*every* chosen partial's stem with `_` and appends a slugged tone (omitted for `SkinRamps.Source`
and for no tone). That one string now becomes three pieces:

- **hero identity** → the `IsRequired` partials + tone. Still built by the existing rule, but it
  names a *row in the manifest*, not a directory — see below.
- **loadout directory** → the *slot names* of the filled optional slots, from
  `AssetSlots.FolderName`, in `AssetSlot` order, joined with `+`. No partial stems here at all.
- **file name** → the optional partials' stems, joined with `_`, same rule as today's join.

Reuse `AssetSlots.FolderName` for the directory segments rather than restating slot spellings — it
is already the single source for "the lowercase member name is also the folder name", and the pack
folders prove it. Order by `AssetSlots.DrawOrder` so `hair+hat+weapon` is deterministic rather than
selection-order dependent.

**`base/` holds exactly one sheet with an empty variant name.** No optional slots filled means no
stems to join, so the file-name rule yields `""`. Give it a defined fallback — the hero label
(`villager_01.webp`) reads best and matches the sketch. Do not let it produce `.webp`.

Guard the `base` literal against colliding with a real slot name. No `AssetSlot` member is called
`base` today, so this is a one-line assertion rather than a design problem, but it is the kind of
thing a future pack breaks silently.

**The hero directory name no longer encodes what the hero *is*.** `villager_01` says nothing about
which bottom, top, head or tone produced it — unlike the old flat stem, which said everything.
That mapping now lives **only** in the manifests, which makes `heroes.csv` load-bearing rather
than a convenience. It must carry, per hero: the directory name, the three body stems, the tone
name, and the run id. Losing it makes a hero directory unidentifiable.

**Auto-numbering must be stable, or it silently corrupts consumers.** If `villager_01` means one
character this run and a different one next run, anything referencing it by path breaks with no
error anywhere. Pick and document a rule, and test it:

- number in plan order (`BatchPlan.Expand` is deterministic — the odometer walks slots in
  `AssetSlot` order and choices in selection order), and/or
- read the existing `heroes.csv` and keep already-assigned numbers stable, appending only new ones.

The second is more work and much safer for repeat runs. Decide explicitly; do not let it fall out
of implementation order by accident.

**`sheets.csv`'s `File` column must agree with what actually lands**, and it now needs the
loadout directory too — a bare `hair1_hat1.webp` is ambiguous across heroes. Use the path relative
to the hero directory (`FullPath.MakePathRelativeTo` is right there).

**`IsRequired` and `IsSkinBearing` name the same three slots today. Do not conflate them.**
`AssetSlots.cs` says so explicitly: "They are separate questions and a future pack could separate
them." The hero key is the **body**, so key on `IsRequired`.

**The empty-body hero.** `BatchPlan.Validate` permits all three required slots empty — the legal
overlay-only case (hair alone), surfaced by `ExportPlan.Explain` as "leave all three empty for an
overlay-only sheet." Those recipes have no body and no tone, so they are not a hero at all. Give
them a defined home (`overlays/` beside `curated/`, say); do not let them fall back to the output
root or become `hero_00`.

---

## Manifest placement

Earlier decision was "per-directory plus a root roll-up". With three levels, put them at the
**hero** level, not the loadout level:

- `heroes.csv` at the root — one row per hero, with the body/tone mapping above
- `index.csv` / `clips.csv` / `sheets.csv` / `manifest.json` inside each hero directory, with
  `sheets.csv` and `manifest.json` covering every loadout under that hero and gaining a column
  giving the loadout's relative path
- loadout directories hold **only** sheets

That keeps a hero directory self-contained and shippable, which was the point, without the file
count below getting worse.

---

## Why the loadout key is the slot set, not the combination

This was the alternative to a directory per exact attachment combination, and it was chosen
deliberately. The numbers are why.

Take a run selecting all 9 hair, 5 hats and 22 weapons. Every optional slot prepends `(none)` —
see `ExportPlan.Worn`'s remarks — so that is 10 × 6 × 23 = **1,380 sheets per hero**, and across
7 tones, 9,660 sheets.

| loadout key | directories per hero | total directories |
|---|---|---|
| exact combination | 1,380 | 9,660 — one per sheet |
| **filled slot set** | **8** (every subset of `{hair, hat, weapon}`) | **56** |

The sheet count is identical either way; only the shape of the tree changes. Eight directories
holding 1,380 files between them is a thing Explorer, MSIX packaging and a consumer's asset loader
can all cope with. Nine thousand directories holding one file each is not.

The ceiling is also bounded rather than open: there are 7 optional slots, so there can never be
more than 2⁷ = 128 loadout directories per hero no matter how large the selection gets.

---

## Traps

1. **`Curated` geometry is a byte-identical contract with Corvus.** Moving the Roost deliverable
   into `curated/` must change the path and nothing else. Hash the sheets before and after the
   refactor and diff — a path change that silently alters bytes is the worst outcome here.
2. **`SheetWriter.Write` does not create directories.** It returns
   `BakeFailure.OutputDirectoryUnavailable` when the folder is missing. Nested output means
   something must create the hero and loadout directories first, and directory creation is a new
   failure mode — it belongs in `BakeFailure` as a returned value, per standing rule 5, not as a
   thrown exception.
3. **`BatchBaker.RunAsync` already has 5 parameters against a hard cap of 6.** Do not add a sixth.
   Group the run's inputs into a record — `SheetRecipe`/`SlotSelection` are the precedent the
   codebase already set for exactly this.
4. **MAX_PATH gets worse, not better.** `StemFor`'s docs already flagged that ten slots plus a
   tone can approach it *flat*. The new path is longer, not shorter: the file name still carries
   up to seven stems, and the loadout directory adds up to seven slot names joined with `+`
   (`shadow+backextra+backhair+hair+frontextra+hat+weapon` is 52 characters on its own) on top of
   the hero segment. Moving the body trio and tone out of the file name buys some of that back but
   not all of it. Measure the worst case against a realistic `OutputFolder` depth before deciding
   the naming is settled.
5. **`manifest.json` is schema-validated.** Adding a loadout or hero field means changing the
   schema in `src/TheOmenDen.PixelForge.Schema`, which `RunManifest.ReadEmbeddedSchema` loads as an
   assembly resource. Generated Corvus types live under `obj/` — never edit those.
6. **Writing `index.csv` into every hero directory duplicates an identical file.** That is the
   intended cost of self-contained folders. Don't "optimize" it into a single root copy; that was
   considered and rejected.
7. **`RoostSheets` file names are hard-coded** (`body-01`…`hair-09`). The fixed `curated/`
   directory resolves the collision risk for the deliverable — but those stems are not available
   to anything else.

---

## House rules that bite here

Read `CLAUDE.md` first — all of it. The ones this task will hit:

- **Check the library before writing anything** (standing rule 0). `FullPath` has `/` for
  combination, plus `IsChildOf`, `MakePathRelativeTo`, `PathDifference` — the last two matter for
  the relative loadout paths in `sheets.csv`. `Meziantou.Framework`'s `Slug` is already used for
  tone segments and is what should slug a typed hero prefix.
- **`Result<T, TError>` / `Optional<T>`, never exceptions for expected failure** (rule 5). A
  directory that cannot be created is expected failure.
- **`AsyncFiles` for every file open** (rule 7). `new StreamWriter(path)` is not async no matter
  what the `await` looks like.
- **Sep, not a CSV object mapper.** `heroes.csv` goes through `Core/Csv.cs` (`Csv.Writer`), columns
  named at the call site. `bool` is not `ISpanFormattable` — write
  `value ? bool.TrueString : bool.FalseString`.
- **ZLinq drop-in**, `var` everywhere, Allman braces, file-scoped namespaces, expression-bodied
  members. `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors` make style a build error.
- **Max 6 parameters**, MA0051 caps methods at 60 lines, and forward/overloaded XML crefs are
  build errors (inherited members aren't resolvable as qualified crefs — `SKCanvas.Dispose()`
  fails).

---

## UI work

The batch page needs a **hero prefix** text box — `BatchExportViewModel`, bound two-way with
`UpdateSourceTrigger=PropertyChanged` or the source only commits on `LostFocus` and UI automation
`set-value` silently does nothing. It needs an `AutomationId` or `ui-tests.ps1` fails the run. Add
a `Test-UI` block covering it, and fold the prefix into `CanExport`.

---

## Open questions to settle at the start

1. Is `heroes/` a literal fixed segment, or just the `OutputFolder` the user picks? The sketch that
   started this was `heroes/hero_01/base/*`, but a typed prefix of `villager` pluralises badly.
   Simplest is no fixed segment — the user points `OutputFolder` at `…/heroes`.
2. Auto-numbering stability across repeat runs — plan order only, or read-back from `heroes.csv`?
3. Repeat runs into an existing hero directory — overwrite, append, or refuse?

---

## Verification

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet test  tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
dotnet run   --project src/TheOmenDen.PixelForge        # prints the PID
.\tests\ui-tests.ps1 -AppPid <PID>
```

Baseline before starting: build 0 warnings / 0 errors, **235/235** unit tests, UI suite 31/31.

Two known flakes — do not chase them as regressions:

- A random `SlotExpander_*` misses realization on roughly half of fresh runs, giving 3 Pipeline
  failures. Baseline with `git stash push --include-untracked` before investigating.
- The app fail-fasts on a **second** `ui-tests.ps1` run against one instance. Nothing managed
  fires. Run the suite twice before calling anything green.

After a UI run, **look at the screenshots** in `tests/ui-results/` — UIA assertions pass while the
app is visually broken.

---

## Uncommitted work in the tree

One change is staged in the working tree and not committed: `LayerComposite.cs` (new) plus
streaming rewrites of `RecipeBaker.AssembleLayers` and `SheetBaker.Assemble`. It drops peak live
bitmaps per worker from `layers + 2` to a flat 3. Build is clean and 235/235 pass with it. Commit
or set it aside before starting.

## Related, deliberately out of scope

A measured proposal exists for a third `SheetGeometry.Packed` — compacted columns plus a uniform
cell trim, cutting decoded footprint from 1,080 KiB to 168 KiB per sheet while leaving `Curated`
byte-identical. Measurements against real art:

| variant | dims | lossless webp | decoded |
|---|---|---|---|
| full source assembly | 1104×192 | 14,884 B | 828 KiB |
| curated (shipped) | 240×1152 | 6,936 B | 1,080 KiB |
| compacted columns | 768×144 | 8,142 B | 432 KiB |
| compacted + uniform trim | 512×84 | — | 168 KiB |

Disk size is already solved (~6.9 KB/sheet) and compaction makes it slightly *worse*; the win is
entirely decoded/VRAM footprint. Not part of this task — noted so it isn't rediscovered.

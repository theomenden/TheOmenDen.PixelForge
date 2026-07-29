# Harden the Manifest Contract — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Stop the manifest's version from being two hand-maintained strings that can silently disagree, and make adding a geometry or an optional field a non-breaking change — without giving up the strictness that makes an unknown key an error where an unknown key really is a bug.

**Architecture:** No new types, no new packages, no new namespaces. `schemaVersion` moves from `pattern` to `const` in the schema, which makes the generated `SchemaVersionEntity.ConstInstance` the single source of the version and upgrades `EvaluateSchema()` from checking the version's *shape* to enforcing its *value*. `additionalProperties: false` is then lifted from the objects that can plausibly grow and kept on the three whose keys are closed enumerations, with a producer-side test replacing the typo protection that lifting it costs.

**Tech Stack:** .NET 10 / C# 14, Corvus.Text.Json 5.2.10 (+ SourceGenerator, Compatibility), ZLinq, DotNext, CommunityToolkit.Diagnostics, Meziantou.Framework (FullPath, TemporaryDirectory), xUnit v3.

**Spec:** `docs/superpowers/specs/2026-07-29-json-run-manifest-design.md` — this plan closes two of its three *Not done* items. The third (publishing at the `$id` host) is deliberately left open; see Task 5.

**Scope:** `Core` and `Core.Tests` only. **No XAML, no ViewModel, no `Directory.Packages.props` change.** If a task appears to need one of those, stop and re-plan.

## Global Constraints

Every task's requirements implicitly include this section. The style, boundary, library-first and XML-doc constraints from
`docs/superpowers/plans/2026-07-29-full-library-batch-baking.md` **apply in full and are not repeated here** — read that
plan's *Global Constraints* first. What follows is only what is new or different for this plan.

**Test baseline:** the suite stands at **211 passing tests**. Nothing may regress. Every task below adds tests; none deletes one.

**Build traps specific to the Corvus generator — all four cost a red build to find:**

1. **`[JsonSchemaTypeGenerator("...")]`'s path resolves relative to the source file, not the project root.** The existing
   attribute correctly reads `"../Schemas/pixelforge-manifest-v1.json"` from `Baking/`. A project-relative path fails with
   `CRV1000: Unable to locate the root document`. Do not "tidy" the `..` away.
2. **`Core.csproj` carries `NoWarn` for `CS1572;CS1573;CS1574`** because the generator emits ~1400 mismatched `<param>` tags
   and one bad cref. **Consequence: CS1574 is suppressed in `Core`, so the "cref to a type that does not exist yet is a build
   error" trap is silent there.** After any task that touches doc comments, verify crefs by grepping the built XML:
   ```powershell
   Select-String -Path src\TheOmenDen.PixelForge.Core\bin\Debug\net10.0\TheOmenDen.PixelForge.Core.xml -Pattern 'cref="!:'
   ```
   Only `!:TryGetNumericValues` (the generator's own) is expected. Anything else is a broken cref that the build will not tell you about.
3. **Corvus ships a `CTJ001` analyzer that flows transitively** and requires `"name"u8` wherever a `ReadOnlySpan<byte>` overload
   exists — including on **System.Text.Json's** `JsonElement.GetProperty`. Production code satisfies it via the generated
   `JsonPropertyNames.*Utf8` spans. It is `none` under `tests/`, so test assertions may use plain string literals.
4. **`.editorconfig`'s `generated_code = true` does not help with any of this** — it exempts analyzer diagnostics only, never the
   compiler's own `CS`-prefixed ones on source-generated trees. Do not attempt to re-scope the `NoWarn` there; it was tried.

**Regenerating and inspecting generated code.** The generated types are virtual by default. To read them:

```powershell
dotnet build src\TheOmenDen.PixelForge.Core\TheOmenDen.PixelForge.Core.csproj -p:EmitCompilerGeneratedFiles=true
# then: src\TheOmenDen.PixelForge.Core\obj\Debug\net10.0\generated\Corvus.Text.Json.SourceGenerator\...
```

Do **not** commit `EmitCompilerGeneratedFiles` to the csproj — it is a diagnostic flag, not a build setting.

**The schema is the source of truth, in both directions.**
- Never hand-write a JSON property name in `RunManifest`. Every name comes from a generated `JsonPropertyNames` alias, which is
  why renaming a schema property is a compile error rather than a manifest a consumer silently cannot read.
- Never hand-write a value the schema constrains either. That is what Task 1 is about.

**Verification after every task:**
```powershell
dotnet build TheOmenDen.PixelForge.slnx      # must be 0 warnings, 0 errors
dotnet test  tests\TheOmenDen.PixelForge.Core.Tests\TheOmenDen.PixelForge.Core.Tests.csproj
```

---

## Task 1 — Make the schema the single source of the version

**Files:** `Core/Schemas/pixelforge-manifest-v1.json`, `Core/Baking/RunManifest.cs`

Today the version exists twice: as `RunManifest.SchemaVersion = "1.0.0"` in C#, and as a `pattern` in the schema that would
accept any semver at all. The two cannot be checked against each other, and the schema does not actually constrain the value.

### Requirements

- [x] In the schema, replace `schemaVersion`'s `"pattern": "^[0-9]+\\.[0-9]+\\.[0-9]+$"` with `"const": "1.0.0"`.
      Keep the `description`, extended per Task 5.
- [x] Delete the `public const string SchemaVersion = "1.0.0"` literal from `RunManifest`.
- [x] Replace it with a value derived from the generated const:
      `public static string SchemaVersion { get; } = (string)RunManifestDocument.SchemaVersionEntity.ConstInstance;`
- [x] Have `Compose` write the typed constant rather than the string — `WritePropertyName(RootNames.SchemaVersionUtf8)` followed
      by `RunManifestDocument.SchemaVersionEntity.ConstInstance.WriteTo(writer)` — so the bytes written are the schema's own.
- [x] `<remarks>` on `SchemaVersion` must state that the value is declared in the schema and that `EvaluateSchema()` now enforces
      it, so a writer that drifts fails validation instead of emitting a plausible-but-wrong version.

### Already verified — do not re-litigate

- A `const` subschema **does** generate a usable accessor: `public static SchemaVersionEntity ConstInstance => Constants.ConstJson;`.
- The backing `Constants` class is `private`, so `Constants.Const` (the raw `"1.0.0"u8` bytes) is **not** reachable. `ConstInstance`
  is the only public route.
- `ConstInstance` is built by `ParsedJsonDocument<T>.StringConstant(...)`, documented as *"Creates a constant string instance that
  does not require disposal… used for fast initialization for a static value."* It is **not** pooled and **not** disposable, so
  hanging a `static` off it leaks nothing. This was the gate on this task and it passes.
- An `enum` subschema (unlike `const`) generates **no** named value constants — which is why `GeometryName` maps
  `SheetGeometry` to `LayoutNames.Curated`/`.Full` rather than to a generated enum member. Leave that alone.

### Risk to handle

`(string)ConstInstance` uses an explicit operator that can `throw new FormatException()`. On a schema constant that is a
bug-class failure, but at static initialisation it surfaces as `TypeInitializationException`, which is a miserable diagnostic.
Prefer a read that cannot fault at type load, or add a test that touches `SchemaVersion` first so a fault is attributed clearly.

### Verification

- [x] `RunManifestTests.Write_StampsTheRunIdAndSchemaVersion` still passes unchanged — it already compares the written value to
      `RunManifest.SchemaVersion`, so it now transitively asserts the schema's own constant.
- [x] Add a test that a manifest whose `schemaVersion` is altered to any other value fails `EvaluateSchema()`. This is what proves
      the upgrade from shape-checking to value-enforcement actually happened, and it is the only new behaviour in this task.

---

## Task 2 — Relax `additionalProperties` at the extension points

**Files:** `Core/Schemas/pixelforge-manifest-v1.json`

`additionalProperties: false` currently sits on **every** object. That makes any additive change breaking: one new optional
property produces a document that fails validation against any older copy of the schema. Adding a third geometry — the most
likely future change — would force a v2.

### The rule

**Is an unknown key here more likely to be a newer version's data, or a typo?** Closed enumerations stay strict; growable
property bags open up.

| Keep `additionalProperties: false` | Remove it |
|---|---|
| `slots` — keys are the ten `AssetSlot` names | root object |
| `curatedClip.rows` — the three curated facings | `palette`, `ramp` |
| `fullLayout.facingRows` — the four source facings | `layouts`, `curatedLayout`, `fullLayout` |
| | `curatedClip`, `fullClip`, `sheet` |

### Requirements

- [x] Remove `"additionalProperties": false` from exactly the **nine** subschemas in the right-hand column.
      *(This plan originally said "eight". The table lists nine — root, `palette`, `ramp`, `layouts`,
      `curatedLayout`, `fullLayout`, `curatedClip`, `fullClip`, `sheet` — and the table is correct.)*
- [x] Leave it on exactly the three in the left-hand column.
- [x] Add a `description` to each of the three strict ones saying *why* it is closed — the key set is an enumeration, so an
      unrecognised key is a producer bug rather than a newer field.
- [x] Rebuild with `-p:EmitCompilerGeneratedFiles=true` and check whether relaxing changes the generated surface (an
      `AdditionalProperties` accessor, `Mutable.SetAdditionalProperty`, or similar). It should not affect `RunManifest`, which only
      ever writes known properties — but confirm rather than assume, and record what changed.

### Note on `ramp`

`ramp` is relaxed, which differs from the first sketch of this split. Applying the rule rather than intuition: a ramp could
plausibly gain `baseTone` — the schema already documents index 3 as the base tone — so it is a growable bag, not a closed
enumeration. `steps` keeps its `minItems`/`maxItems` of 5; that is a separate constraint and is unaffected.

### Verification

- [x] All 211 tests still pass. Relaxing cannot break a document that was already valid.
- [x] **Task 3 must land in the same commit as this task.** Between them there is a window where a producer typo validates clean;
      do not leave that window in `main`.

---

## Task 3 — Restore producer-side typo protection

**Files:** `Core.Tests/Baking/RunManifestTests.cs`

Task 2 gives consumers forward compatibility and costs the producer a real check: with `sheet` relaxed, a writer that emitted
`tonne` instead of `tone` would now validate clean, where today it fails. Pay for the trade rather than absorbing it.

### Requirements

- [x] Add a test asserting the **exact** set of property names emitted at each relaxed level, for a known recipe — root,
      `palette`, `sheet`, `curatedLayout`, `curatedClip`, `fullLayout`, `fullClip`, `ramp`.
- [x] Compare sets, not counts, and fail with the symmetric difference in the message so a failure names the unexpected or
      missing key rather than just a number.
- [x] Enumerate with `JsonElement.EnumerateObject()`; `CTJ001` is off under `tests/`, so plain string literals are fine.
- [x] `<remarks>` must state that this test exists *because* `additionalProperties` was relaxed for consumers, and that it is
      what keeps a producer typo from passing. Without that sentence the test looks redundant and will be deleted by someone
      tidying up.

### Verification

- [x] **Corrected during execution.** A misspelled name is *not* the risk this test guards — every name the writer emits comes
      from a generated `JsonPropertyNames` constant, so `tonne` for `tone` cannot compile. The real exposure an open object
      creates is a *correctly spelled* property at the *wrong nesting level*.
      Falsified against that instead: `frameDurationMs` was emitted onto `curatedClip` rather than its layout.
      **`EvaluateSchema()` accepted it** — every `Manifest(...)` caller stayed green — and only these assertions failed,
      with `curatedClip — unexpected: [frameDurationMs], missing: []`. Then reverted. That is the proof both that the
      relaxation removed real protection and that this test is what replaces it.

---

## Task 4 — Couple the three places the major version still lives

**Files:** `Core.Tests/Baking/RunManifestTests.cs`

After Task 1 the *semver* has one source, but the **major** is still written by hand in three places that must agree:

| Where | Value |
|---|---|
| the schema's `$id` | `https://schemas.corvusconnection.app/pixelforge-manifest-v1.json` |
| `RunManifest.SchemaFileName` | `pixelforge-manifest-v1.json` |
| `RunManifest.SchemaVersion` major | `1` |

### Requirements

- [x] One test that parses `$id` out of `RunManifest.SchemaText`, takes its last path segment, and asserts it equals
      `SchemaFileName`.
- [x] The same test asserts the `-v<n>` suffix of that filename equals the major component of `SchemaVersion`.
- [x] Derive the major by splitting `SchemaVersion` on `'.'` — do not hard-code `1`, or the test passes vacuously after a v2 bump
      and defeats its own purpose.
- [x] `<remarks>` naming this as the guard on a seam with no compiler: three strings a human maintains, one assertion.

### Verification

- [x] Temporarily bump the schema's `$id` to `-v2` and confirm the test fails; revert.

---

## Task 5 — Write the versioning policy down

**Files:** `Core/Schemas/pixelforge-manifest-v1.json`, `docs/superpowers/specs/2026-07-29-json-run-manifest-design.md`

The rules above are only useful if the next person can find them without re-deriving them from the schema.

### Requirements

- [x] In the schema's `schemaVersion` `description`, state the policy: an optional property added at a relaxed point is a **minor**
      bump; a change at a strict point, or a removed/retyped/newly-required property, mints a **new `$id`**, a new `-vN` filename
      and a new generated type.
- [x] In the schema's top-level `description`, state that consumers validate against the copy that shipped **in the same export
      folder**, never a pinned or vendored one.
- [x] Update the spec: add a *Versioning* section recording the strict/relaxed split and the rule behind it, and amend *Not done*
      so it reflects that the version coupling is now closed and only schema **publishing** remains open.
- [x] Keep the spec's *Build traps found* section current — add anything Task 2's regeneration turned up.

### Explicitly out of scope

Publishing the schema at `schemas.corvusconnection.app` is **deferred**, by decision. JSON Schema `$id` is an identifier, not
necessarily a locator; the manifest's `$schema` points at the sibling file, which is what actually validates, and offline. It
stays in the spec's *Not done*. Do not add hosting, a CI publish step, or a fetch fallback under this plan.

---

## Open Questions

None blocking. The two decisions this plan needed — the forward-compatibility strategy and whether publishing was in scope —
were taken before it was written and are recorded in Tasks 2 and 5.

## Risks

| Risk | Mitigation |
|---|---|
| Relaxing `additionalProperties` changes the generated API surface | Task 2 regenerates and inspects before moving on; `RunManifest` only writes known properties, so the blast radius should be nil |
| `(string)ConstInstance` faults at static init as `TypeInitializationException` | Task 1 calls this out; prefer a non-faulting read |
| Task 2 lands without Task 3, leaving a typo-blind window in `main` | Stated in both tasks: they ship in one commit |
| A future task "tidies" the `..` out of the generator's schema path, or re-scopes the `NoWarn` to `.editorconfig` | Both are recorded in *Global Constraints* as already-tried dead ends |
| CS1574 being suppressed hides a broken cref added by this plan | The XML-grep check in *Global Constraints* runs after every doc-comment change |

## Definition of Done

- [x] `dotnet build TheOmenDen.PixelForge.slnx` — 0 warnings, 0 errors.
- [x] `dotnet test` — 211 baseline plus the new tests, 0 failures.
- [x] `cref="!:` grep over the built XML returns only `!:TryGetNumericValues`.
- [x] ~~The string `"1.0.0"` appears **exactly once** in the repo, in the schema.~~ **Amended:** it appears **twice** —
      the schema's `const` (the declaration) and one test assertion pinning it. The second is deliberate: a test that
      derived the expected version from `SchemaVersion` would be tautological, whereas a literal makes a version bump
      fail a test and so forces a conscious acknowledgement. No C# *production* literal remains, which was the actual goal.
- [x] A real manifest is written and re-read, confirming the emitted `schemaVersion` still round-trips and the relaxed levels
      carry exactly the expected property sets.


---

## Execution record — 2026-07-29

All five tasks complete. `dotnet build TheOmenDen.PixelForge.slnx` 0/0; `dotnet test` **216 passed** (baseline 211,
plus 5: version-value enforcement, version-source, the two exact-property tests, and the `$id` coupling test).
Unresolved-cref grep returns only `!:TryGetNumericValues`, as expected.

**Two places this plan was wrong, corrected above rather than quietly worked around:**

1. It said eight subschemas would be relaxed; its own table listed nine. Nine were relaxed.
2. It justified Task 3 with a misspelling that the generated property names make impossible. The falsification was
   redone against the mistake that *can* actually happen — right name, wrong nesting level — and the test's
   `<remarks>` now documents that failure mode instead of the imaginary one.

**Both falsification steps were carried out and both passed**, so neither new guard is decorative:

| Guard | Falsified by | Result |
|---|---|---|
| `EvaluateSchema_RejectsAManifestCarryingTheWrongVersion` | rewriting `schemaVersion` to `9.9.9` | rejected |
| `Write_EmitsExactlyTheExpectedProperties_*` | stray `frameDurationMs` on a clip | caught; schema did not |
| `SchemaId_AgreesWithTheFileNameAndTheMajorVersion` | bumping `$id` to `-v2` | caught |

**Confirmed non-finding:** relaxing `additionalProperties` changed the generated code not at all — same 194 files,
no `AdditionalProperties` accessors. Task 2's inspection requirement is discharged.

A real two-sheet manifest was written to disk and re-read: `schemaVersion` `1.0.0` round-tripped, `$schema` resolved
to the sibling copy whose `$id` and `const` both agreed, the three closed objects were still closed and the nine open
ones open, and `walk` still carried `[1, 2, 1, 0]`.

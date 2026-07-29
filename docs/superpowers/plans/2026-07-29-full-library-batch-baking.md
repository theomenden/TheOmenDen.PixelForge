# Full-Library Batch Baking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hard-coded sixteen-recipe table with a scanned catalogue of all 995 Time Elements partials, and bake the cross product of a per-slot selection across skin tones.

**Architecture:** A new `Core/Catalog` namespace scans the three pack directories into `AssetPartial` records keyed by `(slot, base, variant)`. The skin substitution moves from the flattened assembly to individual layers, driven by `AssetSlot.IsSkinBearing`, which deletes the `Overlays` workaround. `SheetBaker.Recolor` becomes a `System.Numerics.Vector<uint>` compare-and-select over whole 32-bit pixels. A cross-product planner turns per-slot selections into recipes, and a second output geometry emits the raw 23x4 assembly alongside the curated Corvus sheet.

**Tech Stack:** .NET 10 / C# 14, SkiaSharp 4.151, ZLinq (+ ZLinq.FileSystem), DotNext, CommunityToolkit (Mvvm, WinUI Controls, Collections, HighPerformance, Diagnostics), Meziantou.Framework (FullPath, ByteSize, TemporaryDirectory, Slug), CsvHelper, xUnit v3, WinUI 3 / Windows App SDK 2.3.1.

**Spec:** `docs/superpowers/specs/2026-07-29-full-library-batch-baking-design.md`

## Global Constraints

Every task's requirements implicitly include this section.

**Projects and boundary**
- `Core` targets `net10.0` and MUST NOT reference `Microsoft.UI.*` or any Windows type. All logic in this plan except the ViewModel/View tasks belongs in `Core`.
- App targets `net10.0-windows10.0.26100.0`. Never `AnyCPU`; never add `<WindowsPackageType>None`; never delete `Package.appxmanifest`.
- Central Package Management: package versions live **only** in `Directory.Packages.props`. A `Version=` attribute on a `PackageReference` is a restore error.

**Style — enforced at build, not advisory**
- `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on. A style violation fails the build.
- **`var` everywhere**, including built-in types (`var i = 0`, never `int i = 0`). `.editorconfig` sets all three `csharp_style_var_*` rules to `true:warning`, so an explicit type is build error IDE0007. **This overrides the `modern-csharp` skill's "use explicit type when not obvious" guidance** — the project rule wins.
- **Allman braces** everywhere, on every construct. Braces always, even for one-line bodies. `else`/`catch`/`finally` on new lines. 4-space indent.
- File-scoped namespaces. Private fields `_camelCase`.
- Expression-bodied members wherever the body is a single expression.
- Collection expressions `[]`, `required`/`init`, `readonly record struct` for small value types, switch expressions over switch statements, pattern matching over type/null checks.
- No deeply nested property patterns — sequential checks read better.

**XML documentation — required on every public type and member**
- `GenerateDocumentationFile` is on but `CS1591` is suppressed, so the compiler will **not** catch a missing doc. Enforce it by review.
- Match the density of the existing codebase: `<summary>` states *what*, and a `<para>` or `<remarks>` states *why* — the constraint, the trap, or the rejected alternative. See `SheetBaker`, `BatchBaker` and `BakeFailure` for the house voice.
- Non-obvious constants, magic column numbers and every deliberate deviation from the generator get a sentence explaining the evidence behind them.
- **Use `<see/>` heavily.** `<see cref="..."/>` for every reference to a type, member or parameter, so a rename breaks the build path rather than rotting the prose silently. `<see langword="..."/>` for **every** language keyword in documentation — `null`, `true`, `false`, `default`, `static`, `readonly`, `ref`, `in`, `sealed`, `await`. Never write a bare `true` or `null` in a doc comment; both render as plain prose and neither gets keyword styling in IntelliSense.
- `<paramref name="..."/>` for parameters and `<typeparamref name="..."/>` for type parameters — never a bare name in backticks.
- `<inheritdoc />` on interface implementations and overrides rather than a copied summary that will drift.
- `<returns>` on any member whose return value is not obvious from the summary, especially the `Result<T, TError>` and `Optional<T>` returns, which must say what each failure means.

**Library-first (standing rule 0)**
- Before writing anything non-trivial, enumerate the relevant package's public surface and state what was found — including when it genuinely lacks the method.
- ZLinq replaces System.Linq. `ImmutableArray<T>` is **not** covered by the drop-in generator — call `.AsSpan()` first or the chain silently binds to System.Linq.
- `ZLinq.FileSystem` (`DirectoryInfo.Children()`) for directory walks, not `Directory.EnumerateFiles` + LINQ.
- Banned at build time (RS0030): `SemaphoreSlim`, `Semaphore`, `ReaderWriterLockSlim`, `Monitor`, `Mutex`, `ManualResetEventSlim`, `CountdownEvent`, `Barrier`, **`System.Lazy<T>`**, and the reflection-based `JsonSerializer` overloads. Each error names its replacement.

**Failure handling**
- Expected failure is a return value: `DotNext.Result<T, TError>` and `Optional<T>`, never exceptions. Exceptions are for bugs.
- Failure enums are numbered **from 1** so `default` is never a real failure.
- `Guard.IsNotNull` / `Guard.IsGreaterThan` at public boundaries — a null there is a caller bug, not an expected outcome.

**Resources**
- Inherit `DotNext.Disposable` rather than hand-rolling a `_disposed` flag; call `base.Dispose(disposing)` (CA2215).
- `PooledStreams.Manager` is the single `RecyclableMemoryStreamManager`. `ToArray()` throws by design — use `WriteTo`, `GetBuffer().AsSpan(0, (int)Length)` or `GetReadOnlySequence()`.
- Never `SKBitmap.Pixels` in production paths (allocates an `SKColor[]` per call, 828 KiB per partial, straight to the LOH). Use `PeekPixels()` / `GetPixelSpan()`. Tests may use `.Pixels` for clarity.

**Identifiers**
- GUIDs are **UUIDv7** via `Guid.CreateVersion7()`, never `Guid.NewGuid()`.

**WinUI (tasks 10-13 only)**
- Every interactive control needs an `AutomationId` — `ui-tests.ps1` fails the run without it.
- `x:Bind` over `Binding`; add `Mode=OneWay` explicitly for anything that updates (`x:Bind` defaults to `OneTime`).
- `TextBox`/`AutoSuggestBox` two-way bindings need `UpdateSourceTrigger=PropertyChanged`, or UIA `set-value` silently does nothing.
- `{ThemeResource ...}`, never hardcoded colours. Verify Light, Dark and HighContrast.
- Typography styles (`BodyStrongTextBlockStyle`, `CaptionTextBlockStyle`), never raw `FontSize`. 4px spacing grid.
- `[ObservableProperty]` / `[RelayCommand]`; never hand-write `INotifyPropertyChanged` or `ICommand`.

**Verification after every task**

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
```

Both must be green before the commit. Do not proceed to the next task on a red build.

---

## File Structure

**New — `src/TheOmenDen.PixelForge.Core/Catalog/`**

| File | Responsibility |
|---|---|
| `AssetSlot.cs` | The ten generator layers as an enum whose value *is* the draw order. |
| `AssetSlots.cs` | Slot metadata: folder name, `IsSkinBearing`, `IsRequired`, draw-order sequence. |
| `AssetName.cs` | Splits `hair15_c3` into base + variant; derives the ordering key. |
| `AssetSortKey.cs` | `(Prefix, Number, Suffix, Variant)` comparable ordering key. |
| `AssetPartial.cs` | One partial file: slot, pack, base, variant, path. |
| `AssetCatalog.cs` | Scans the three packs; exposes partials per slot in order. |
| `CatalogFailure.cs` | Why a scan produced nothing. |

**New — `src/TheOmenDen.PixelForge.Core/Baking/`**

| File | Responsibility |
|---|---|
| `AssetLayer.cs` | A path plus whether it carries skin. |
| `SheetGeometry.cs` | Curated vs Full output geometry. |
| `SlotSelection.cs` | One slot's chosen partials, `(none)` included. |
| `BatchPlan.cs` | Cross product of selections x tones into recipes. |
| `PlanFailure.cs` | Why a selection cannot be expanded. |
| `BatchManifest.cs` | `sheets.csv` — output file to slot composition, with the run id. |
| `BatchManifestRow.cs` | One `sheets.csv` record. |

**New — `src/TheOmenDen.PixelForge.Core/Spritesheets/`**

| File | Responsibility |
|---|---|
| `GeneratorClip.cs` | One animation with its real playback frame order. |
| `GeneratorClips.cs` | All twelve animations, verbatim from `Settings.json`. |
| `ClipIndex.cs` | `clips.csv` for full-geometry output. |
| `ClipIndexRow.cs` | One `clips.csv` record. |

**New — `src/TheOmenDen.PixelForge.Core/Palettes/`**

| File | Responsibility |
|---|---|
| `RampSubstitution.cs` | Two parallel `uint` arrays replacing the per-pixel dictionary. |

**Modified**

| File | Change |
|---|---|
| `Palettes/SkinRamp.cs` | Add `PackedRgba`; `SubstitutionFrom` returns `RampSubstitution`. |
| `Baking/SheetBaker.cs` | SIMD `Recolor`; correct the inaccurate "no library can do this" remark. |
| `Baking/SheetRecipe.cs` | `Layers` becomes `ImmutableArray<AssetLayer>`; `Recolor` renamed `Tone`; `Overlays` deleted; `Geometry` added. |
| `Baking/RecipeBaker.cs` | Per-layer recolour; `ApplyOverlays` deleted; honours `Geometry`. |
| `Baking/RoostSheets.cs` | Rebuilt on `AssetLayer`; `Flattened` deleted; `Selection` added. |
| `Spritesheets/AnimationClip.cs` | **Unchanged** — the curated path must not drift. Full geometry gets its own `GeneratorClip` instead. |
| `src/TheOmenDen.PixelForge/Services/` | Add `CatalogService.cs`. |
| `src/TheOmenDen.PixelForge/ViewModels/` | Rewrite `BatchExportViewModel`; repurpose `ExportMode`; add `SlotGroupViewModel`, `PartialSelectionItem`. |
| `src/TheOmenDen.PixelForge/Views/PipelinePage.xaml(.cs)` | Per-slot picker. |
| `Directory.Packages.props` | Add `Meziantou.Framework.Slug`. |
| `tests/ui-tests.ps1` | Slot picker, variants toggle, geometry mode, preset. |

**Deleted**

| File | Reason |
|---|---|
| `tests/.../Baking/RecipeBakerOverlayTests.cs` | `Overlays` no longer exists. |

---

## Task 1: Slot model and asset name parsing

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Catalog/AssetSlot.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Catalog/AssetSlots.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Catalog/AssetSortKey.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Catalog/AssetName.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Catalog/AssetNameTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AssetSlot` (enum, values 0-9); `AssetSlots.FolderName(AssetSlot) -> string`, `AssetSlots.IsSkinBearing(AssetSlot) -> bool`, `AssetSlots.IsRequired(AssetSlot) -> bool`, `AssetSlots.DrawOrder -> ImmutableArray<AssetSlot>`; `AssetSortKey(string Prefix, int Number, string Suffix, int Variant)` implementing `IComparable<AssetSortKey>`; `AssetName.Split(string stem) -> (string Base, int Variant)`, `AssetName.SortKey(string @base, int variant) -> AssetSortKey`.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Catalog/AssetNameTests.cs`:

```csharp
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Tests.Catalog;

/// <summary>
/// Pins the file-name grammar the packs actually use. The awkward cases are real files:
/// <c>bow1arrow1</c>, <c>shield1L</c>, <c>daggerL</c>, <c>daggers</c>, <c>crown1</c>.
/// </summary>
public sealed class AssetNameTests
{
    [Theory]
    [InlineData("hair15", "hair15", 0)]
    [InlineData("hair15_c3", "hair15", 3)]
    [InlineData("top0", "top0", 0)]
    [InlineData("bow1arrow1", "bow1arrow1", 0)]
    [InlineData("shield1L", "shield1L", 0)]
    [InlineData("daggerL", "daggerL", 0)]
    [InlineData("daggers", "daggers", 0)]
    [InlineData("crown1", "crown1", 0)]
    [InlineData("sword1_c2", "sword1", 2)]
    public void Split_SeparatesBaseFromColourVariant(string stem, string expectedBase, int expectedVariant)
    {
        var (actualBase, actualVariant) = AssetName.Split(stem);

        Assert.Equal(expectedBase, actualBase);
        Assert.Equal(expectedVariant, actualVariant);
    }

    /// <summary>A trailing <c>_c</c> that is not followed by digits is part of the name.</summary>
    [Theory]
    [InlineData("weird_cape")]
    [InlineData("thing_c")]
    public void Split_TreatsANonNumericSuffixAsPartOfTheBase(string stem)
    {
        var (actualBase, actualVariant) = AssetName.Split(stem);

        Assert.Equal(stem, actualBase);
        Assert.Equal(0, actualVariant);
    }

    [Fact]
    public void SortKey_OrdersNumericallyNotLexically()
    {
        var two = AssetName.SortKey("hair2", 0);
        var ten = AssetName.SortKey("hair10", 0);

        Assert.True(two.CompareTo(ten) < 0, "hair2 must sort before hair10");
    }

    [Fact]
    public void SortKey_SplitsPrefixNumberAndSuffix()
    {
        var key = AssetName.SortKey("shield1L", 0);

        Assert.Equal("shield", key.Prefix);
        Assert.Equal(1, key.Number);
        Assert.Equal("L", key.Suffix);
    }

    /// <summary>A name with no digits at all still has to order deterministically.</summary>
    [Fact]
    public void SortKey_UsesMinusOne_WhenTheNameCarriesNoNumber()
    {
        var key = AssetName.SortKey("daggers", 0);

        Assert.Equal("daggers", key.Prefix);
        Assert.Equal(-1, key.Number);
        Assert.Equal(string.Empty, key.Suffix);
    }

    [Fact]
    public void SortKey_OrdersVariantsAfterTheirBase()
    {
        var bare = AssetName.SortKey("hair1", 0);
        var variant = AssetName.SortKey("hair1", 3);

        Assert.True(bare.CompareTo(variant) < 0);
    }

    /// <summary>Folder names are the slot's own lowercase name in every pack.</summary>
    [Theory]
    [InlineData(AssetSlot.BackExtra, "backextra")]
    [InlineData(AssetSlot.BackHair, "backhair")]
    [InlineData(AssetSlot.FrontExtra, "frontextra")]
    [InlineData(AssetSlot.Weapon, "weapon")]
    public void FolderName_IsTheLowercasedSlotName(AssetSlot slot, string expected)
        => Assert.Equal(expected, AssetSlots.FolderName(slot));

    /// <summary>
    /// The evidence for this set is in the spec: 23 of 28 tops carry bare arms and hands,
    /// while weapons carry ramp hexes as wood and shield trim, not skin.
    /// </summary>
    [Fact]
    public void IsSkinBearing_IsTrueForExactlyBottomTopAndHead()
    {
        AssetSlot[] expected = [AssetSlot.Bottom, AssetSlot.Top, AssetSlot.Head];

        foreach (var slot in AssetSlots.DrawOrder)
        {
            Assert.Equal(expected.Contains(slot), AssetSlots.IsSkinBearing(slot));
        }
    }

    [Fact]
    public void DrawOrder_IsTheGeneratorsCharacterLayersOrder()
    {
        AssetSlot[] expected =
        [
            AssetSlot.Shadow, AssetSlot.BackExtra, AssetSlot.BackHair, AssetSlot.Bottom,
            AssetSlot.Top, AssetSlot.Head, AssetSlot.Hair, AssetSlot.FrontExtra,
            AssetSlot.Hat, AssetSlot.Weapon,
        ];

        Assert.Equal(expected, AssetSlots.DrawOrder);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~AssetNameTests"`

Expected: build failure — `AssetSlot`, `AssetSlots` and `AssetName` do not exist.

- [ ] **Step 3: Create `AssetSlot.cs`**

```csharp
namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// The ten character layers the Elements generator composites, and the only slots a partial can
/// belong to.
/// <para>
/// Each member's value <em>is</em> its draw order, taken verbatim from the generator's
/// <c>Settings.json</c> <c>CharacterLayers</c> block. Compositing is therefore an ordinary sort
/// by slot rather than a second table that can drift out of step with this enum.
/// </para>
/// <para>
/// The lowercase member name is also the folder name in all three packs, so no slot-to-folder
/// map is needed either — see <see cref="AssetSlots.FolderName"/>.
/// </para>
/// </summary>
public enum AssetSlot
{
    /// <summary>Drawn first, beneath everything.</summary>
    Shadow = 0,

    /// <summary>Backpacks and tails, behind the body.</summary>
    BackExtra = 1,

    /// <summary>Long hair falling behind the body.</summary>
    BackHair = 2,

    /// <summary>Legs and lower garment. Carries bare skin on <c>bottom0</c>.</summary>
    Bottom = 3,

    /// <summary>Torso, arms and hands. Carries bare skin on 23 of its 28 bases.</summary>
    Top = 4,

    /// <summary>The face. Always skin.</summary>
    Head = 5,

    /// <summary>Hair in front of the head.</summary>
    Hair = 6,

    /// <summary>Held items and effects drawn in front of the body.</summary>
    FrontExtra = 7,

    /// <summary>Headwear, drawn over hair.</summary>
    Hat = 8,

    /// <summary>Weapons and shields, drawn last.</summary>
    Weapon = 9,
}
```

- [ ] **Step 4: Create `AssetSlots.cs`**

```csharp
using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>Slot metadata: where a slot's files live, and how the baker must treat them.</summary>
public static class AssetSlots
{
    /// <summary>
    /// Every slot in generator draw order. Derived from the enum rather than restated, so a new
    /// member cannot be forgotten here.
    /// </summary>
    public static ImmutableArray<AssetSlot> DrawOrder { get; } =
        [.. Enum.GetValues<AssetSlot>().AsSpan().OrderBy(static slot => (int)slot)];

    /// <summary>
    /// The directory a slot's partials live in, under a pack's <c>assets</c> folder.
    /// <para>
    /// This is the member name lowercased, which matches every folder in all three packs
    /// (<c>backextra</c>, <c>backhair</c>, <c>frontextra</c> and the rest). Verified against the
    /// packs; a mismatch would surface immediately as an empty slot in the catalogue.
    /// </para>
    /// </summary>
    public static string FolderName(AssetSlot slot) => slot.ToString().ToLowerInvariant();

    /// <summary>
    /// Whether a substitution into the chosen skin tone must be applied to this slot's layers.
    /// <para>
    /// <see langword="true"/> for exactly <see cref="AssetSlot.Bottom"/>, <see cref="AssetSlot.Top"/>
    /// and <see cref="AssetSlot.Head"/>. The evidence is a scan of every base partial for pixels in
    /// the five source-ramp hexes: 23 of 28 tops carry bare arms and hands, all 20 heads are
    /// faces, and three bottoms expose legs.
    /// </para>
    /// <para>
    /// <see cref="AssetSlot.Weapon"/> is deliberately excluded even though 13 of its 22 bases do
    /// carry ramp pixels. Hands are not on the weapon layer at all — <c>arrow1</c> is 10.7% ramp
    /// with no hand on it, while <c>sword1</c>, <c>gun1</c> and <c>wand1</c> are 0%. Those hexes
    /// are wooden shafts, bow limbs and shield trim, so recolouring them would turn a Bone-toned
    /// character's wooden bow white. This diverges from the generator, which swaps globally.
    /// </para>
    /// <para>
    /// <see cref="AssetSlot.Hair"/> and <see cref="AssetSlot.Hat"/> keep their authored colour;
    /// <c>hair1</c> (2.7%) and <c>hat4</c> (9.7%) use ramp hexes as highlights and trim.
    /// </para>
    /// </summary>
    public static bool IsSkinBearing(AssetSlot slot) =>
        slot is AssetSlot.Bottom or AssetSlot.Top or AssetSlot.Head;

    /// <summary>
    /// Whether a character must have this slot filled. The generator marks every other layer
    /// <c>IsOptional</c>, so a hatless, weaponless character is legal but a headless one is not.
    /// <para>
    /// This currently names the same three slots as <see cref="IsSkinBearing"/>. That is not a
    /// coincidence worth collapsing: the required layers are the body, and the body is where the
    /// skin is. They are separate questions and a future pack could separate them.
    /// </para>
    /// </summary>
    public static bool IsRequired(AssetSlot slot) =>
        slot is AssetSlot.Bottom or AssetSlot.Top or AssetSlot.Head;
}
```

- [ ] **Step 5: Create `AssetSortKey.cs`**

```csharp
namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// How one partial orders against another within its slot.
/// <para>
/// Pack file names are numbered, so plain string ordering is wrong everywhere — it puts
/// <c>hair10</c> before <c>hair2</c>. Rather than reach for a natural-sort comparer (none of the
/// referenced Meziantou packages ships one, the BCL has no portable one, and
/// <c>StrCmpLogicalW</c> is Win32 which <c>Core</c> may not use), the catalogue already
/// decomposes each name while parsing it. Ordering by the parts is correct by construction and
/// costs nothing extra.
/// </para>
/// <para>
/// The <see cref="Suffix"/> is what keeps <c>shield1L</c> and <c>shield1R</c> adjacent and
/// deterministic, and <see cref="Number"/> is -1 for names carrying no digits at all
/// (<c>daggers</c>), sorting them ahead of numbered siblings sharing a prefix.
/// </para>
/// </summary>
public readonly record struct AssetSortKey(string Prefix, int Number, string Suffix, int Variant)
    : IComparable<AssetSortKey>
{
    /// <inheritdoc />
    public int CompareTo(AssetSortKey other)
    {
        var byPrefix = string.CompareOrdinal(Prefix, other.Prefix);

        if (byPrefix is not 0)
        {
            return byPrefix;
        }

        if (Number != other.Number)
        {
            return Number.CompareTo(other.Number);
        }

        var bySuffix = string.CompareOrdinal(Suffix, other.Suffix);

        if (bySuffix is not 0)
        {
            return bySuffix;
        }

        return Variant.CompareTo(other.Variant);
    }
}
```

- [ ] **Step 6: Create `AssetName.cs`**

```csharp
using System.Globalization;
using CommunityToolkit.Diagnostics;

namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// The file-name grammar the Time Elements packs use, and the ordering key that falls out of it.
/// <para>
/// A partial is <c>&lt;base&gt;.png</c> or <c>&lt;base&gt;_c&lt;n&gt;.png</c>. The <c>_cN</c>
/// files are colour variants, verified by pixel diff: <c>top1_c1..c4</c> change garment pixels
/// and leave every skin pixel and the silhouette untouched. On heads they are eye colours.
/// </para>
/// <para>
/// Parsing is a span split rather than a regex, because the base name is not a simple
/// letters-then-digits shape — <c>bow1arrow1</c>, <c>shield1L</c>, <c>daggerL</c> and
/// <c>daggers</c> are all real files. Anything that is not a trailing <c>_c</c> followed only by
/// digits belongs to the base.
/// </para>
/// </summary>
public static class AssetName
{
    private const string VariantMarker = "_c";

    /// <summary>
    /// Splits a file stem (no extension) into its base name and colour variant.
    /// Variant <c>0</c> means the un-suffixed base file.
    /// </summary>
    public static (string Base, int Variant) Split(string stem)
    {
        Guard.IsNotNullOrWhiteSpace(stem);

        var marker = stem.LastIndexOf(VariantMarker, StringComparison.Ordinal);

        if (marker < 0)
        {
            return (stem, 0);
        }

        var digits = stem.AsSpan(marker + VariantMarker.Length);

        // NumberStyles.None rejects signs and whitespace, so "_c+3" stays part of the base.
        if (digits.IsEmpty
            || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var variant))
        {
            return (stem, 0);
        }

        return (stem[..marker], variant);
    }

    /// <summary>
    /// Decomposes a base name into the ordering key described by <see cref="AssetSortKey"/>:
    /// leading letters, the first run of digits, then whatever remains.
    /// </summary>
    public static AssetSortKey SortKey(string @base, int variant)
    {
        Guard.IsNotNullOrWhiteSpace(@base);

        var span = @base.AsSpan();
        var letters = 0;

        while (letters < span.Length && !char.IsAsciiDigit(span[letters]))
        {
            letters++;
        }

        var digits = letters;

        while (digits < span.Length && char.IsAsciiDigit(span[digits]))
        {
            digits++;
        }

        var number = digits > letters
            ? int.Parse(span[letters..digits], NumberStyles.None, CultureInfo.InvariantCulture)
            : -1;

        return new(span[..letters].ToString(), number, span[digits..].ToString(), variant);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~AssetNameTests"`

Expected: PASS, all cases.

If `DrawOrder` fails to compile because `Enum.GetValues<T>()` returns an array that the ZLinq drop-in does not cover, note that arrays **are** covered — no `.AsValueEnumerable()` is needed, but `.AsSpan()` is harmless and explicit.

- [ ] **Step 8: Full build and test**

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
```

Expected: green, 45 existing tests plus the new ones.

- [ ] **Step 9: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Catalog tests/TheOmenDen.PixelForge.Core.Tests/Catalog
git commit -m "feat(core): add the asset slot model and pack file-name grammar"
```

---

## Task 2: Scan the packs into a catalogue

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Catalog/AssetPartial.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Catalog/CatalogFailure.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Catalog/AssetCatalog.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Catalog/AssetCatalogTests.cs`

**Interfaces:**
- Consumes: `AssetSlot`, `AssetSlots.FolderName`, `AssetSlots.DrawOrder`, `AssetName.Split`, `AssetName.SortKey`, `AssetSortKey` (Task 1); `SourcePacks`, `ElementsPack` (existing, `Core/Baking`).
- Produces: `AssetPartial` (`readonly record struct` with `Slot`, `Pack`, `Base`, `Variant`, `Path`, `Stem`, `FileName`, `SortKey`); `CatalogFailure` enum; `AssetCatalog.Scan(SourcePacks) -> Result<AssetCatalog, CatalogFailure>`, `AssetCatalog.Partials(AssetSlot) -> ImmutableArray<AssetPartial>`, `AssetCatalog.Bases(AssetSlot) -> ImmutableArray<AssetPartial>`, `AssetCatalog.Find(AssetSlot, string, int) -> Optional<AssetPartial>`, `AssetCatalog.Count -> int`.

> `Bases` returns the variant-`0` partials only — that is what the picker lists. `Partials` returns everything including `_cN`, which is what the "include colour variants" toggle expands into.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Catalog/AssetCatalogTests.cs`:

```csharp
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Tests.Catalog;

/// <summary>
/// Scans a synthetic pack tree rather than the real packs: the licence keeps the art outside
/// every repo, so a test that needed it could not run on a clean checkout. The tree below
/// reproduces the shapes that actually matter — numbered bases out of lexical order, colour
/// variants, a slot present in one pack and absent from another, and the non-numeric weapon
/// names.
/// </summary>
public sealed class AssetCatalogTests
{
    /// <summary>Writes a zero-byte file per name; the scan reads directory entries only.</summary>
    private static void WriteSlot(FullPath assets, AssetSlot slot, params ReadOnlySpan<string> stems)
    {
        var directory = assets / AssetSlots.FolderName(slot);

        Directory.CreateDirectory(directory.Value);

        foreach (var stem in stems)
        {
            File.WriteAllBytes((directory / (stem + ".png")).Value, []);
        }
    }

    private static SourcePacks BuildPacks(TemporaryDirectory root)
    {
        var core = FullPath.FromPath(Path.Combine(root.FullPath.Value, "core"));
        var one = FullPath.FromPath(Path.Combine(root.FullPath.Value, "exp1"));
        var two = FullPath.FromPath(Path.Combine(root.FullPath.Value, "exp2"));

        WriteSlot(core, AssetSlot.Hair, "hair1", "hair1_c1", "hair2", "hair10");
        WriteSlot(core, AssetSlot.Top, "top0", "top11", "top11_c5");
        WriteSlot(core, AssetSlot.Bottom, "bottom1");
        WriteSlot(core, AssetSlot.Head, "head1", "head1_c2");
        WriteSlot(core, AssetSlot.Weapon, "sword1", "bow1arrow1", "shield1L", "daggers");

        WriteSlot(one, AssetSlot.Hair, "hair13");
        WriteSlot(one, AssetSlot.Top, "top13");

        // Expansion 2 has no weapon folder here — a missing slot must not fail the scan.
        WriteSlot(two, AssetSlot.Hair, "hair22");

        return new()
        {
            CoreAssets = core,
            Expansion1Assets = one,
            Expansion2Assets = two,
        };
    }

    private static AssetCatalog ScanOrFail(SourcePacks packs)
    {
        var result = AssetCatalog.Scan(packs);

        Assert.True(result.IsSuccessful, $"scan failed with {result.Error}");

        return result.Value;
    }

    [Fact]
    public void Scan_FindsPartialsAcrossAllThreePacks()
    {
        using var root = TemporaryDirectory.Create();

        var catalog = ScanOrFail(BuildPacks(root));
        var hair = catalog.Partials(AssetSlot.Hair);

        Assert.Equal(6, hair.Length);
        Assert.Contains(hair, p => p.Base == "hair13" && p.Pack == ElementsPack.CharacterExpansion1);
        Assert.Contains(hair, p => p.Base == "hair22" && p.Pack == ElementsPack.CharacterExpansion2);
    }

    [Fact]
    public void Scan_OrdersBasesNumerically()
    {
        using var root = TemporaryDirectory.Create();

        var catalog = ScanOrFail(BuildPacks(root));
        var bases = catalog.Bases(AssetSlot.Hair);

        Assert.Equal(["hair1", "hair2", "hair10", "hair13", "hair22"], bases.Select(static p => p.Base).ToArray());
    }

    [Fact]
    public void Bases_ExcludesColourVariants()
    {
        using var root = TemporaryDirectory.Create();

        var catalog = ScanOrFail(BuildPacks(root));

        Assert.All(catalog.Bases(AssetSlot.Hair), p => Assert.Equal(0, p.Variant));
        Assert.Equal(2, catalog.Bases(AssetSlot.Top).Length);
        Assert.Equal(3, catalog.Partials(AssetSlot.Top).Length);
    }

    [Fact]
    public void Scan_PlacesAVariantImmediatelyAfterItsBase()
    {
        using var root = TemporaryDirectory.Create();

        var catalog = ScanOrFail(BuildPacks(root));
        var hair = catalog.Partials(AssetSlot.Hair);

        Assert.Equal("hair1", hair[0].Base);
        Assert.Equal(0, hair[0].Variant);
        Assert.Equal("hair1", hair[1].Base);
        Assert.Equal(1, hair[1].Variant);
    }

    [Fact]
    public void Scan_TolerantOfASlotFolderThatDoesNotExist()
    {
        using var root = TemporaryDirectory.Create();

        var catalog = ScanOrFail(BuildPacks(root));

        Assert.Empty(catalog.Partials(AssetSlot.Hat));
        Assert.Equal(4, catalog.Partials(AssetSlot.Weapon).Length);
    }

    [Fact]
    public void Scan_KeepsNonNumericWeaponNamesIntact()
    {
        using var root = TemporaryDirectory.Create();

        var catalog = ScanOrFail(BuildPacks(root));
        var names = catalog.Partials(AssetSlot.Weapon).Select(static p => p.Base).ToArray();

        Assert.Contains("bow1arrow1", names);
        Assert.Contains("shield1L", names);
        Assert.Contains("daggers", names);
    }

    [Fact]
    public void Find_LocatesAPartialByItsIdentity()
    {
        using var root = TemporaryDirectory.Create();

        var catalog = ScanOrFail(BuildPacks(root));

        Assert.True(catalog.Find(AssetSlot.Top, "top11", 5).TryGet(out var found));
        Assert.Equal("top11_c5.png", found.FileName);

        Assert.False(catalog.Find(AssetSlot.Top, "top11", 9).HasValue);
    }

    [Fact]
    public void Scan_ReportsPackDirectoryMissing_WhenARootIsNotThere()
    {
        using var root = TemporaryDirectory.Create();

        var packs = BuildPacks(root) with
        {
            Expansion2Assets = FullPath.FromPath(Path.Combine(root.FullPath.Value, "nope")),
        };

        var result = AssetCatalog.Scan(packs);

        Assert.False(result.IsSuccessful);
        Assert.Equal(CatalogFailure.PackDirectoryMissing, result.Error);
    }

    [Fact]
    public void Scan_ReportsNoPartialsFound_WhenEveryPackIsEmpty()
    {
        using var root = TemporaryDirectory.Create();

        var empty = FullPath.FromPath(Path.Combine(root.FullPath.Value, "bare"));

        Directory.CreateDirectory(empty.Value);

        var result = AssetCatalog.Scan(new()
        {
            CoreAssets = empty,
            Expansion1Assets = empty,
            Expansion2Assets = empty,
        });

        Assert.False(result.IsSuccessful);
        Assert.Equal(CatalogFailure.NoPartialsFound, result.Error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~AssetCatalogTests"`

Expected: build failure — `AssetPartial`, `CatalogFailure` and `AssetCatalog` do not exist.

If `TemporaryDirectory` is not resolvable from the test project, add `<PackageReference Include="Meziantou.Framework.TemporaryDirectory" />` to `tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`. The version is already in `Directory.Packages.props`; do **not** add a `Version=` attribute.

- [ ] **Step 3: Create `CatalogFailure.cs`**

```csharp
namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// Why a catalogue scan produced nothing usable.
/// <para>
/// Both members describe someone's disk rather than a bug — the packs live outside every repo
/// and the user points the app at them — so they travel as
/// <see cref="DotNext.Result{T, TError}"/> values instead of exceptions.
/// </para>
/// <para>
/// Numbering starts at 1 so <see langword="default"/> is never mistaken for a real failure.
/// </para>
/// </summary>
public enum CatalogFailure
{
    /// <summary>One of the three configured pack directories is not on disk.</summary>
    PackDirectoryMissing = 1,

    /// <summary>
    /// Every directory exists but holds no <c>.png</c> in any known slot folder — usually a path
    /// pointing at a pack's root rather than at its <c>assets</c> subdirectory.
    /// </summary>
    NoPartialsFound,
}
```

- [ ] **Step 4: Create `AssetPartial.cs`**

```csharp
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// One partial file in one pack — the smallest unit the picker selects and the baker composites.
/// <para>
/// Identity is the value itself: <see cref="Slot"/> plus <see cref="Base"/> plus
/// <see cref="Variant"/> names exactly one file, and base names never collide across the three
/// packs (core is <c>hair1-12</c>, expansion 1 continues <c>hair13-21</c>, expansion 2
/// <c>hair22-25</c>, and the same holds for every other numbered slot). That is why this is a
/// <see langword="readonly"/> <see langword="record"/> <see langword="struct"/> with no surrogate
/// id — structural equality already is the identity.
/// </para>
/// </summary>
public readonly record struct AssetPartial
{
    /// <summary>Which character layer this file belongs to, and therefore its draw order.</summary>
    public required AssetSlot Slot { get; init; }

    /// <summary>Which of the three packs supplied it. Derivable from the name, carried for display.</summary>
    public required ElementsPack Pack { get; init; }

    /// <summary>The name without its colour-variant suffix, e.g. <c>top11</c> or <c>shield1L</c>.</summary>
    public required string Base { get; init; }

    /// <summary>
    /// The <c>_cN</c> colour variant, or <c>0</c> for the base file. Variants recolour the
    /// garment and leave skin untouched; on heads they are eye colours.
    /// </summary>
    public required int Variant { get; init; }

    /// <summary>Absolute path to the <c>.png</c>.</summary>
    public required FullPath Path { get; init; }

    /// <summary>The file's name on disk, including extension.</summary>
    public string FileName => Variant is 0 ? $"{Base}.png" : $"{Base}_c{Variant}.png";

    /// <summary>
    /// The segment this partial contributes to a baked sheet's name. The underscore is dropped
    /// (<c>top11c5</c>, not <c>top11_c5</c>) because the underscore separates <em>slots</em> in
    /// an output stem, and a segment that contained one would be ambiguous to read back.
    /// </summary>
    public string Stem => Variant is 0 ? Base : $"{Base}c{Variant}";

    /// <summary>How this partial orders against its siblings within <see cref="Slot"/>.</summary>
    public AssetSortKey SortKey => AssetName.SortKey(Base, Variant);
}
```

- [ ] **Step 5: Create `AssetCatalog.cs`**

```csharp
using System.Collections.Frozen;
using System.Collections.Immutable;
using CommunityToolkit.Diagnostics;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// Every partial the three configured packs hold, grouped by slot and ordered for display.
/// <para>
/// This replaces the hard-coded recipe table that preceded it. The library is roughly 995 files
/// across 156 base names, which is past the point where enumerating art in source is honest.
/// </para>
/// <para>
/// The scan reads directory entries only — no image is decoded — so it is cheap enough to re-run
/// whenever a pack path changes, which is exactly where the app calls it.
/// </para>
/// </summary>
public sealed class AssetCatalog
{
    private readonly FrozenDictionary<AssetSlot, ImmutableArray<AssetPartial>> _bySlot;

    private AssetCatalog(FrozenDictionary<AssetSlot, ImmutableArray<AssetPartial>> bySlot)
    {
        _bySlot = bySlot;

        Count = 0;

        foreach (var slot in AssetSlots.DrawOrder)
        {
            Count += bySlot[slot].Length;
        }
    }

    /// <summary>How many partials the scan found, across every slot and pack.</summary>
    public int Count { get; }

    /// <summary>
    /// Walks the three packs and indexes what they hold.
    /// </summary>
    /// <returns>
    /// The catalogue, or <see cref="CatalogFailure.PackDirectoryMissing"/> when a configured root
    /// is absent, or <see cref="CatalogFailure.NoPartialsFound"/> when all three exist but hold no
    /// partial in any slot folder.
    /// </returns>
    public static Result<AssetCatalog, CatalogFailure> Scan(SourcePacks packs)
    {
        Guard.IsNotNull(packs);

        (ElementsPack Pack, FullPath Assets)[] roots =
        [
            (ElementsPack.Core, packs.CoreAssets),
            (ElementsPack.CharacterExpansion1, packs.Expansion1Assets),
            (ElementsPack.CharacterExpansion2, packs.Expansion2Assets),
        ];

        foreach (var (_, assets) in roots)
        {
            if (!Directory.Exists(assets.Value))
            {
                return new(CatalogFailure.PackDirectoryMissing);
            }
        }

        var builders = new Dictionary<AssetSlot, ImmutableArray<AssetPartial>.Builder>();

        foreach (var slot in AssetSlots.DrawOrder)
        {
            builders[slot] = ImmutableArray.CreateBuilder<AssetPartial>();
        }

        var total = 0;

        foreach (var (pack, assets) in roots)
        {
            foreach (var slot in AssetSlots.DrawOrder)
            {
                // A slot folder legitimately missing from a pack is normal, not a failure:
                // expansion 2 ships no frontextra, and only the core pack ships a shadow.
                var directory = new DirectoryInfo((assets / AssetSlots.FolderName(slot)).Value);

                if (!directory.Exists)
                {
                    continue;
                }

                // ZLinq.FileSystem's value-enumerable walk, the project's stated replacement for
                // Directory.EnumerateFiles + LINQ.
                foreach (var entry in directory.Children())
                {
                    if (entry is not FileInfo file
                        || !file.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var (baseName, variant) = AssetName.Split(Path.GetFileNameWithoutExtension(file.Name));

                    builders[slot].Add(new()
                    {
                        Slot = slot,
                        Pack = pack,
                        Base = baseName,
                        Variant = variant,
                        Path = FullPath.FromPath(file.FullName),
                    });

                    total++;
                }
            }
        }

        if (total is 0)
        {
            return new(CatalogFailure.NoPartialsFound);
        }

        var indexed = new Dictionary<AssetSlot, ImmutableArray<AssetPartial>>(builders.Count);

        foreach (var (slot, builder) in builders)
        {
            var ordered = builder.ToArray();

            Array.Sort(ordered, static (left, right) => left.SortKey.CompareTo(right.SortKey));

            indexed[slot] = [.. ordered];
        }

        return new AssetCatalog(indexed.ToFrozenDictionary());
    }

    /// <summary>
    /// Every partial in <paramref name="slot"/>, base files and colour variants alike, in display
    /// order. Empty when no pack ships that slot.
    /// </summary>
    public ImmutableArray<AssetPartial> Partials(AssetSlot slot) => _bySlot[slot];

    /// <summary>
    /// The base files of <paramref name="slot"/> only — what the picker lists. Colour variants are
    /// reached through the per-slot variants toggle, which expands a ticked base into
    /// <see cref="Partials"/> sharing its <see cref="AssetPartial.Base"/>.
    /// </summary>
    public ImmutableArray<AssetPartial> Bases(AssetSlot slot) =>
        [.. _bySlot[slot].AsSpan().Where(static partial => partial.Variant is 0)];

    /// <summary>
    /// Every colour variant of <paramref name="baseName"/>, the base file first.
    /// </summary>
    public ImmutableArray<AssetPartial> VariantsOf(AssetSlot slot, string baseName)
    {
        Guard.IsNotNullOrWhiteSpace(baseName);

        return
        [
            .. _bySlot[slot].AsSpan()
                .Where(partial => string.Equals(partial.Base, baseName, StringComparison.Ordinal)),
        ];
    }

    /// <summary>
    /// Looks a partial up by its identity.
    /// </summary>
    /// <returns>
    /// The partial, or <see cref="Optional{T}.None"/> when the packs do not hold it — which is how
    /// a stale saved selection is detected after a pack is swapped.
    /// </returns>
    public Optional<AssetPartial> Find(AssetSlot slot, string baseName, int variant)
    {
        Guard.IsNotNullOrWhiteSpace(baseName);

        foreach (var partial in _bySlot[slot])
        {
            if (partial.Variant == variant
                && string.Equals(partial.Base, baseName, StringComparison.Ordinal))
            {
                return partial;
            }
        }

        return Optional<AssetPartial>.None;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~AssetCatalogTests"`

Expected: PASS.

If `directory.Children()` does not resolve, confirm `global using ZLinq;` is present in `src/TheOmenDen.PixelForge.Core/GlobalUsings.cs` and add `using ZLinq.FileSystem;` — the extension lives on `FileSystemInfoExtensions`.

- [ ] **Step 7: Full build and test, then commit**

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
```

```bash
git add src/TheOmenDen.PixelForge.Core/Catalog tests/TheOmenDen.PixelForge.Core.Tests/Catalog tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
git commit -m "feat(core): scan the three packs into an asset catalogue"
```

---

## Task 3: Packed ramp substitution

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Palettes/RampSubstitution.cs`
- Modify: `src/TheOmenDen.PixelForge.Core/Palettes/SkinRamp.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Palettes/RampSubstitutionTests.cs`

**Interfaces:**
- Consumes: `SkinRamp`, `SkinRamps` (existing).
- Produces: `RampSubstitution` (`readonly record struct` with `ImmutableArray<uint> From`, `ImmutableArray<uint> To`, `bool IsIdentity`, `int Length`); `SkinRamp.PackedRgba(SKColor) -> uint`; `SkinRamp.SubstitutionFrom(SkinRamp) -> RampSubstitution` (**return type changed** from `FrozenDictionary<uint, SKColor>`).

> This task changes a public signature that `SheetBaker.Recolor`, `RecipeBaker.Finish`, `PalettePreview.RenderIdleRow` and `SheetBakerTests` all call. Task 4 rewrites `Recolor`; to keep this task independently green, add the new members here and leave the **old** `SubstitutionFrom` in place renamed to `LegacySubstitutionFrom`, deleting it in Task 4. Do not leave two live paths beyond that boundary.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Palettes/RampSubstitutionTests.cs`:

```csharp
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Palettes;

/// <summary>
/// Pins the packed pixel layout the vectorised recolour depends on. Getting the byte order
/// wrong here swaps red and blue in every baked sheet, and nothing downstream would notice —
/// the round-trip check compares an encode against its own decode, not against expected art.
/// </summary>
public sealed class RampSubstitutionTests
{
    /// <summary>
    /// RGBA8888 stores R,G,B,A in ascending address order, so a little-endian <c>uint</c> read of
    /// that memory is <c>0xAABBGGRR</c> — red in the low byte, the reverse of the
    /// <see cref="SkinRamp.Pack"/> key's <c>0xRRGGBB</c>.
    /// </summary>
    [Fact]
    public void PackedRgba_PutsRedInTheLowByteAndOpaqueAlphaInTheHigh()
    {
        var packed = SkinRamp.PackedRgba(new SKColor(0x73, 0x17, 0x2D, 0xFF));

        Assert.Equal(0xFF2D1773u, packed);
    }

    [Fact]
    public void PackedRgba_ForcesOpaqueAlpha_RegardlessOfTheColoursOwn()
    {
        var packed = SkinRamp.PackedRgba(new SKColor(0x12, 0x34, 0x56, 0x00));

        Assert.Equal(0xFFu, packed >> 24);
    }

    [Fact]
    public void PackedRgba_AndPack_DescribeTheSameColour()
    {
        foreach (var step in SkinRamps.Source.Steps)
        {
            var packed = SkinRamp.PackedRgba(step);

            Assert.Equal((uint)step.Red, packed & 0xFF);
            Assert.Equal((uint)step.Green, (packed >> 8) & 0xFF);
            Assert.Equal((uint)step.Blue, (packed >> 16) & 0xFF);
            Assert.Equal(SkinRamp.Pack(step), ((uint)step.Red << 16) | ((uint)step.Green << 8) | step.Blue);
        }
    }

    [Fact]
    public void SubstitutionFrom_PairsEveryStepInOrder()
    {
        var target = SkinRamps.All[4];
        var substitution = target.SubstitutionFrom(SkinRamps.Source);

        Assert.Equal(SkinRamps.StepCount, substitution.Length);

        for (var step = 0; step < SkinRamps.StepCount; step++)
        {
            Assert.Equal(SkinRamp.PackedRgba(SkinRamps.Source.Steps[step]), substitution.From[step]);
            Assert.Equal(SkinRamp.PackedRgba(target.Steps[step]), substitution.To[step]);
        }
    }

    /// <summary>
    /// The default tone is the ramp the art is already authored in, so substituting it is a no-op.
    /// The baker uses this to skip a pass over 212,000 pixels per layer.
    /// </summary>
    [Fact]
    public void IsIdentity_IsTrueOnlyWhenSourceAndTargetMatch()
    {
        Assert.True(SkinRamps.Source.SubstitutionFrom(SkinRamps.Source).IsIdentity);
        Assert.False(SkinRamps.All[3].SubstitutionFrom(SkinRamps.Source).IsIdentity);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RampSubstitutionTests"`

Expected: build failure — `RampSubstitution` and `SkinRamp.PackedRgba` do not exist.

- [ ] **Step 3: Create `RampSubstitution.cs`**

```csharp
using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Palettes;

/// <summary>
/// A five-entry colour substitution, held as two parallel arrays of packed pixels.
/// <para>
/// This replaces the <see cref="System.Collections.Frozen.FrozenDictionary{TKey, TValue}"/> the
/// recolour used to consult once per pixel. A hash lookup is the wrong shape for a table of five:
/// five straight comparisons beat it even scalar, and they vectorise, which a dictionary cannot.
/// </para>
/// <para>
/// Both arrays hold whole 32-bit pixels with alpha forced opaque, not bare RGB. The source art has
/// strictly binary alpha — verified across all 995 partials — so an opaque pixel is always
/// <c>0xFF______</c> and a transparent one can never equal an entry here. Comparing the full pixel
/// therefore excludes transparent pixels for free, with no mask, no separate opacity test and no
/// alpha re-combination. See <see cref="Baking.SheetBaker"/> for the loop that relies on it.
/// </para>
/// </summary>
public readonly record struct RampSubstitution
{
    /// <summary>Packed pixels to look for, in ramp-step order.</summary>
    public required ImmutableArray<uint> From { get; init; }

    /// <summary>Packed pixels to write, index-aligned with <see cref="From"/>.</summary>
    public required ImmutableArray<uint> To { get; init; }

    /// <summary>How many steps the substitution covers.</summary>
    public int Length => From.Length;

    /// <summary>
    /// Whether every step maps to itself, making the substitution a no-op the baker can skip
    /// entirely. <see langword="true"/> when a sheet is baked in the tone its art is authored in.
    /// </summary>
    public bool IsIdentity
    {
        get
        {
            for (var step = 0; step < From.Length; step++)
            {
                if (From[step] != To[step])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
```

- [ ] **Step 4: Add `PackedRgba` and the new `SubstitutionFrom` to `SkinRamp.cs`**

Rename the existing `SubstitutionFrom` to `LegacySubstitutionFrom` (keeping its body and its `FrozenDictionary` return type untouched), then add:

```csharp
    /// <summary>
    /// Substitution table taking <paramref name="source"/>'s colours to this ramp's, as packed
    /// pixels ready for the vectorised recolour. Identity when this ramp <em>is</em>
    /// <paramref name="source"/>.
    /// </summary>
    public RampSubstitution SubstitutionFrom(SkinRamp source)
    {
        Guard.IsNotNull(source);
        Guard.IsEqualTo(source.Steps.Length, SkinRamps.StepCount);
        Guard.IsEqualTo(Steps.Length, SkinRamps.StepCount);

        var from = ImmutableArray.CreateBuilder<uint>(SkinRamps.StepCount);
        var to = ImmutableArray.CreateBuilder<uint>(SkinRamps.StepCount);

        for (var step = 0; step < SkinRamps.StepCount; step++)
        {
            from.Add(PackedRgba(source.Steps[step]));
            to.Add(PackedRgba(Steps[step]));
        }

        return new()
        {
            From = from.ToImmutable(),
            To = to.ToImmutable(),
        };
    }

    /// <summary>
    /// Packs a colour into a whole RGBA8888 pixel with alpha forced opaque.
    /// <para>
    /// The byte order is the trap. RGBA8888 lays out R,G,B,A in ascending addresses, so reading
    /// that memory as a little-endian <see cref="uint"/> yields <c>0xAABBGGRR</c> — red in the
    /// <em>low</em> byte. <see cref="Pack"/>'s dictionary key is the opposite, <c>0xRRGGBB</c>.
    /// Confusing the two swaps red and blue in every baked sheet, and the round-trip verification
    /// would not catch it because it compares an encode against its own decode.
    /// </para>
    /// <para>
    /// Alpha is forced to <c>0xFF</c> rather than taken from <paramref name="color"/> because these
    /// values are compared against opaque pixels only; see <see cref="RampSubstitution"/>.
    /// </para>
    /// </summary>
    public static uint PackedRgba(SKColor color) =>
        0xFF000000u | ((uint)color.Blue << 16) | ((uint)color.Green << 8) | color.Red;
```

Add `using System.Collections.Immutable;` if it is not already present.

- [ ] **Step 5: Point existing callers at `LegacySubstitutionFrom`**

Three call sites break on the new overload's return type. Change each to `LegacySubstitutionFrom` for now — Task 4 deletes the legacy member and moves them back:

- `src/TheOmenDen.PixelForge.Core/Baking/RecipeBaker.cs` — in `Finish`
- `src/TheOmenDen.PixelForge.Core/Palettes/PalettePreview.cs` — in `RenderIdleRow`
- `tests/TheOmenDen.PixelForge.Core.Tests/Baking/SheetBakerTests.cs` — in `Recolor_ReplacesRampColours_AndLeavesEverythingElseAlone` and `Recolor_PreservesPerStepPixelCounts`

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: PASS, all existing tests plus the new ones.

- [ ] **Step 7: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Palettes tests/TheOmenDen.PixelForge.Core.Tests/Palettes src/TheOmenDen.PixelForge.Core/Baking/RecipeBaker.cs tests/TheOmenDen.PixelForge.Core.Tests/Baking/SheetBakerTests.cs
git commit -m "feat(core): pack the ramp substitution for a vectorised recolour"
```

---

## Task 4: Vectorise the recolour

**Files:**
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/SheetBaker.cs`
- Modify: `src/TheOmenDen.PixelForge.Core/Palettes/SkinRamp.cs` (delete `LegacySubstitutionFrom`)
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/RecipeBaker.cs`, `src/TheOmenDen.PixelForge.Core/Palettes/PalettePreview.cs` (back to `SubstitutionFrom`)
- Modify: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/SheetBakerTests.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RecolorVectorTests.cs`

**Interfaces:**
- Consumes: `RampSubstitution`, `SkinRamp.PackedRgba`, `SkinRamp.SubstitutionFrom` (Task 3).
- Produces: `SheetBaker.Recolor(SKBitmap, RampSubstitution) -> Result<SKBitmap, BakeFailure>` (**signature changed**); `SheetBaker.Substitute(Span<uint>, RampSubstitution)` (`internal`, vectorised); `SheetBaker.SubstituteScalar(Span<uint>, RampSubstitution)` (`internal`, the reference implementation).

> `SubstituteScalar` is `internal` and exists so the test can prove the vector path agrees with it. Add `[assembly: InternalsVisibleTo("TheOmenDen.PixelForge.Core.Tests")]` to `src/TheOmenDen.PixelForge.Core/GlobalUsings.cs` if it is not already there.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RecolorVectorTests.cs`:

```csharp
using System.Numerics;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The vector path is an optimisation of the scalar one, so the scalar one is the oracle: every
/// assertion here is "these two agree", plus the boundary conditions that a hand-written SIMD
/// loop gets wrong — a length that is not a whole number of vectors, and the alpha assumption the
/// whole-pixel comparison rests on.
/// </summary>
public sealed class RecolorVectorTests
{
    private static RampSubstitution Substitution() => SkinRamps.All[3].SubstitutionFrom(SkinRamps.Source);

    /// <summary>
    /// A buffer of every ramp colour, some non-ramp colours, and transparent pixels, at a length
    /// deliberately coprime with any vector width so the scalar tail is always exercised.
    /// </summary>
    private static uint[] MixedBuffer(int length)
    {
        var buffer = new uint[length];

        for (var i = 0; i < length; i++)
        {
            buffer[i] = (i % 8) switch
            {
                0 or 1 or 2 or 3 or 4 => SkinRamp.PackedRgba(SkinRamps.Source.Steps[i % 5]),
                5 => 0xFF563412u,                              // opaque, not in the ramp
                6 => 0x00000000u,                              // fully transparent
                _ => SkinRamp.PackedRgba(SkinRamps.Source.Steps[0]) & 0x00FFFFFFu,  // ramp RGB, alpha 0
            };
        }

        return buffer;
    }

    [Fact]
    public void Substitute_AgreesWithTheScalarReference()
    {
        var substitution = Substitution();

        // 1003 is prime, so it is never a whole multiple of Vector<uint>.Count.
        var vectorised = MixedBuffer(1003);
        var scalar = (uint[])vectorised.Clone();

        SheetBaker.Substitute(vectorised, substitution);
        SheetBaker.SubstituteScalar(scalar, substitution);

        Assert.Equal(scalar, vectorised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(65)]
    public void Substitute_AgreesWithTheScalarReference_AtEveryAwkwardLength(int length)
    {
        var substitution = Substitution();
        var vectorised = MixedBuffer(length);
        var scalar = (uint[])vectorised.Clone();

        SheetBaker.Substitute(vectorised, substitution);
        SheetBaker.SubstituteScalar(scalar, substitution);

        Assert.Equal(scalar, vectorised);
    }

    /// <summary>
    /// The whole-pixel comparison is what makes the loop cheap, and this is the property it buys:
    /// a transparent pixel is never rewritten, even when its RGB happens to equal a ramp colour.
    /// </summary>
    [Fact]
    public void Substitute_LeavesTransparentPixelsAlone_EvenWhenTheirRgbMatchesTheRamp()
    {
        var substitution = Substitution();
        var rampRgbButTransparent = SkinRamp.PackedRgba(SkinRamps.Source.Steps[0]) & 0x00FFFFFFu;

        uint[] buffer = [rampRgbButTransparent];

        SheetBaker.Substitute(buffer, substitution);

        Assert.Equal(rampRgbButTransparent, buffer[0]);
    }

    /// <summary>
    /// Documents the boundary rather than guarding live input: a semi-transparent ramp pixel is
    /// <em>not</em> substituted. No such pixel exists in the shipped packs — all 995 partials
    /// decode with strictly binary alpha — but art authored with antialiased edges would both
    /// break this and break <see cref="SheetBaker.Assemble"/>'s exact premultiplied round trip.
    /// </summary>
    [Fact]
    public void Substitute_SkipsSemiTransparentRampPixels_WhichTheShippedPacksNeverContain()
    {
        var substitution = Substitution();
        var halfAlpha = (SkinRamp.PackedRgba(SkinRamps.Source.Steps[0]) & 0x00FFFFFFu) | 0x80000000u;

        uint[] buffer = [halfAlpha];

        SheetBaker.Substitute(buffer, substitution);

        Assert.Equal(halfAlpha, buffer[0]);
    }

    [Fact]
    public void Substitute_ReplacesEveryRampStepWithItsTarget()
    {
        var target = SkinRamps.All[5];
        var substitution = target.SubstitutionFrom(SkinRamps.Source);
        var buffer = new uint[SkinRamps.StepCount];

        for (var step = 0; step < SkinRamps.StepCount; step++)
        {
            buffer[step] = SkinRamp.PackedRgba(SkinRamps.Source.Steps[step]);
        }

        SheetBaker.Substitute(buffer, substitution);

        for (var step = 0; step < SkinRamps.StepCount; step++)
        {
            Assert.Equal(SkinRamp.PackedRgba(target.Steps[step]), buffer[step]);
        }
    }

    /// <summary>Sanity check that the vector width is what the loop thinks it is.</summary>
    [Fact]
    public void VectorWidth_IsAtLeastOnePixel() => Assert.True(Vector<uint>.Count >= 1);

    /// <summary>The pixel-facing entry point still returns a bitmap and still honours geometry.</summary>
    [Fact]
    public void Recolor_ReplacesRampColoursThroughTheBitmapApi()
    {
        var target = SkinRamps.All[3];

        using var source = new SKBitmap(new SKImageInfo(4, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var pixels = source.Pixels;

        pixels[0] = SkinRamps.Source.Steps[0];
        pixels[1] = SkinRamps.Source.Steps[3];
        pixels[2] = new SKColor(0x12, 0x34, 0x56, 0xFF);
        pixels[3] = SKColors.Transparent;
        source.Pixels = pixels;

        var result = SheetBaker.Recolor(source, target.SubstitutionFrom(SkinRamps.Source));

        Assert.True(result.IsSuccessful, $"recolor failed with {result.Error}");

        using var recolored = result.Value;

        Assert.Equal(target.Steps[0], recolored.GetPixel(0, 0));
        Assert.Equal(target.Steps[3], recolored.GetPixel(1, 0));
        Assert.Equal(new SKColor(0x12, 0x34, 0x56, 0xFF), recolored.GetPixel(2, 0));
        Assert.Equal(0, recolored.GetPixel(3, 0).Alpha);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RecolorVectorTests"`

Expected: build failure — `SheetBaker.Substitute` and `SheetBaker.SubstituteScalar` do not exist and `Recolor` has the wrong signature.

- [ ] **Step 3: Replace `SheetBaker.Recolor`**

Delete the existing `Recolor` method **and its inaccurate remarks block**, and add:

```csharp
    /// <summary>
    /// Applies a palette substitution, leaving every colour outside the table untouched and every
    /// transparent pixel exactly as it was.
    /// </summary>
    /// <returns>
    /// The recoloured bitmap in canonical format, or
    /// <see cref="BakeFailure.LayerPixelFormatMismatch"/> when the source cannot be converted to it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Hand-written, but by choice rather than for want of a library.
    /// <see cref="SKColorFilter.CreateTable"/> applies four independent per-channel lookups and
    /// cannot express "change this pixel only when all three channels match";
    /// <see cref="SKColorFilter.CreateColorMatrix"/> is a linear transform and is no closer. Skia
    /// <em>can</em> express it through <see cref="SKRuntimeEffect"/>, and
    /// <c>ColorHelper.ColorComparer</c> compares single colours across colour models. Both were
    /// rejected: a runtime effect works in float, while this substitution must be byte-exact to
    /// survive <see cref="LosslessWebp.EncodeVerified"/>'s round trip, and it would add a shader
    /// compile as a new failure mode; <c>ColorComparer</c> is a scalar helper that would allocate
    /// per pixel.
    /// </para>
    /// </remarks>
    public static Result<SKBitmap, BakeFailure> Recolor(SKBitmap source, RampSubstitution substitution)
    {
        Guard.IsNotNull(source);

        var recolored = ToCanonical(source);

        if (!recolored.TryGet(out var target))
        {
            return recolored;
        }

        if (substitution.IsIdentity)
        {
            return target;
        }

        using var pixmap = target.PeekPixels();

        // MemoryMarshal.Cast is a zero-copy reinterpretation. SKBitmap.Pixels would allocate an
        // SKColor[] — 828 KiB for a source partial, straight to the LOH.
        Substitute(MemoryMarshal.Cast<byte, uint>(pixmap.GetPixelSpan()), substitution);

        return target;
    }

    /// <summary>
    /// Substitutes packed pixels in place, vectorised, with a scalar tail for the remainder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whole 32-bit pixels are compared, alpha included. That is only correct because the source
    /// art has strictly binary alpha — verified across all 995 partials — so every opaque pixel is
    /// <c>0xFF______</c> and no transparent pixel can equal an entry in
    /// <see cref="RampSubstitution.From"/>. It buys the removal of a mask, a separate opacity test
    /// and an alpha recombination from the inner loop, leaving five comparisons and five selects.
    /// </para>
    /// <para>
    /// <see cref="Vector{T}"/> rather than <c>Vector256&lt;T&gt;</c> so the JIT picks the widest
    /// register the machine has — 8 pixels at a time under AVX2, 16 under AVX-512 — instead of the
    /// source pinning an instruction set.
    /// </para>
    /// </remarks>
    internal static void Substitute(Span<uint> pixels, RampSubstitution substitution)
    {
        var width = Vector<uint>.Count;

        if (!Vector.IsHardwareAccelerated || pixels.Length < width)
        {
            SubstituteScalar(pixels, substitution);
            return;
        }

        // Broadcast once, outside the loop: the table is five entries and never changes mid-pass.
        var from = new Vector<uint>[substitution.Length];
        var to = new Vector<uint>[substitution.Length];

        for (var step = 0; step < substitution.Length; step++)
        {
            from[step] = new(substitution.From[step]);
            to[step] = new(substitution.To[step]);
        }

        var index = 0;

        for (; index <= pixels.Length - width; index += width)
        {
            var block = new Vector<uint>(pixels.Slice(index, width));
            var result = block;

            // Comparing against `block`, never `result`: the five source colours are distinct, so
            // a lane can match at most one, and folding earlier writes back in would let a target
            // colour that happens to equal a later source colour be substituted twice.
            for (var step = 0; step < from.Length; step++)
            {
                result = Vector.ConditionalSelect(Vector.Equals(block, from[step]), to[step], result);
            }

            result.CopyTo(pixels.Slice(index, width));
        }

        SubstituteScalar(pixels[index..], substitution);
    }

    /// <summary>
    /// The unvectorised substitution. Handles the tail of <see cref="Substitute"/>, and is the
    /// reference the vector path is tested against.
    /// </summary>
    internal static void SubstituteScalar(Span<uint> pixels, RampSubstitution substitution)
    {
        var from = substitution.From.AsSpan();
        var to = substitution.To.AsSpan();

        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];

            for (var step = 0; step < from.Length; step++)
            {
                if (pixel == from[step])
                {
                    pixels[index] = to[step];
                    break;
                }
            }
        }
    }
```

Add these usings to `SheetBaker.cs` and remove `using System.Collections.Frozen;`:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
using TheOmenDen.PixelForge.Core.Palettes;
```

Also update the class-level `<summary>` where it claims the recolour is the operation "neither library can express" — say instead that it is the one operation done by hand, and point at `Recolor`'s remarks for why.

- [ ] **Step 4: Delete the legacy member and restore the callers**

- `SkinRamp.cs` — delete `LegacySubstitutionFrom` and the now-unused `using System.Collections.Frozen;`. Keep `Pack`: `SheetBakerTests.CountOf` still uses it and it remains the natural key for palette-editor comparisons.
- `RecipeBaker.cs` and `PalettePreview.cs` — change `LegacySubstitutionFrom` back to `SubstitutionFrom`.
- `SheetBakerTests.cs` — change `RecolorOrFail`'s parameter from `FrozenDictionary<uint, SKColor>` to `RampSubstitution`, drop `using System.Collections.Frozen;`, and change both call sites back to `SubstitutionFrom`.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: PASS. `Recolor_PreservesPerStepPixelCounts` passing unchanged is the signal that the packed byte order is right — it counts colours through `SkinRamp.Pack`, which the new path never touches.

- [ ] **Step 6: Commit**

```bash
git add src/TheOmenDen.PixelForge.Core tests/TheOmenDen.PixelForge.Core.Tests
git commit -m "perf(core): vectorise the recolour and drop the per-pixel dictionary"
```

---

## Task 5: Per-layer recolour

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Baking/AssetLayer.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Baking/SheetGeometry.cs`
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/SheetRecipe.cs`
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/RecipeBaker.cs`
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/RoostSheets.cs`
- Modify: `src/TheOmenDen.PixelForge.Core/Palettes/PalettePreview.cs`
- Delete: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RecipeBakerOverlayTests.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/PerLayerRecolorTests.cs`

**Interfaces:**
- Consumes: `AssetSlots.IsSkinBearing` (Task 1), `RampSubstitution` (Task 3), `SheetBaker.Recolor` (Task 4).
- Produces: `AssetLayer(FullPath Path, bool IsSkin)` (`readonly record struct`); `SheetGeometry` enum (`Curated = 0`, `Full = 1`); reshaped `SheetRecipe` with `Name`, `ImmutableArray<AssetLayer> Layers`, `Optional<SkinRamp> Tone`, `SheetGeometry Geometry`; `RecipeBaker.AssembleLayers(SheetRecipe) -> Result<SKBitmap, BakeFailure>` (now applies per-layer recolour); `RoostSheets.Bodies/Hair/All(SourcePacks)` unchanged in shape.

> **Deleted in this task:** `SheetRecipe.Overlays`, `RecipeBaker.ApplyOverlays`, `RoostSheets.Flattened`, and `RecipeBakerOverlayTests`. Do not preserve them behind a flag — per-layer recolour makes the problem they solved impossible to have.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/PerLayerRecolorTests.cs`:

```csharp
using System.Collections.Immutable;
using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Palettes;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The reason the substitution moved off the flattened assembly and onto individual layers.
/// <para>
/// Against the full library a whole-assembly recolour is simply wrong: 23 of 28 tops draw bare
/// arms and hands, so skin lives on the top layer, while hats and hair legitimately use the same
/// hexes as trim and highlights. Only a per-layer rule can recolour the first and spare the
/// second — and it is also the only formulation that handles back-hair, which draws *below* the
/// body and so could never have been an after-the-fact overlay.
/// </para>
/// </summary>
public sealed class PerLayerRecolorTests
{
    /// <summary>Writes a source-geometry PNG filled with one colour.</summary>
    private static FullPath WriteLayer(FullPath directory, string name, SKColor fill)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        bitmap.Erase(fill);

        var path = directory / (name + ".png");

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    [Fact]
    public void AssembleLayers_RecoloursOnlySkinBearingLayers()
    {
        using var root = TemporaryDirectory.Create();

        var rampColour = SkinRamps.Source.Steps[1];

        // Both layers are painted the *same* ramp colour. Only the one marked IsSkin may change.
        var skin = WriteLayer(root.FullPath, "top", rampColour);
        var authored = WriteLayer(root.FullPath, "hat", rampColour);

        var target = SkinRamps.All[4];

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Tone = target,
            Layers =
            [
                new(skin, IsSkin: true),
                new(authored, IsSkin: false),
            ],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        // The hat is drawn last and is opaque, so what survives on top is the *authored* colour.
        Assert.Equal(rampColour, assembled.GetPixel(0, 0));
    }

    [Fact]
    public void AssembleLayers_RecoloursASkinLayerWhenNothingCoversIt()
    {
        using var root = TemporaryDirectory.Create();

        var rampColour = SkinRamps.Source.Steps[1];
        var skin = WriteLayer(root.FullPath, "top", rampColour);
        var target = SkinRamps.All[4];

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Tone = target,
            Layers = [new(skin, IsSkin: true)],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        Assert.Equal(target.Steps[1], assembled.GetPixel(0, 0));
    }

    [Fact]
    public void AssembleLayers_LeavesEverythingAlone_WhenNoToneIsChosen()
    {
        using var root = TemporaryDirectory.Create();

        var rampColour = SkinRamps.Source.Steps[1];
        var skin = WriteLayer(root.FullPath, "top", rampColour);

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Layers = [new(skin, IsSkin: true)],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.True(result.IsSuccessful, $"assemble failed with {result.Error}");

        using var assembled = result.Value;

        Assert.Equal(rampColour, assembled.GetPixel(0, 0));
    }

    [Fact]
    public void AssembleLayers_ReportsLayerNotFound_WhenAPartialIsMissing()
    {
        using var root = TemporaryDirectory.Create();

        var recipe = new SheetRecipe
        {
            Name = "probe",
            Layers = [new(root.FullPath / "absent.png", IsSkin: true)],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.LayerNotFound, result.Error);
    }

    [Fact]
    public void AssembleLayers_ReportsNoLayersSupplied_WhenTheRecipeIsEmpty()
    {
        var recipe = new SheetRecipe
        {
            Name = "probe",
            Layers = [],
        };

        var result = RecipeBaker.AssembleLayers(recipe);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.NoLayersSupplied, result.Error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~PerLayerRecolorTests"`

Expected: build failure — `AssetLayer` does not exist and `SheetRecipe.Layers` is still `ImmutableArray<FullPath>`.

- [ ] **Step 3: Create `AssetLayer.cs` and `SheetGeometry.cs`**

```csharp
using Meziantou.Framework;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One partial in a recipe's draw order, and whether the skin substitution applies to it.
/// </summary>
/// <param name="Path">Absolute path to the partial.</param>
/// <param name="IsSkin">
/// <see langword="true"/> when this layer carries skin and must take the recipe's tone.
/// Seeded from <see cref="Catalog.AssetSlots.IsSkinBearing"/>, but carried per layer rather than
/// looked up per slot — that is the escape hatch for excluding a single partial later without
/// touching the baker or reclassifying its whole slot.
/// </param>
/// <remarks>
/// <para>
/// Named <c>IsSkin</c> rather than something like <c>Recolor</c> on purpose: the layer states
/// whether it <em>carries skin</em>, while <see cref="SheetRecipe.Tone"/> states which tone to
/// apply. A layer is substituted when both are set. Naming both ends the same thing would read as
/// one switch expressed in two places.
/// </para>
/// </remarks>
public readonly record struct AssetLayer(FullPath Path, bool IsSkin);
```

```csharp
namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>Which geometry a baked sheet is written in.</summary>
public enum SheetGeometry
{
    /// <summary>
    /// The 240x1152 sheet Corvus consumes: 8 clips on 3 facings, north dropped, described by
    /// <see cref="Spritesheets.SheetIndex"/>. This is a shipped contract — see
    /// <see cref="Spritesheets.SheetLayout"/> — and must stay byte-identical.
    /// </summary>
    Curated = 0,

    /// <summary>
    /// The raw 1104x192 assembly: all 23 source columns on all 4 facing rows, written without a
    /// remap. Keeps the nock/bow draw, climb and the north facing, which the curated geometry
    /// drops. Described by <see cref="Spritesheets.ClipIndex"/>.
    /// </summary>
    Full = 1,
}
```

- [ ] **Step 4: Reshape `SheetRecipe.cs`**

Replace the whole file:

```csharp
using System.Collections.Immutable;
using DotNext;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One output sheet: the layers that make it, in back-to-front draw order, and the tone its
/// skin-bearing layers are substituted into.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>Overlays</c> collection any more. It existed to draw hair <em>after</em> a
/// substitution that ran over the flattened assembly, so hair's authored colour survived. Once the
/// substitution moved onto individual layers that problem cannot arise, and the old shape could
/// never have expressed back-hair anyway — it draws below the body, so "after the recolour" and
/// "behind the body" were mutually exclusive.
/// </para>
/// </remarks>
public sealed record SheetRecipe
{
    /// <summary>Output stem, e.g. <c>body-01</c>. The <c>.webp</c> is added when written.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Layers back to front, following the generator's <c>CharacterLayers</c> order — which is
    /// also <see cref="Catalog.AssetSlot"/>'s member order, so a planner can sort by slot.
    /// </summary>
    public required ImmutableArray<AssetLayer> Layers { get; init; }

    /// <summary>
    /// The skin tone to substitute into, applied only to layers whose
    /// <see cref="AssetLayer.IsSkin"/> is set.
    /// <para>
    /// <see cref="Optional{T}"/> rather than a nullable reference so "keep the authored tone" is a
    /// value the type system carries, not a <see langword="null"/> every caller must remember to
    /// check. A hair-only sheet has no tone at all.
    /// </para>
    /// </summary>
    public Optional<SkinRamp> Tone { get; init; } = Optional<SkinRamp>.None;

    /// <summary>Which geometry to write. Defaults to the Corvus contract.</summary>
    public SheetGeometry Geometry { get; init; } = SheetGeometry.Curated;
}
```

- [ ] **Step 5: Rewrite `RecipeBaker.AssembleLayers` and `Finish`**

In `RecipeBaker.cs`: delete `ApplyOverlays` entirely, and replace `AssembleLayers` and `Finish`:

```csharp
    /// <summary>
    /// Decodes a recipe's layers, recolours the skin-bearing ones, and composites the result in
    /// draw order.
    /// </summary>
    /// <returns>
    /// The assembled source-geometry bitmap, or the first failure encountered:
    /// <see cref="BakeFailure.NoLayersSupplied"/>, <see cref="BakeFailure.LayerNotFound"/>,
    /// <see cref="BakeFailure.LayerUnreadable"/> or a geometry or format mismatch.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The substitution runs per layer, before compositing, rather than once over the flattened
    /// result. That is what lets a bare-armed top take the skin tone while a hat's ramp-coloured
    /// trim keeps its authored one, and it is the only ordering that works for back-hair, which
    /// draws beneath the body.
    /// </para>
    /// <para>
    /// Exposed for the composite preview, which needs the assembly but neither a curate nor an
    /// encode. Sharing it keeps the decode-and-validate loop in one place.
    /// </para>
    /// </remarks>
    public static Result<SKBitmap, BakeFailure> AssembleLayers(SheetRecipe recipe)
    {
        Guard.IsNotNull(recipe);

        if (recipe.Layers.IsDefaultOrEmpty)
        {
            return new(BakeFailure.NoLayersSupplied);
        }

        var substitution = recipe.Tone.TryGet(out var ramp)
            ? ramp.SubstitutionFrom(SkinRamps.Source)
            : default;

        var hasTone = recipe.Tone.HasValue;
        var prepared = new List<SKBitmap>(recipe.Layers.Length);

        try
        {
            foreach (var layer in recipe.Layers)
            {
                if (!File.Exists(layer.Path.Value))
                {
                    return new(BakeFailure.LayerNotFound);
                }

                using var decoded = SKBitmap.Decode(layer.Path.Value);

                if (decoded is null)
                {
                    return new(BakeFailure.LayerUnreadable);
                }

                if (decoded.Width != SheetLayout.SourceWidth || decoded.Height != SheetLayout.SourceHeight)
                {
                    return new(BakeFailure.LayerGeometryMismatch);
                }

                // Recolour before compositing. ToCanonical is required either way — Skia's
                // preferred type on Windows is BGRA, and the substitution reads pixel memory
                // directly — so the non-skin path is a format conversion, not a wasted pass.
                var canonical = layer.IsSkin && hasTone
                    ? SheetBaker.Recolor(decoded, substitution)
                    : SheetBaker.ToCanonical(decoded);

                if (!canonical.TryGet(out var ready))
                {
                    return new(canonical.Error);
                }

                prepared.Add(ready);
            }

            return SheetBaker.Assemble(prepared);
        }
        finally
        {
            foreach (var layer in prepared)
            {
                layer.Dispose();
            }
        }
    }

    private static Result<RecyclableMemoryStream, BakeFailure> Finish(
        SKBitmap assembled,
        SheetRecipe recipe)
    {
        if (recipe.Geometry is SheetGeometry.Full)
        {
            // Full geometry *is* the assembly — no remap, so nothing to curate.
            return LosslessWebp.EncodeVerified(assembled);
        }

        var curation = SheetBaker.Curate(assembled);

        if (!curation.TryGet(out var curated))
        {
            return new(curation.Error);
        }

        using (curated)
        {
            return LosslessWebp.EncodeVerified(curated);
        }
    }
```

`Bake` itself is unchanged: it still calls `AssembleLayers` then `Finish`. Remove the now-unused `using System.Collections.Immutable;` and `using Meziantou.Framework;` if the compiler flags them (IDE0005 is a build error here).

- [ ] **Step 6: Update `RoostSheets.cs` and `PalettePreview.cs`**

In `RoostSheets`, delete `Flattened` entirely and build `AssetLayer` values instead of bare paths. `BodyLayers` already names the slots, so the `IsSkin` flag comes straight from `AssetSlots.IsSkinBearing`:

```csharp
        layers.Add(new AssetLayer(packs.Partial(pack, slot, file), AssetSlots.IsSkinBearing(ToSlot(slot))));
```

Add a small `private static AssetSlot ToSlot(string folder)` that maps the three literal folder names used in the table (`"bottom"`, `"top"`, `"head"`) via `Enum.Parse<AssetSlot>(folder, ignoreCase: true)`, and document that the table's strings are the slot folder names. Change `Recolor = ...` to `Tone = ...` in `Bodies`. Hair recipes get `IsSkin: false`.

In `PalettePreview.Create`, `SheetRecipe.Recolor` is now `Tone`; the comment about ignoring it still applies — the cache must hold source-toned pixels — so pass the recipe through with `Tone` cleared:

```csharp
        var assembly = RecipeBaker.AssembleLayers(body with { Tone = Optional<SkinRamp>.None });
```

- [ ] **Step 7: Delete the overlay tests**

```bash
git rm tests/TheOmenDen.PixelForge.Core.Tests/Baking/RecipeBakerOverlayTests.cs
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: PASS. `RoostSheetsTests` may need its `Flattened` cases removed — delete them rather than adapting; the cross product now lives in `BatchPlan` (Task 8).

- [ ] **Step 9: Commit**

```bash
git add -A src/TheOmenDen.PixelForge.Core tests/TheOmenDen.PixelForge.Core.Tests
git commit -m "refactor(core): recolour per layer and delete the overlay workaround"
```

---

## Task 6: The generator's animation table and full geometry

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Spritesheets/GeneratorClip.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Spritesheets/GeneratorClips.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Spritesheets/GeneratorClipsTests.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/FullGeometryTests.cs`

**Interfaces:**
- Consumes: `SheetGeometry` (Task 5), `SheetLayout` (existing).
- Produces: `GeneratorClip` (`sealed record` with `Name`, `ImmutableArray<int> Frames`, `bool ReverseDrawOrder`, `bool IsRenderedByDefault`, `int FrameCount`); `GeneratorClips.All -> ImmutableArray<GeneratorClip>`, `GeneratorClips.FrameDurationMilliseconds -> int`, `GeneratorClips.Facings -> ImmutableArray<string>`.

- [ ] **Step 1: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Spritesheets/GeneratorClipsTests.cs`:

```csharp
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

/// <summary>
/// The generator's own animation table, transcribed. These are not inferred from the art — they
/// come from <c>Settings.json</c>'s <c>CharacterAnimations</c> block, which is why <c>walk</c>
/// opens on column 1 and returns through column 0, and why <c>jump</c> opens on the crouch frame.
/// </summary>
public sealed class GeneratorClipsTests
{
    private static GeneratorClip Clip(string name)
        => GeneratorClips.All.AsSpan().First(clip => clip.Name == name);

    [Fact]
    public void All_HasTheTwelveGeneratorAnimations() => Assert.Equal(12, GeneratorClips.All.Length);

    /// <summary>
    /// The property the curated <see cref="SheetLayout"/> model cannot express: these are
    /// playback orders, not contiguous spans. <c>walk</c> visits 1, 2, 1, 0.
    /// </summary>
    [Theory]
    [InlineData("walk", new[] { 1, 2, 1, 0 })]
    [InlineData("arms_up", new[] { 4, 5, 4, 3 })]
    [InlineData("jump", new[] { 6, 7, 8, 9 })]
    [InlineData("attack_tool", new[] { 10, 11, 12, 13, 14 })]
    [InlineData("nock_and_bow", new[] { 15, 16, 17, 18 })]
    [InlineData("bow", new[] { 17, 18, 17, 16 })]
    [InlineData("climb", new[] { 20, 21, 20, 19 })]
    [InlineData("stand", new[] { 1 })]
    [InlineData("crouch", new[] { 6 })]
    [InlineData("wind_up", new[] { 10 })]
    [InlineData("nock", new[] { 15 })]
    [InlineData("sleep_dead", new[] { 22 })]
    public void All_CarriesTheRealPlaybackOrder(string name, int[] expected)
        => Assert.Equal(expected, Clip(name).Frames);

    /// <summary>Climb is the only animation the generator draws back to front.</summary>
    [Fact]
    public void ReverseDrawOrder_IsSetOnClimbAlone()
    {
        foreach (var clip in GeneratorClips.All)
        {
            Assert.Equal(clip.Name == "climb", clip.ReverseDrawOrder);
        }
    }

    [Fact]
    public void All_NeverReferencesAColumnOutsideTheSourceSheet()
    {
        foreach (var clip in GeneratorClips.All)
        {
            foreach (var column in clip.Frames)
            {
                Assert.InRange(column, 0, SheetLayout.SourceColumns - 1);
            }
        }
    }

    /// <summary>Full geometry keeps all four facings; the curated sheet drops north.</summary>
    [Fact]
    public void Facings_AreTheFourSourceRowsInOrder()
        => Assert.Equal(["south", "west", "east", "north"], GeneratorClips.Facings);

    [Fact]
    public void FrameDuration_IsTheGeneratorsOwn() => Assert.Equal(300, GeneratorClips.FrameDurationMilliseconds);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~GeneratorClipsTests"`

Expected: build failure — `GeneratorClip` and `GeneratorClips` do not exist.

- [ ] **Step 3: Create `GeneratorClip.cs`**

```csharp
using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// One animation exactly as the Elements generator defines it.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="AnimationClip"/>, and deliberately not merged with it.
/// <see cref="AnimationClip"/> models a curated clip as a start column plus a length, which is all
/// the Corvus contract needs and must not change. That model cannot represent a playback order
/// that revisits a column — <c>walk</c> is 1, 2, 1, 0 — so full geometry carries the frame list
/// verbatim instead.
/// </para>
/// </remarks>
public sealed record GeneratorClip
{
    /// <summary>Snake-cased name, e.g. <c>arms_up</c>. Stable across manifests.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Source columns in playback order. May repeat a column, and is not necessarily ascending.
    /// </summary>
    public required ImmutableArray<int> Frames { get; init; }

    /// <summary>
    /// Whether the generator composites this animation's layers back to front. Set on
    /// <c>climb</c> alone, where the character faces away and the body must occlude the hair.
    /// </summary>
    public required bool ReverseDrawOrder { get; init; }

    /// <summary>
    /// Whether the generator exports this animation by default. Its <c>IgnoreRender</c> flag
    /// inverted — the single-frame poses (<c>stand</c>, <c>crouch</c>, <c>wind_up</c>,
    /// <c>nock</c>) and <c>bow</c> are marked ignored because a longer animation already covers
    /// their columns. Carried so a consumer can tell a pose from an animation.
    /// </summary>
    public required bool IsRenderedByDefault { get; init; }

    /// <summary>How many frames the animation plays.</summary>
    public int FrameCount => Frames.Length;
}
```

- [ ] **Step 4: Create `GeneratorClips.cs`**

```csharp
using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// Every animation the Elements generator ships, transcribed from its <c>Settings.json</c>
/// <c>CharacterAnimations</c> block.
/// </summary>
/// <remarks>
/// <para>
/// This is the machine-readable spec for the source art, so it is copied rather than inferred:
/// <c>jump</c> opening on the crouch frame and <c>attack_tool</c> opening on the wind-up frame are
/// the generator's decisions, not observations about the pixels.
/// </para>
/// <para>
/// Used only by <see cref="SheetGeometry.Full"/> output. The curated Corvus sheet keeps its own
/// eight-clip subset in <see cref="SheetLayout.Clips"/> and is unaffected by anything here.
/// </para>
/// </remarks>
public static class GeneratorClips
{
    /// <summary>
    /// The generator's <c>AnimationDelayInMilliseconds</c>. Roughly 3.3 FPS, which is the
    /// deliberate cadence of this art style's walk cycle — shipped in the manifest so a consumer
    /// plays it at the rate it was authored for rather than guessing.
    /// </summary>
    public const int FrameDurationMilliseconds = 300;

    /// <summary>
    /// Source rows top to bottom. Full geometry keeps all four; the curated sheet drops
    /// <c>north</c> — see <see cref="SheetLayout.FacingCount"/>.
    /// </summary>
    public static ImmutableArray<string> Facings { get; } = ["south", "west", "east", "north"];

    /// <summary>All twelve animations, in the order the generator declares them.</summary>
    public static ImmutableArray<GeneratorClip> All { get; } =
    [
        Clip("stand", [1], rendered: false),
        Clip("walk", [1, 2, 1, 0], rendered: true),
        Clip("arms_up", [4, 5, 4, 3], rendered: true),
        Clip("crouch", [6], rendered: false),
        Clip("jump", [6, 7, 8, 9], rendered: true),
        Clip("wind_up", [10], rendered: false),
        Clip("attack_tool", [10, 11, 12, 13, 14], rendered: true),
        Clip("nock", [15], rendered: false),
        Clip("bow", [17, 18, 17, 16], rendered: false),
        Clip("nock_and_bow", [15, 16, 17, 18], rendered: true),
        Clip("climb", [20, 21, 20, 19], rendered: true, reverseDrawOrder: true),
        Clip("sleep_dead", [22], rendered: true),
    ];

    private static GeneratorClip Clip(
        string name,
        ImmutableArray<int> frames,
        bool rendered,
        bool reverseDrawOrder = false) =>
        new()
        {
            Name = name,
            Frames = frames,
            IsRenderedByDefault = rendered,
            ReverseDrawOrder = reverseDrawOrder,
        };
}
```

- [ ] **Step 5: Write the full-geometry bake test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/FullGeometryTests.cs`:

```csharp
using Meziantou.Framework;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// Full geometry writes the assembly untouched, which is the whole point: no remap means no
/// frames dropped, so the nock/bow draw, climb and the north facing all survive.
/// </summary>
public sealed class FullGeometryTests
{
    private static FullPath WriteLayer(FullPath directory, string name)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            SheetLayout.SourceWidth, SheetLayout.SourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        bitmap.Erase(new SKColor(0x20, 0x40, 0x60, 0xFF));

        var path = directory / (name + ".png");

        using var stream = File.Create(path.Value);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        data.SaveTo(stream);

        return path;
    }

    private static SheetRecipe Recipe(FullPath layer, SheetGeometry geometry) => new()
    {
        Name = "probe",
        Layers = [new(layer, IsSkin: false)],
        Geometry = geometry,
    };

    [Fact]
    public void Bake_WritesSourceGeometry_WhenTheRecipeAsksForFull()
    {
        using var root = TemporaryDirectory.Create();

        var layer = WriteLayer(root.FullPath, "body");
        var baked = RecipeBaker.Bake(Recipe(layer, SheetGeometry.Full));

        Assert.True(baked.IsSuccessful, $"bake failed with {baked.Error}");

        using var sheet = baked.Value;
        using var decoded = SKBitmap.Decode(sheet.GetBuffer().AsSpan(0, (int)sheet.Length).ToArray());

        Assert.Equal(SheetLayout.SourceWidth, decoded.Width);
        Assert.Equal(SheetLayout.SourceHeight, decoded.Height);
    }

    [Fact]
    public void Bake_WritesContractGeometry_WhenTheRecipeAsksForCurated()
    {
        using var root = TemporaryDirectory.Create();

        var layer = WriteLayer(root.FullPath, "body");
        var baked = RecipeBaker.Bake(Recipe(layer, SheetGeometry.Curated));

        Assert.True(baked.IsSuccessful, $"bake failed with {baked.Error}");

        using var sheet = baked.Value;
        using var decoded = SKBitmap.Decode(sheet.GetBuffer().AsSpan(0, (int)sheet.Length).ToArray());

        Assert.Equal(SheetLayout.OutputWidth, decoded.Width);
        Assert.Equal(SheetLayout.OutputHeight, decoded.Height);
    }

    /// <summary>Curated is the default, so an unset geometry cannot silently change the contract.</summary>
    [Fact]
    public void Geometry_DefaultsToCurated()
    {
        var recipe = new SheetRecipe { Name = "probe", Layers = [] };

        Assert.Equal(SheetGeometry.Curated, recipe.Geometry);
    }
}
```

- [ ] **Step 6: Run both test classes**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~GeneratorClipsTests|FullyQualifiedName~FullGeometryTests"`

Expected: PASS. `Finish` from Task 5 already branches on `SheetGeometry.Full`, so no production change should be needed here — if `FullGeometryTests` fails, the bug is in that branch.

- [ ] **Step 7: Full build and test, then commit**

```bash
git add src/TheOmenDen.PixelForge.Core/Spritesheets tests/TheOmenDen.PixelForge.Core.Tests
git commit -m "feat(core): transcribe the generator animation table and bake full geometry"
```

---

## Task 7: Manifests — clips.csv and sheets.csv

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Spritesheets/ClipIndexRow.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Spritesheets/ClipIndex.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Baking/BatchManifestRow.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Baking/BatchManifest.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Spritesheets/ClipIndexTests.cs`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/BatchManifestTests.cs`

**Interfaces:**
- Consumes: `GeneratorClips` (Task 6), `AssetSlot`/`AssetSlots` (Task 1), `AssetPartial` (Task 2), `SheetGeometry` (Task 5), `BakeFailure` (existing).
- Produces: `ClipIndexRow` (`sealed record`); `ClipIndex.FileName -> "clips.csv"`, `ClipIndex.Rows -> ImmutableArray<ClipIndexRow>`, `ClipIndex.WriteTo(FullPath) -> Result<int, BakeFailure>`; `BatchManifestRow` (`sealed record`); `BatchManifest.FileName -> "sheets.csv"`, `BatchManifest.NewRunId() -> Guid`, `BatchManifest.WriteTo(FullPath, Guid, IReadOnlyList<BatchManifestRow>) -> Result<int, BakeFailure>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Spritesheets/ClipIndexTests.cs`:

```csharp
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Spritesheets;

namespace TheOmenDen.PixelForge.Core.Tests.Spritesheets;

/// <summary>
/// A full-geometry sheet is 23x4 cells with nothing in-band saying which column is the bow draw.
/// This manifest is the difference between an atlas a consumer can load and one it has to be told
/// about out of band.
/// </summary>
public sealed class ClipIndexTests
{
    [Fact]
    public void Rows_CoverEveryClipOnEveryFacing()
    {
        var expected = 0;

        foreach (var clip in GeneratorClips.All)
        {
            expected += clip.FrameCount * GeneratorClips.Facings.Length;
        }

        Assert.Equal(expected, ClipIndex.Rows.Length);
    }

    [Fact]
    public void Rows_CarryThePlaybackOrderNotAscendingColumns()
    {
        var walkSouth = ClipIndex.Rows
            .AsSpan()
            .Where(static row => row.Clip == "walk" && row.Facing == "south")
            .OrderBy(static row => row.FrameIndex)
            .ToArray();

        Assert.Equal([1, 2, 1, 0], walkSouth.Select(static row => row.SourceColumn).ToArray());
    }

    [Fact]
    public void Rows_MapFacingsOntoSourceRowsInOrder()
    {
        foreach (var row in ClipIndex.Rows)
        {
            Assert.Equal(GeneratorClips.Facings.IndexOf(row.Facing), row.SourceRow);
        }
    }

    [Fact]
    public void Rows_CarryTheAuthoredFrameDuration()
        => Assert.All(ClipIndex.Rows, row => Assert.Equal(GeneratorClips.FrameDurationMilliseconds, row.FrameDurationMs));

    [Fact]
    public void WriteTo_WritesTheManifestAndReportsTheRowCount()
    {
        using var root = TemporaryDirectory.Create();

        var written = ClipIndex.WriteTo(root.FullPath);

        Assert.True(written.IsSuccessful, $"write failed with {written.Error}");
        Assert.Equal(ClipIndex.Rows.Length, written.Value);
        Assert.True(File.Exists((root.FullPath / ClipIndex.FileName).Value));
    }

    [Fact]
    public void WriteTo_ReportsOutputDirectoryUnavailable_WhenTheFolderIsAbsent()
    {
        using var root = TemporaryDirectory.Create();

        var result = ClipIndex.WriteTo(root.FullPath / "absent");

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }
}
```

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/BatchManifestTests.cs`:

```csharp
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// At 168 files a run's filenames are not a usable index. This manifest is what maps a baked
/// sheet back to the partials and tone that produced it.
/// </summary>
public sealed class BatchManifestTests
{
    private static BatchManifestRow Row(string name) => new()
    {
        Name = name,
        File = name + ".webp",
        Geometry = nameof(SheetGeometry.Curated),
        Tone = "Tone 3",
        Bottom = "bottom1",
        Top = "top11",
        Head = "head1",
        Hair = "hair15c3",
        Hat = string.Empty,
    };

    /// <summary>
    /// UUIDv7, not v4: run ids are stamped into the manifest and the log, and v7's leading
    /// timestamp makes them sort chronologically instead of scattering.
    /// </summary>
    [Fact]
    public void NewRunId_IsAVersion7Uuid() => Assert.Equal(7, BatchManifest.NewRunId().Version);

    [Fact]
    public void NewRunId_OrdersLaterIdsAfterEarlierOnes()
    {
        var first = BatchManifest.NewRunId();
        var second = BatchManifest.NewRunId();

        Assert.True(string.CompareOrdinal(first.ToString("D"), second.ToString("D")) <= 0);
    }

    [Fact]
    public void WriteTo_RecordsEveryRowAgainstTheRunId()
    {
        using var root = TemporaryDirectory.Create();

        var runId = BatchManifest.NewRunId();
        var written = BatchManifest.WriteTo(root.FullPath, runId, [Row("a"), Row("b")]);

        Assert.True(written.IsSuccessful, $"write failed with {written.Error}");
        Assert.Equal(2, written.Value);

        var text = File.ReadAllText((root.FullPath / BatchManifest.FileName).Value);

        Assert.Contains(runId.ToString("D"), text, StringComparison.Ordinal);
        Assert.Contains("top11", text, StringComparison.Ordinal);
        Assert.Contains("hair15c3", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteTo_ReportsOutputDirectoryUnavailable_WhenTheFolderIsAbsent()
    {
        using var root = TemporaryDirectory.Create();

        var result = BatchManifest.WriteTo(root.FullPath / "absent", BatchManifest.NewRunId(), [Row("a")]);

        Assert.False(result.IsSuccessful);
        Assert.Equal(BakeFailure.OutputDirectoryUnavailable, result.Error);
    }

    /// <summary>An empty slot must be an empty cell, not the string "null".</summary>
    [Fact]
    public void WriteTo_LeavesUnusedSlotsBlank()
    {
        using var root = TemporaryDirectory.Create();

        BatchManifest.WriteTo(root.FullPath, BatchManifest.NewRunId(), [Row("a")]);

        var text = File.ReadAllText((root.FullPath / BatchManifest.FileName).Value);

        Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~ClipIndexTests|FullyQualifiedName~BatchManifestTests"`

Expected: build failure — the manifest types do not exist.

- [ ] **Step 3: Create `ClipIndexRow.cs`**

```csharp
namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>One row of <c>clips.csv</c>: a single frame of a single clip on a single facing.</summary>
/// <remarks>
/// Fully denormalised on purpose. A consumer reading this file should not have to know that
/// facings map onto source rows in a fixed order, or that a frame index is not the same thing as
/// a source column — both are stated per row.
/// </remarks>
public sealed record ClipIndexRow
{
    /// <summary>Snake-cased animation name, e.g. <c>nock_and_bow</c>.</summary>
    public required string Clip { get; init; }

    /// <summary>One of <see cref="GeneratorClips.Facings"/>.</summary>
    public required string Facing { get; init; }

    /// <summary>Row of the sheet this facing occupies, 0-3.</summary>
    public required int SourceRow { get; init; }

    /// <summary>Position within the clip's playback, from 0.</summary>
    public required int FrameIndex { get; init; }

    /// <summary>Column of the sheet to draw for this frame. Repeats where the animation does.</summary>
    public required int SourceColumn { get; init; }

    /// <summary>Cell edge in pixels.</summary>
    public required int CellSize { get; init; }

    /// <summary>How long this frame is held, in milliseconds.</summary>
    public required int FrameDurationMs { get; init; }

    /// <summary>Whether the generator composites this clip's layers back to front.</summary>
    public required bool ReverseDrawOrder { get; init; }
}
```

- [ ] **Step 4: Create `ClipIndex.cs`**

Model it on the existing `SheetIndex`: same `CsvWriter` usage, same `Result<int, BakeFailure>` shape, same `IOException`/`UnauthorizedAccessException` filter.

```csharp
using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Diagnostics;
using CsvHelper;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// The manifest that makes a <see cref="SheetGeometry.Full"/> sheet self-describing.
/// </summary>
/// <remarks>
/// <para>
/// The full-geometry counterpart of <see cref="SheetIndex"/>. It describes the source sheet rather
/// than a remap of it, so it carries all twelve generator animations on all four facings —
/// including the ones the curated contract drops.
/// </para>
/// <para>
/// Derived from <see cref="GeneratorClips"/> rather than restated, so the manifest cannot drift
/// from the table the bake is built on.
/// </para>
/// </remarks>
public static class ClipIndex
{
    /// <summary>Name of the manifest written beside full-geometry sheets.</summary>
    public const string FileName = "clips.csv";

    /// <summary>Every clip, facing and frame, in declaration order.</summary>
    public static ImmutableArray<ClipIndexRow> Rows { get; } = Build();

    private static ImmutableArray<ClipIndexRow> Build()
    {
        var rows = ImmutableArray.CreateBuilder<ClipIndexRow>();

        foreach (var clip in GeneratorClips.All)
        {
            for (var facing = 0; facing < GeneratorClips.Facings.Length; facing++)
            {
                for (var frame = 0; frame < clip.Frames.Length; frame++)
                {
                    rows.Add(new()
                    {
                        Clip = clip.Name,
                        Facing = GeneratorClips.Facings[facing],
                        SourceRow = facing,
                        FrameIndex = frame,
                        SourceColumn = clip.Frames[frame],
                        CellSize = SheetLayout.CellSize,
                        FrameDurationMs = GeneratorClips.FrameDurationMilliseconds,
                        ReverseDrawOrder = clip.ReverseDrawOrder,
                    });
                }
            }
        }

        return rows.ToImmutable();
    }

    /// <summary>Writes the manifest and reports how many rows landed.</summary>
    public static int Write(TextWriter writer)
    {
        Guard.IsNotNull(writer);

        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        csv.WriteRecords(Rows);
        csv.Flush();

        return Rows.Length;
    }

    /// <summary>Writes <c>clips.csv</c> into an export directory.</summary>
    /// <returns>
    /// The row count, or <see cref="BakeFailure.OutputDirectoryUnavailable"/> when the folder is
    /// not there, or <see cref="BakeFailure.OutputWriteFailed"/> when it cannot be written.
    /// </returns>
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

- [ ] **Step 5: Create `BatchManifestRow.cs` and `BatchManifest.cs`**

`BatchManifestRow` carries one property per slot, all defaulting to `string.Empty` so an unused slot writes a blank cell:

```csharp
namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One row of <c>sheets.csv</c>: an output file and the partials and tone that produced it.
/// </summary>
/// <remarks>
/// One column per <see cref="Catalog.AssetSlot"/> rather than a packed string, so the file is
/// filterable in a spreadsheet — "every sheet wearing hat4" is a column filter, not a text search.
/// Unused slots default to <see cref="string.Empty"/> and write a blank cell.
/// </remarks>
public sealed record BatchManifestRow
{
    /// <summary>The sheet's stem, without extension.</summary>
    public required string Name { get; init; }

    /// <summary>The file written, including extension.</summary>
    public required string File { get; init; }

    /// <summary>Which geometry was written — see <see cref="SheetGeometry"/>.</summary>
    public required string Geometry { get; init; }

    /// <summary>Name of the skin ramp applied, or blank when the sheet carries no skin.</summary>
    public string Tone { get; init; } = string.Empty;

    /// <summary>Stem of the shadow partial, or blank.</summary>
    public string Shadow { get; init; } = string.Empty;

    /// <summary>Stem of the back-extra partial, or blank.</summary>
    public string BackExtra { get; init; } = string.Empty;

    /// <summary>Stem of the back-hair partial, or blank.</summary>
    public string BackHair { get; init; } = string.Empty;

    /// <summary>Stem of the bottom partial.</summary>
    public string Bottom { get; init; } = string.Empty;

    /// <summary>Stem of the top partial.</summary>
    public string Top { get; init; } = string.Empty;

    /// <summary>Stem of the head partial.</summary>
    public string Head { get; init; } = string.Empty;

    /// <summary>Stem of the hair partial, or blank.</summary>
    public string Hair { get; init; } = string.Empty;

    /// <summary>Stem of the front-extra partial, or blank.</summary>
    public string FrontExtra { get; init; } = string.Empty;

    /// <summary>Stem of the hat partial, or blank.</summary>
    public string Hat { get; init; } = string.Empty;

    /// <summary>Stem of the weapon partial, or blank.</summary>
    public string Weapon { get; init; } = string.Empty;
}
```

`BatchManifest` mirrors `ClipIndex`'s write shape, prepending a `RunId` column. Write it by projecting each row into an anonymous-free wrapper record `BatchManifestRecord` that carries `RunId` plus the row's members, or simply call `csv.WriteField` per column — either is fine, but keep the run id first.

```csharp
    /// <summary>
    /// A fresh identifier for one batch run.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.CreateVersion7()"/>, never <see cref="Guid.NewGuid"/>. A v7 UUID leads with
    /// a millisecond timestamp, so run ids sort chronologically in the manifest and in the log;
    /// v4 is uniformly random and scatters.
    /// </remarks>
    public static Guid NewRunId() => Guid.CreateVersion7();
```

- [ ] **Step 6: Run the tests, full build, commit**

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
```

```bash
git add src/TheOmenDen.PixelForge.Core tests/TheOmenDen.PixelForge.Core.Tests
git commit -m "feat(core): add the clip and batch manifests"
```

---

## Task 8: Cross-product planning

**Files:**
- Create: `src/TheOmenDen.PixelForge.Core/Baking/SlotSelection.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Baking/PlanFailure.cs`
- Create: `src/TheOmenDen.PixelForge.Core/Baking/BatchPlan.cs`
- Modify: `Directory.Packages.props`, `src/TheOmenDen.PixelForge.Core/TheOmenDen.PixelForge.Core.csproj`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/BatchPlanTests.cs`

**Interfaces:**
- Consumes: `AssetSlot`, `AssetSlots.IsRequired`, `AssetSlots.IsSkinBearing`, `AssetSlots.DrawOrder` (Task 1); `AssetPartial` (Task 2); `AssetLayer`, `SheetGeometry`, `SheetRecipe` (Task 5); `SkinRamp`, `SkinRamps` (existing).
- Produces: `SlotSelection` (`sealed record` with `AssetSlot Slot`, `ImmutableArray<Optional<AssetPartial>> Choices`); `PlanFailure` enum; `BatchPlan.Expand(ImmutableArray<SlotSelection>, ImmutableArray<SkinRamp>, SheetGeometry) -> Result<ImmutableArray<SheetRecipe>, PlanFailure>`; `BatchPlan.Count(ImmutableArray<SlotSelection>, ImmutableArray<SkinRamp>) -> long`; `BatchPlan.StemFor(IReadOnlyList<AssetPartial>, Optional<SkinRamp>) -> string`.

**The rule that is easy to get wrong:** the tone axis multiplies a combination **only when that combination actually contains a skin-bearing partial**. Selecting hair alone must produce one sheet per hairstyle, not one per hairstyle per tone — that is exactly the Corvus two-texture contract, and getting it wrong turns 9 hair sheets into 63 identical ones.

`Count` is used for the live planned-count label and must agree with `Expand(...).Length` exactly. It returns `long` because a careless selection genuinely overflows `int`.

- [ ] **Step 1: Add the slug package**

In `Directory.Packages.props`, inside the Meziantou `ItemGroup`:

```xml
    <PackageVersion Include="Meziantou.Framework.Slug" Version="1.0.9" />
```

In `src/TheOmenDen.PixelForge.Core/TheOmenDen.PixelForge.Core.csproj`, beside the other Meziantou references:

```xml
    <PackageReference Include="Meziantou.Framework.Slug" />
```

No `Version=` attribute — CPM makes that a restore error. This replaces hand-rolled sanitisation of ramp names for the filename's tone segment.

- [ ] **Step 2: Write the failing test**

Create `tests/TheOmenDen.PixelForge.Core.Tests/Baking/BatchPlanTests.cs`:

```csharp
using System.Collections.Immutable;
using DotNext;
using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;
using TheOmenDen.PixelForge.Core.Catalog;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Tests.Baking;

/// <summary>
/// The planner turns a per-slot selection into recipes. Its subtle rule is the tone axis: it
/// multiplies a combination only when that combination actually carries skin, so selecting hair
/// alone yields one sheet per style rather than one per style per tone.
/// </summary>
public sealed class BatchPlanTests
{
    private static AssetPartial Partial(AssetSlot slot, string baseName, int variant = 0) => new()
    {
        Slot = slot,
        Pack = ElementsPack.Core,
        Base = baseName,
        Variant = variant,
        Path = FullPath.FromPath(Path.Combine(Path.GetTempPath(), $"{baseName}.png")),
    };

    private static SlotSelection Selection(AssetSlot slot, params ReadOnlySpan<AssetPartial> partials)
    {
        var choices = ImmutableArray.CreateBuilder<Optional<AssetPartial>>(partials.Length);

        foreach (var partial in partials)
        {
            choices.Add(partial);
        }

        return new() { Slot = slot, Choices = choices.ToImmutable() };
    }

    /// <summary>A selection that also offers "no piece in this slot".</summary>
    private static SlotSelection WithNone(AssetSlot slot, params ReadOnlySpan<AssetPartial> partials)
    {
        var choices = ImmutableArray.CreateBuilder<Optional<AssetPartial>>(partials.Length + 1);

        choices.Add(Optional<AssetPartial>.None);

        foreach (var partial in partials)
        {
            choices.Add(partial);
        }

        return new() { Slot = slot, Choices = choices.ToImmutable() };
    }

    private static ImmutableArray<SlotSelection> Body() =>
    [
        Selection(AssetSlot.Bottom, Partial(AssetSlot.Bottom, "bottom1")),
        Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11")),
        Selection(AssetSlot.Head, Partial(AssetSlot.Head, "head1")),
    ];

    private static ImmutableArray<SheetRecipe> ExpandOrFail(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones)
    {
        var result = BatchPlan.Expand(selections, tones, SheetGeometry.Curated);

        Assert.True(result.IsSuccessful, $"expand failed with {result.Error}");

        return result.Value;
    }

    [Fact]
    public void Expand_MultipliesEveryAxis()
    {
        ImmutableArray<SlotSelection> selections =
        [
            Selection(AssetSlot.Bottom, Partial(AssetSlot.Bottom, "bottom1"), Partial(AssetSlot.Bottom, "bottom9")),
            Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11"), Partial(AssetSlot.Top, "top15"), Partial(AssetSlot.Top, "top23")),
            Selection(AssetSlot.Head, Partial(AssetSlot.Head, "head1")),
            Selection(AssetSlot.Hair, Partial(AssetSlot.Hair, "hair1"), Partial(AssetSlot.Hair, "hair7"),
                Partial(AssetSlot.Hair, "hair15"), Partial(AssetSlot.Hair, "hair24")),
        ];

        var recipes = ExpandOrFail(selections, SkinRamps.All);

        // 2 bottoms x 3 tops x 1 head x 4 hair x 7 tones
        Assert.Equal(168, recipes.Length);
    }

    /// <summary>The live planned-count label must not be able to disagree with the run.</summary>
    [Fact]
    public void Count_AgreesWithExpand()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body(),
            WithNone(AssetSlot.Hat, Partial(AssetSlot.Hat, "hat4")),
        ];

        var recipes = ExpandOrFail(selections, SkinRamps.All);

        Assert.Equal(recipes.Length, BatchPlan.Count(selections, SkinRamps.All));
    }

    /// <summary>
    /// The Corvus contract in one assertion: hair alone is nine sheets, not sixty-three.
    /// </summary>
    [Fact]
    public void Expand_DoesNotApplyTheToneAxis_WhenNothingSelectedCarriesSkin()
    {
        var selections = ImmutableArray.Create(Selection(
            AssetSlot.Hair,
            Partial(AssetSlot.Hair, "hair1"),
            Partial(AssetSlot.Hair, "hair7"),
            Partial(AssetSlot.Hair, "hair9")));

        var recipes = ExpandOrFail(selections, SkinRamps.All);

        Assert.Equal(3, recipes.Length);
        Assert.All(recipes, recipe => Assert.False(recipe.Tone.HasValue));
    }

    /// <summary>
    /// A mixed selection must not pay the tone axis on the skinless combinations either — the
    /// "no top" combination is one sheet, not seven identical ones.
    /// </summary>
    [Fact]
    public void Expand_AppliesTheToneAxisPerCombination()
    {
        ImmutableArray<SlotSelection> selections =
        [
            Selection(AssetSlot.Hair, Partial(AssetSlot.Hair, "hair1")),
            WithNone(AssetSlot.Top, Partial(AssetSlot.Top, "top11")),
        ];

        var recipes = BatchPlan.Expand(selections, SkinRamps.All, SheetGeometry.Curated).Value;

        // hair-only combination: 1 sheet. hair + top combination: 7 tones.
        Assert.Equal(8, recipes.Length);
        Assert.Single(recipes.AsSpan().Where(static recipe => !recipe.Tone.HasValue).ToArray());
    }

    [Fact]
    public void Expand_OrdersLayersByDrawOrder()
    {
        ImmutableArray<SlotSelection> selections =
        [
            Selection(AssetSlot.Hat, Partial(AssetSlot.Hat, "hat4")),
            Selection(AssetSlot.Bottom, Partial(AssetSlot.Bottom, "bottom1")),
            Selection(AssetSlot.Head, Partial(AssetSlot.Head, "head1")),
            Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11")),
        ];

        var recipe = ExpandOrFail(selections, [SkinRamps.Source])[0];

        Assert.Equal(4, recipe.Layers.Length);
        Assert.EndsWith("bottom1.png", recipe.Layers[0].Path.Value, StringComparison.Ordinal);
        Assert.EndsWith("top11.png", recipe.Layers[1].Path.Value, StringComparison.Ordinal);
        Assert.EndsWith("head1.png", recipe.Layers[2].Path.Value, StringComparison.Ordinal);
        Assert.EndsWith("hat4.png", recipe.Layers[3].Path.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_MarksOnlySkinBearingLayers()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body(),
            Selection(AssetSlot.Weapon, Partial(AssetSlot.Weapon, "bow1")),
        ];

        var recipe = ExpandOrFail(selections, [SkinRamps.Source])[0];

        Assert.True(recipe.Layers[0].IsSkin);   // bottom
        Assert.True(recipe.Layers[1].IsSkin);   // top
        Assert.True(recipe.Layers[2].IsSkin);   // head
        Assert.False(recipe.Layers[3].IsSkin);  // weapon keeps its wooden tan
    }

    [Fact]
    public void Expand_ReportsRequiredSlotEmpty_WhenTheBodyIsIncomplete()
    {
        var selections = ImmutableArray.Create(Selection(AssetSlot.Top, Partial(AssetSlot.Top, "top11")));

        var result = BatchPlan.Expand(selections, [SkinRamps.Source], SheetGeometry.Curated);

        Assert.False(result.IsSuccessful);
        Assert.Equal(PlanFailure.RequiredSlotEmpty, result.Error);
    }

    /// <summary>A required slot offering "(none)" is the same error, stated differently.</summary>
    [Fact]
    public void Expand_ReportsRequiredSlotEmpty_WhenARequiredSlotOffersNone()
    {
        ImmutableArray<SlotSelection> selections =
        [
            Selection(AssetSlot.Bottom, Partial(AssetSlot.Bottom, "bottom1")),
            WithNone(AssetSlot.Top, Partial(AssetSlot.Top, "top11")),
            Selection(AssetSlot.Head, Partial(AssetSlot.Head, "head1")),
        ];

        var result = BatchPlan.Expand(selections, [SkinRamps.Source], SheetGeometry.Curated);

        Assert.False(result.IsSuccessful);
        Assert.Equal(PlanFailure.RequiredSlotEmpty, result.Error);
    }

    [Fact]
    public void Expand_ReportsNothingSelected_WhenThereAreNoSelectionsAtAll()
    {
        var result = BatchPlan.Expand([], [SkinRamps.Source], SheetGeometry.Curated);

        Assert.False(result.IsSuccessful);
        Assert.Equal(PlanFailure.NothingSelected, result.Error);
    }

    [Fact]
    public void StemFor_JoinsSlotsInDrawOrderAndAppendsTheTone()
    {
        AssetPartial[] chosen =
        [
            Partial(AssetSlot.Bottom, "bottom1"),
            Partial(AssetSlot.Top, "top11"),
            Partial(AssetSlot.Head, "head1"),
            Partial(AssetSlot.Hair, "hair15", 3),
        ];

        var stem = BatchPlan.StemFor(chosen, SkinRamps.All[4]);

        Assert.Equal("bottom1_top11_head1_hair15c3_tone-4-green", stem);
    }

    /// <summary>
    /// The default tone is the ramp the art is already authored in, so naming it would put a
    /// redundant segment on the majority of files.
    /// </summary>
    [Fact]
    public void StemFor_OmitsTheToneSegment_ForTheSourceToneAndForNoTone()
    {
        AssetPartial[] chosen = [Partial(AssetSlot.Hair, "hair1")];

        Assert.Equal("hair1", BatchPlan.StemFor(chosen, SkinRamps.Source));
        Assert.Equal("hair1", BatchPlan.StemFor(chosen, Optional<SkinRamp>.None));
    }

    [Fact]
    public void Expand_ProducesDistinctNames()
    {
        ImmutableArray<SlotSelection> selections =
        [
            .. Body(),
            Selection(AssetSlot.Hair, Partial(AssetSlot.Hair, "hair1"), Partial(AssetSlot.Hair, "hair1", 2)),
        ];

        var recipes = ExpandOrFail(selections, SkinRamps.All);
        var names = recipes.AsSpan().Select(static recipe => recipe.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void Expand_StampsTheRequestedGeometryOnEveryRecipe()
    {
        var result = BatchPlan.Expand(Body(), [SkinRamps.Source], SheetGeometry.Full);

        Assert.All(result.Value, recipe => Assert.Equal(SheetGeometry.Full, recipe.Geometry));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~BatchPlanTests"`

Expected: build failure — `SlotSelection`, `PlanFailure` and `BatchPlan` do not exist.

- [ ] **Step 4: Create `PlanFailure.cs` and `SlotSelection.cs`**

```csharp
namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Why a selection cannot be turned into recipes. Every member describes a user's choice rather
/// than a bug, so these travel as <see cref="DotNext.Result{T, TError}"/> values.
/// <para>Numbering starts at 1 so <see langword="default"/> is never a real failure.</para>
/// </summary>
public enum PlanFailure
{
    /// <summary>No slot has anything ticked.</summary>
    NothingSelected = 1,

    /// <summary>
    /// A slot the generator marks non-optional — bottom, top or head — has no partial chosen, or
    /// offers "(none)" as a choice. A character without a head is not a sheet.
    /// </summary>
    RequiredSlotEmpty,
}
```

```csharp
using System.Collections.Immutable;
using DotNext;
using TheOmenDen.PixelForge.Core.Catalog;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// What the user ticked for one slot.
/// </summary>
/// <remarks>
/// <para>
/// A choice is <see cref="Optional{T}"/> so that "wear no hat" is a first-class alternative
/// alongside the hats themselves. Including <see cref="Optional{T}.None"/> in an optional slot is
/// how one run produces both a hatted and a hatless character; including it in a required slot is
/// <see cref="PlanFailure.RequiredSlotEmpty"/>.
/// </para>
/// <para>
/// Colour variants are ordinary choices here. The picker's per-slot "include colour variants"
/// toggle is what expands a ticked base into its <c>_cN</c> siblings before building this.
/// </para>
/// </remarks>
public sealed record SlotSelection
{
    /// <summary>Which slot these choices fill.</summary>
    public required AssetSlot Slot { get; init; }

    /// <summary>The chosen partials, plus <see cref="Optional{T}.None"/> to mean "leave empty".</summary>
    public required ImmutableArray<Optional<AssetPartial>> Choices { get; init; }
}
```

- [ ] **Step 5: Create `BatchPlan.cs`**

Key points for the implementer:

- Validate first: empty selections is `NothingSelected`; any required slot missing, empty, or offering `None` is `RequiredSlotEmpty`.
- Sort the selections by `Slot` (the enum value is the draw order) before iterating, so layers come out ordered without a second sort per combination.
- Walk the combinations with an odometer over the per-slot choice indices — neither ZLinq nor System.Linq ships a cartesian product over a variable number of sequences, and that absence is why this is hand-written.
- For each combination, gather the non-`None` partials. If any of them sits on a skin-bearing slot, emit one recipe per tone; otherwise emit exactly one with `Tone = Optional<SkinRamp>.None`.
- `Count` must mirror the same rule rather than assume `combinations * tones`.

```csharp
    /// <summary>
    /// Expands a per-slot selection into one recipe per combination, multiplied by the tone axis
    /// where a combination carries skin.
    /// </summary>
    /// <returns>
    /// The recipes, or <see cref="PlanFailure.NothingSelected"/> when nothing is ticked, or
    /// <see cref="PlanFailure.RequiredSlotEmpty"/> when bottom, top or head is unfilled.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The tone axis applies <em>per combination</em>, not to the run as a whole. A combination
    /// with no skin-bearing partial produces a single sheet with no tone: hair alone is nine
    /// sheets, not sixty-three, which is exactly the layered contract Corvus consumes.
    /// </para>
    /// <para>
    /// The odometer is hand-written because no available LINQ provider expresses a cartesian
    /// product over a variable number of sequences. It is the only such gap in this feature.
    /// </para>
    /// </remarks>
    public static Result<ImmutableArray<SheetRecipe>, PlanFailure> Expand(
        ImmutableArray<SlotSelection> selections,
        ImmutableArray<SkinRamp> tones,
        SheetGeometry geometry)
```

For the stem, use the slug package for the tone segment:

```csharp
    /// <summary>
    /// The output stem for one combination: each partial's <see cref="AssetPartial.Stem"/> joined
    /// in draw order, then the tone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tone segment is omitted for <see cref="SkinRamps.Source"/> and for no tone at all — the
    /// source ramp is what the art is already authored in, so naming it would add a redundant
    /// segment to most files. Other ramps are slugged with
    /// <see cref="Meziantou.Framework.Slug"/> rather than hand-sanitised, which also covers
    /// user-created ramps whose names are arbitrary text.
    /// </para>
    /// <para>
    /// Ten slots plus a tone can approach <c>MAX_PATH</c> under a deep output directory. That is
    /// accepted: <see cref="BatchManifest"/> is the authoritative index, and a write failure
    /// surfaces as <see cref="BakeFailure.OutputWriteFailed"/> rather than silent truncation.
    /// </para>
    /// </remarks>
    public static string StemFor(IReadOnlyList<AssetPartial> chosen, Optional<SkinRamp> tone)
```

- [ ] **Step 6: Run the tests, full build, commit**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj`

Expected: PASS. If `StemFor_JoinsSlotsInDrawOrderAndAppendsTheTone` produces `tone-4-green-` or `tone4green`, adjust `SlugOptions.Separator` to `"-"` and `CanEndWithSeparator` to `false`.

```bash
git add Directory.Packages.props src/TheOmenDen.PixelForge.Core tests/TheOmenDen.PixelForge.Core.Tests
git commit -m "feat(core): expand per-slot selections into a batch of recipes"
```

---

## Task 9: Keep the Roost contract reproducible

**Files:**
- Modify: `src/TheOmenDen.PixelForge.Core/Baking/RoostSheets.cs`
- Modify: `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RoostSheetsTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-8.
- Produces: `RoostSheets.Bodies(SourcePacks) -> ImmutableArray<SheetRecipe>` (7, named `body-01..07`), `RoostSheets.Hair(SourcePacks) -> ImmutableArray<SheetRecipe>` (9, named `hair-01..09`), `RoostSheets.All(SourcePacks)` (16), `RoostSheets.Selection(AssetCatalog) -> ImmutableArray<SlotSelection>`.

> `RoostSheets` keeps its **explicit names** rather than going through `BatchPlan.StemFor`. Corvus's `CosmeticDescriptor` registry names `body-01.webp` and `hair-01.webp` literally; a generated stem would silently rename the deliverable. `Selection` exists only to pre-tick the picker for exploration and is not the path that produces the shipped files.

- [ ] **Step 1: Write the failing test**

Add to `tests/TheOmenDen.PixelForge.Core.Tests/Baking/RoostSheetsTests.cs` (removing any surviving `Flattened` cases):

```csharp
    /// <summary>
    /// The spec-079 filenames are literals in Corvus's cosmetic registry, so they are a contract,
    /// not a naming convention. Generating them from the picker's stem rule would rename the
    /// shipped deliverable without a compiler anywhere to notice.
    /// </summary>
    [Fact]
    public void All_KeepsTheContractFilenames()
    {
        var packs = Packs();
        var names = RoostSheets.All(packs).AsSpan().Select(static recipe => recipe.Name).ToArray();

        Assert.Equal(16, names.Length);
        Assert.Contains("body-01", names);
        Assert.Contains("body-07", names);
        Assert.Contains("hair-01", names);
        Assert.Contains("hair-09", names);
    }

    [Fact]
    public void Bodies_CarryOneRecipePerToneInOrder()
    {
        var bodies = RoostSheets.Bodies(Packs());

        Assert.Equal(SkinRamps.All.Length, bodies.Length);

        for (var i = 0; i < bodies.Length; i++)
        {
            Assert.Equal($"body-{i + 1:00}", bodies[i].Name);
            Assert.True(bodies[i].Tone.TryGet(out var tone));
            Assert.Equal(SkinRamps.All[i].Name, tone.Name);
        }
    }

    /// <summary>Hair keeps its authored colour and never carries a tone.</summary>
    [Fact]
    public void Hair_CarriesNoToneAndNoSkinLayers()
    {
        foreach (var recipe in RoostSheets.Hair(Packs()))
        {
            Assert.False(recipe.Tone.HasValue);
            Assert.All(recipe.Layers, layer => Assert.False(layer.IsSkin));
        }
    }

    [Fact]
    public void Bodies_MarkEveryLayerAsSkinBearing()
    {
        foreach (var recipe in RoostSheets.Bodies(Packs()))
        {
            Assert.All(recipe.Layers, layer => Assert.True(layer.IsSkin));
        }
    }

    [Fact]
    public void All_BakesToTheCuratedGeometry()
        => Assert.All(RoostSheets.All(Packs()), recipe => Assert.Equal(SheetGeometry.Curated, recipe.Geometry));
```

Reuse whatever `Packs()` helper the existing file already has; if there is none, build a `SourcePacks` pointing at three non-existent directories — these assertions never touch the disk.

- [ ] **Step 2: Run to verify it fails, then implement**

Run: `dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj --filter "FullyQualifiedName~RoostSheetsTests"`

Then add `Selection` to `RoostSheets`:

```csharp
    /// <summary>
    /// The spec-079 art as a picker selection, so the shipped set can be loaded, inspected and
    /// varied from the batch page.
    /// </summary>
    /// <returns>
    /// One <see cref="SlotSelection"/> per filled slot. Partials the catalogue does not hold are
    /// dropped rather than faulted — a pack pointed somewhere wrong should show an obviously short
    /// selection, not an error dialog.
    /// </returns>
    /// <remarks>
    /// This is a convenience for exploration. The files Corvus consumes come from
    /// <see cref="All"/>, which names them explicitly; a stem generated by
    /// <see cref="BatchPlan.StemFor"/> would not match the registry.
    /// </remarks>
    public static ImmutableArray<SlotSelection> Selection(AssetCatalog catalog)
```

- [ ] **Step 3: Run the full suite and commit**

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
```

```bash
git add src/TheOmenDen.PixelForge.Core/Baking/RoostSheets.cs tests/TheOmenDen.PixelForge.Core.Tests/Baking/RoostSheetsTests.cs
git commit -m "feat(core): keep the spec-079 set reproducible beside the picker"
```

---

## Task 10: Catalogue service and picker view models

**Files:**
- Create: `src/TheOmenDen.PixelForge/Services/CatalogService.cs`
- Create: `src/TheOmenDen.PixelForge/ViewModels/PartialSelectionItem.cs`
- Create: `src/TheOmenDen.PixelForge/ViewModels/SlotGroupViewModel.cs`
- Modify: `src/TheOmenDen.PixelForge/ViewModels/ExportMode.cs`
- Modify: `src/TheOmenDen.PixelForge/ViewModels/BatchExportViewModel.cs`
- Modify: `src/TheOmenDen.PixelForge/App.xaml.cs` (register `CatalogService`)
- Delete: `src/TheOmenDen.PixelForge/ViewModels/SheetSelectionItem.cs`

**Interfaces:**
- Consumes: `AssetCatalog`, `AssetSlot`, `AssetSlots`, `AssetPartial`, `BatchPlan`, `SlotSelection`, `SheetGeometry`, `RoostSheets`, `BatchManifest`, `ClipIndex`, `SheetIndex`, `BatchBaker`.
- Produces: `CatalogService` (holds `Optional<AssetCatalog> Current`, rescans on `SourcePackService.Changed`, raises `Changed`); `PartialSelectionItem : ObservableObject` (`AssetPartial Partial`, `string Name`, `string AutomationId`, `bool IsSelected`); `SlotGroupViewModel : ObservableObject` (`AssetSlot Slot`, `string Header`, `bool IncludeVariants`, `bool AllowNone`, `IAdvancedCollectionView Items`, `string Filter`, `int SelectedCount`, `SlotSelection ToSelection(AssetCatalog)`); `ExportMode { Curated, Full, Both }`.

> `ExportMode`'s members must stay declared in the order the page lists them — `BatchExportViewModel.Mode` casts the Segmented's index straight to the enum, and the existing UI test `A mode picked right after navigation survives` depends on that.

- [ ] **Step 1: Repurpose `ExportMode`**

```csharp
namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// Which geometry a batch writes. Members are declared in the order the page's
/// <c>Segmented</c> lists them, so the control's index <em>is</em> the enum value and no lookup
/// table has to be kept in step with the XAML by hand.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the previous Layered/Flattened/Both meaning. Layering is no longer a mode: it
/// falls out of what is selected, because the recolour now runs per layer. Tick head, top and
/// bottom for a body sheet; tick hair alone for a hair sheet — which is exactly the two-texture
/// contract Corvus consumes.
/// </para>
/// </remarks>
public enum ExportMode
{
    /// <summary>The 240x1152 contract sheet only.</summary>
    Curated,

    /// <summary>The raw 1104x192 source geometry only.</summary>
    Full,

    /// <summary>Both geometries, one file each per combination.</summary>
    Both,
}
```

- [ ] **Step 2: Create `CatalogService`**

Mirror `SourcePackService`'s shape — concrete class, no interface, registered directly, `ILogger<T>` injected, `Optional<T>` for "not available yet".

```csharp
/// <summary>
/// Holds the scanned asset catalogue and keeps it in step with the configured pack paths.
/// </summary>
/// <remarks>
/// <para>
/// Rescans whenever <see cref="SourcePackService.Changed"/> fires. The scan reads directory
/// entries only — no image is decoded — so re-running it on a path change costs nothing worth
/// deferring, and a stale catalogue would silently plan bakes against files that moved.
/// </para>
/// <para>
/// No interface: there is one implementation and nothing mocks it.
/// </para>
/// </remarks>
public sealed class CatalogService
{
    /// <summary>The catalogue, or <see cref="Optional{T}.None"/> until the packs resolve.</summary>
    public Optional<AssetCatalog> Current { get; private set; }

    /// <summary>Raised after a rescan, so pages can rebuild their lists.</summary>
    public event EventHandler? Changed;
}
```

A failed scan logs at warning with the `CatalogFailure` as a structured property and leaves `Current` as `None`:

```csharp
        logger.LogWarning("Asset catalogue scan failed: {Failure}", result.Error);
```

Register in `App.xaml.cs` beside `SourcePackService`, as a singleton, and call its initial scan after `SourcePackService.Load()`.

- [ ] **Step 3: Create `PartialSelectionItem` and `SlotGroupViewModel`**

`PartialSelectionItem` replaces `SheetSelectionItem`. `AutomationId` must be stable and unique across the whole page, since ten slots now share it — use `$"Sel{slot}_{partial.Stem}"`, e.g. `SelTop_top11`.

`SlotGroupViewModel` owns one slot's list. Points that matter:

- `Items` is an `AdvancedCollectionView` over the slot's `PartialSelectionItem`s, so the search box filters without rebuilding collections.
- Sorting uses **`SortDescription<T>`, the generic form**. The string-property overload resolves the property reflectively and is not trim-safe, and Release publishes `PublishTrimmed=true`. Simpler still: feed the list in catalogue order (already sorted by `AssetSortKey`) and use `AdvancedCollectionView` for filtering only.
- `Filter` setter re-applies `Items.Filter` and refreshes.
- `IncludeVariants` does **not** change the list — the list always shows bases. It changes `ToSelection`, which expands each ticked base into `catalog.VariantsOf(slot, base)` when set.
- `AllowNone` is `!AssetSlots.IsRequired(Slot)`; `ToSelection` prepends `Optional<AssetPartial>.None` when set and at least one item is ticked.

```csharp
    /// <summary>
    /// What this slot contributes to a plan.
    /// </summary>
    /// <remarks>
    /// The list always shows base files; <see cref="IncludeVariants"/> is applied here rather than
    /// by growing the list, so ticking <c>hair15</c> can mean one file or eight without the row
    /// count changing under the user mid-selection.
    /// </remarks>
    public SlotSelection ToSelection(AssetCatalog catalog)
```

- [ ] **Step 4: Rewrite `BatchExportViewModel`**

Keep everything that already works — `OutputFolder`, `BrowseOutputAsync`, `IsExporting`, `ProgressValue`/`ProgressText`, `Notified`/`StatusNotice`, the `SelectedModeIndex`/`Mode` pair with its deferred-initialisation comment, and the `_reloadPending` guard on `Reload`. Replace only the selection model.

- `Groups` is an `ObservableCollection<SlotGroupViewModel>`, one per `AssetSlots.DrawOrder`, built from `CatalogService.Current`.
- `Tones` is an `ObservableCollection<ToneSelectionItem>` over `SkinRamps.All` plus any custom ramps from `RampStore`.
- `PlannedCount` calls `BatchPlan.Count(...)`, returning `long`. Cap the label at a readable form; `PlannedLabel` in the page already handles singular/plural and takes an `int` — widen it to `long`.
- `CanExport` additionally requires `PlannedCount > 0`.
- `ExportAsync` plans once, then for `ExportMode.Both` runs the same selection twice with the two geometries and concatenates. After the run it writes `SheetIndex` (curated), `ClipIndex` (full), and `BatchManifest` with a fresh `BatchManifest.NewRunId()`.
- A planned count above `WarnThreshold` (1000) posts a `StatusLevel.Warning` notice **and still runs** — a large deliberate run is legitimate.

```csharp
    /// <summary>
    /// Above this many files the run is announced rather than blocked. A four-figure batch is a
    /// legitimate thing to ask for; silently starting one is not.
    /// </summary>
    private const int WarnThreshold = 1000;
```

Progress marking: the old `Mark` matched a flattened name against a row prefix. That heuristic goes — a stem now contains every slot, so match a `PartialSelectionItem` by testing whether the recipe's stem contains its `Stem` as a whole underscore-delimited segment.

- [ ] **Step 5: Build and commit**

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet test tests/TheOmenDen.PixelForge.Core.Tests/TheOmenDen.PixelForge.Core.Tests.csproj
```

The app project has no unit tests; the build must be clean and `ui-tests.ps1` covers it in Task 13.

```bash
git add -A src/TheOmenDen.PixelForge
git commit -m "feat(app): back the batch page with the asset catalogue"
```

---

## Task 11: The per-slot picker page

**Files:**
- Modify: `src/TheOmenDen.PixelForge/Views/PipelinePage.xaml`
- Modify: `src/TheOmenDen.PixelForge/Views/PipelinePage.xaml.cs`

**Interfaces:**
- Consumes: `BatchExportViewModel`, `SlotGroupViewModel`, `PartialSelectionItem`, `ExportMode` (Task 10).
- Produces: automation ids `SlotExpander_<Slot>`, `SlotFilter_<Slot>`, `SlotVariants_<Slot>`, `Sel<Slot>_<Stem>`, `ToneList`, `BtnLoadRoostSelection`, plus the existing `ExportModeSegmented`, `ExportModeDescription`, `PlannedCountText`, `BtnExport`, `BtnCancelExport`, `OutputFolderText`, `BtnBrowseOutput`, `ExportProgress`, `ExportStatusBar`, `PacksMissingInfoBar`.

**Do not rename the existing automation ids.** `ui-tests.ps1` already asserts on them, and a rename turns a passing suite red for no reason.

- [ ] **Step 1: Replace the selection region**

The two `ListView`s and the `GridSplitter` between them go. In their place, a `ScrollViewer` of ten `SettingsExpander`s plus a tone panel. Per the catalogue sample, `SettingsExpander` takes `ItemsSource` + `ItemTemplate` and hosts header content:

```xml
<controls:SettingsExpander
    AutomationProperties.AutomationId="SlotExpander_Top"
    Description="{x:Bind TopGroup.Description, Mode=OneWay}"
    Header="Top"
    ItemsSource="{x:Bind TopGroup.Items, Mode=OneWay}">
    <StackPanel Orientation="Horizontal" Spacing="8">
        <AutoSuggestBox
            Width="180"
            AutomationProperties.AutomationId="SlotFilter_Top"
            AutomationProperties.Name="Filter top pieces"
            PlaceholderText="Filter"
            QueryIcon="Find"
            Text="{x:Bind TopGroup.Filter, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
        <ToggleSwitch
            AutomationProperties.AutomationId="SlotVariants_Top"
            AutomationProperties.Name="Include colour variants for top"
            IsOn="{x:Bind TopGroup.IncludeVariants, Mode=TwoWay}"
            OffContent="Base only"
            OnContent="All colours" />
    </StackPanel>
    <controls:SettingsExpander.ItemTemplate>
        <DataTemplate x:DataType="vm:PartialSelectionItem">
            <controls:SettingsCard Header="{x:Bind Name}">
                <CheckBox
                    AutomationProperties.AutomationId="{x:Bind AutomationId}"
                    AutomationProperties.Name="{x:Bind Name}"
                    IsChecked="{x:Bind IsSelected, Mode=TwoWay}" />
            </controls:SettingsCard>
        </DataTemplate>
    </controls:SettingsExpander.ItemTemplate>
</controls:SettingsExpander>
```

Ten near-identical blocks is a lot of XAML. Prefer an `ItemsRepeater` over `ViewModel.Groups` with a single `DataTemplate`, binding `AutomationProperties.AutomationId` to a `SlotGroupViewModel.ExpanderAutomationId` property (`$"SlotExpander_{Slot}"`) so the ids stay addressable. **The `AutoSuggestBox` `Text` binding must keep `UpdateSourceTrigger=PropertyChanged`** or UIA `set-value` will not commit the filter.

`x:Bind` inside a `DataTemplate` requires `x:DataType`; add `xmlns:vm="using:TheOmenDen.PixelForge.ViewModels"` (already present) and `xmlns:catalog="using:TheOmenDen.PixelForge.Core.Catalog"` if a slot enum is bound.

- [ ] **Step 2: Add the tone panel and preset button**

Tones go in a `UniformGrid` of swatch toggles beside the slot list. Each swatch shows `SkinRamp.BaseTone`; label with `AutomationProperties.Name` set to the ramp name so the suite can address them, and give the container `AutomationProperties.AutomationId="ToneList"`.

The preset button sits next to Export:

```xml
<Button
    AutomationProperties.AutomationId="BtnLoadRoostSelection"
    Command="{x:Bind ViewModel.LoadRoostSelectionCommand}"
    Content="Roost set (079)" />
```

- [ ] **Step 3: Relabel the mode Segmented**

Keep the control, its `x:Name`, its automation id and the code-behind `_modeReady` gate exactly as they are — that gate is load-bearing and its comment explains why. Change only the three `SegmentedItem` contents to `Curated`, `Full`, `Both`, and update `ModeDescription`'s three constants in the view model:

- Curated: "The 240x1152 sheet Corvus consumes: 8 clips on 3 facings, north dropped."
- Full: "The raw 1104x192 source sheet: all 23 columns and 4 facings, keeping the bow draw, climb and north."
- Both: "Both geometries, one file each per combination."

- [ ] **Step 4: Check theming and layout**

- No hardcoded colours; tone swatches use the ramp's own colour as a `SolidColorBrush` on a `Border`, which is data, not theming — give the border a `{ThemeResource ControlStrokeColorDefaultBrush}` outline so a near-background swatch stays visible in both themes.
- Spacing on the 4px grid; `RowSpacing`/`ColumnSpacing`, no spacer elements.
- Typography styles only — `BodyStrongTextBlockStyle` for group headings, `CaptionTextBlockStyle` for counts.
- The page already handles its own DIP-accurate breakpoint pattern on `CanvasPage`; this page does not need one, but the slot list must scroll rather than clip: put the expanders in a `ScrollViewer` in the `*` row.

- [ ] **Step 5: Build and run the app**

```powershell
dotnet build TheOmenDen.PixelForge.slnx
dotnet run --project src/TheOmenDen.PixelForge
```

Navigate to Pipeline. Confirm the ten groups render, expand, filter and tick, and that the planned count moves. **Look at the window, not just the automation** — check Light, Dark and HighContrast.

- [ ] **Step 6: Commit**

```bash
git add src/TheOmenDen.PixelForge/Views
git commit -m "feat(app): replace the two sheet lists with a per-slot picker"
```

---

## Task 12: Composite preview

**Files:**
- Modify: `src/TheOmenDen.PixelForge.Core/Palettes/PalettePreview.cs`
- Modify: `src/TheOmenDen.PixelForge/ViewModels/BatchExportViewModel.cs`
- Modify: `src/TheOmenDen.PixelForge/Views/PipelinePage.xaml(.cs)`
- Test: `tests/TheOmenDen.PixelForge.Core.Tests/Palettes/PalettePreviewTests.cs` (extend)

**Interfaces:**
- Consumes: `RecipeBaker.AssembleLayers`, `SheetBaker.Curate`, `BatchPlan.Expand`.
- Produces: `PalettePreview.Create(SheetRecipe)` unchanged in shape but now accepting a full ten-slot recipe; automation id `CompositePreviewImage`.

`PalettePreview` already does exactly the right thing — it bakes once un-toned, caches the curated sheet, and applies only the substitution per render. Nothing about it assumes a three-layer body, so the work here is mostly wiring plus a test proving a ten-layer recipe previews.

- [ ] **Step 1: Add the test**

```csharp
    /// <summary>
    /// The preview is built from whatever recipe it is handed, so a full ten-slot character must
    /// render exactly as a bare body does — the cached sheet is un-toned either way.
    /// </summary>
    [Fact]
    public void Create_PreviewsAFullyEquippedRecipe()
```

Build the recipe from synthetic source-geometry PNGs the way `PerLayerRecolorTests.WriteLayer` does, one per slot, and assert `RenderIdleRow` returns a bitmap of `IdleRowWidth` x `IdleRowHeight`.

- [ ] **Step 2: Wire it into the view model**

Add a `PreviewSource` property and rebuild it, debounced, whenever the selection or tone changes: plan the current selection, take the **first** recipe, `PalettePreview.Create`, `RenderIdleRow`, convert to a `WriteableBitmap` the same way `PalettePage.xaml.cs` already does, and dispose the previous one.

Guard the cost: previewing rebuilds on every tick, so skip when `IsExporting`, and skip when the plan is empty. Do **not** preview every combination — one still is the whole point.

- [ ] **Step 3: Add the image to the page**

```xml
<Image
    AutomationProperties.AutomationId="CompositePreviewImage"
    AutomationProperties.Name="Composite preview"
    Source="{x:Bind ViewModel.PreviewSource, Mode=OneWay}"
    Stretch="None" />
```

`Stretch="None"` because `PalettePreview` already upscales nearest-neighbour — WinUI's `Image` has no interpolation-mode switch, so letting it scale would blur the pixel art.

- [ ] **Step 4: Build, test, look at it, commit**

```bash
git add -A src
git commit -m "feat(app): preview the composed character before running a batch"
```

---

## Task 13: UI automation

**Files:**
- Modify: `tests/ui-tests.ps1`

**Interfaces:**
- Consumes: every automation id from Tasks 11 and 12.

- [ ] **Step 1: Update the existing batch-page tests**

`Test-UI 'Batch export page has its controls'` and `'Export mode description and planned count follow the selection'` both assert on the old Layered/Flattened wording. Update the expected strings to the Curated/Full/Both descriptions from Task 11 Step 3. Leave `'A mode picked right after navigation survives'` structurally alone — it guards the `_modeReady` race and that race has not changed.

- [ ] **Step 2: Add the picker tests**

```powershell
Test-UI 'Pipeline: slot groups are present' {
    winapp ui invoke 'NavPipeline' -a $AppPid
    foreach ($slot in 'Shadow','BackExtra','BackHair','Bottom','Top','Head','Hair','FrontExtra','Hat','Weapon') {
        winapp ui wait-for "SlotExpander_$slot" -a $AppPid -t 3000
        if ($LASTEXITCODE -ne 0) { throw "missing slot expander for $slot" }
    }
}

Test-UI 'Pipeline: ticking a base changes the planned count' {
    winapp ui invoke 'SlotExpander_Hair' -a $AppPid
    $before = "$(winapp ui get-value 'PlannedCountText' -a $AppPid 2>&1)".Trim()
    winapp ui invoke 'SelHair_hair1' -a $AppPid
    $after = "$(winapp ui get-value 'PlannedCountText' -a $AppPid 2>&1)".Trim()
    if ($before -eq $after) { throw "planned count did not move: '$before'" }
    $global:LASTEXITCODE = 0
}

Test-UI 'Pipeline: the variants toggle multiplies the planned count' {
    $before = "$(winapp ui get-value 'PlannedCountText' -a $AppPid 2>&1)".Trim()
    winapp ui invoke 'SlotVariants_Hair' -a $AppPid
    $after = "$(winapp ui get-value 'PlannedCountText' -a $AppPid 2>&1)".Trim()
    if ($before -eq $after) { throw "variants toggle did not change the plan: '$before'" }
    $global:LASTEXITCODE = 0
}

Test-UI 'Pipeline: the slot filter narrows a list' {
    winapp ui set-value 'SlotFilter_Hair' 'hair2' -a $AppPid
    winapp ui wait-for 'SelHair_hair2' -a $AppPid -t 3000
}

Test-UI 'Pipeline: the Roost preset loads a selection' {
    winapp ui invoke 'BtnLoadRoostSelection' -a $AppPid
    $count = "$(winapp ui get-value 'PlannedCountText' -a $AppPid 2>&1)".Trim()
    if ($count -notmatch '\d') { throw "no planned count after loading the preset: '$count'" }
    $global:LASTEXITCODE = 0
}
```

- [ ] **Step 3: Run the suite twice**

```powershell
dotnet run --project src/TheOmenDen.PixelForge     # prints the PID
.\tests\ui-tests.ps1 -AppPid <PID>
```

**Run it a second time against a freshly launched app.** There is a known open issue where the app fail-fasts on a repeat run with nothing managed firing, so a single green run is not sufficient evidence.

Close the window rather than killing the process — `Serilog.Sinks.Async` buffers, and `Log.CloseAndFlush()` on window close is what persists the tail of the log.

- [ ] **Step 4: Look at the screenshots**

Open `tests/ui-results/`. UIA assertions pass while the app is visually broken — check for clipped expander headers, overlapping tone swatches, a preview that is blurred rather than nearest-neighbour, and the layout in Light, Dark and HighContrast.

- [ ] **Step 5: Commit**

```bash
git add tests/ui-tests.ps1
git commit -m "test(ui): cover the per-slot picker and geometry modes"
```

---

## Done

At this point:

- The batch page lists all 156 bases across ten slots, with colour variants behind a per-slot toggle.
- A run bakes the cross product of the selection and the tones, in either or both geometries.
- Skin recolours per layer, so bare arms follow the tone and wooden bows do not.
- `index.csv`, `clips.csv` and `sheets.csv` describe the output, the last stamped with a UUIDv7 run id.
- The spec-079 sheets still bake under their contract names.

**Not done, deliberately** — see the spec's "Deliberately skipped": no decoded-layer cache, no animated preview, no path-length guard on long stems, and no per-batch weapon-recolour toggle. **Also still open from the previous phase:** Corvus has not been updated for the 7-body / 9-hair scope (AC-9's filename list, the `CosmeticDescriptor` registry, and `!look`'s valid ranges). That is work in the Corvus repo, not this one.


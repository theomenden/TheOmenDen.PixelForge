global using ZLinq;

// No InternalsVisibleTo here, deliberately. The drop-in generator below emits an *internal*
// ZLinqDropInExtensions into every assembly that carries the attribute, so granting the test
// project access to this assembly's internals puts two identical extension classes in scope at
// once and every drop-in call in the tests fails with CS0121. SheetBaker.Substitute and
// SubstituteScalar are public for that reason — see the note on them.
//
// Generates higher-priority LINQ extension methods for Array, Span/ReadOnlySpan,
// Memory/ReadOnlyMemory, and List<T>, so ordinary `.Where(...).Select(...)` chains bind
// to ZLinq instead of System.Linq without touching call sites.
//
// Empty namespace = global, so it applies assembly-wide with no using directive.
// Collection (not Everything) on purpose: the IEnumerable<T> drop-in is the one ZLinq
// itself advises against by default, and it does not help here — pixel buffers are
// arrays and spans, which this covers.
[assembly: ZLinqDropIn("", DropInGenerateTypes.Collection)]

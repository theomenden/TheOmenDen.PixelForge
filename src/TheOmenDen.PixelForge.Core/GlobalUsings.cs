global using ZLinq;

// Generates higher-priority LINQ extension methods for Array, Span/ReadOnlySpan,
// Memory/ReadOnlyMemory, and List<T>, so ordinary `.Where(...).Select(...)` chains bind
// to ZLinq instead of System.Linq without touching call sites.
//
// Empty namespace = global, so it applies assembly-wide with no using directive.
// Collection (not Everything) on purpose: the IEnumerable<T> drop-in is the one ZLinq
// itself advises against by default, and it does not help here — pixel buffers are
// arrays and spans, which this covers.
[assembly: ZLinqDropIn("", DropInGenerateTypes.Collection)]

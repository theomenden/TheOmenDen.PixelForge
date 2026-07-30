using System.Diagnostics.CodeAnalysis;
using nietras.SeparatedValues;

namespace TheOmenDen.PixelForge.Core;

/// <summary>
/// The one place the CSV dialect is stated: comma separated, quoted only where it has to be.
/// </summary>
/// <remarks>
/// <para>
/// Both of the defaults that matter here are wrong for these files, and both fail silently, which
/// is why they are corrected once rather than at four call sites. Sep's default separator is
/// <c>;</c>, not <c>,</c>; and escaping is off, so a ramp named <c>Ash, Pale</c> would be written
/// as two columns rather than one quoted column and read back as a different ramp.
/// <c>Strict()</c> is Sep's own name for turning escaping on — and, on the reader, unescaping.
/// </para>
/// <para>
/// Sep has no object mapper by design: there is no <c>GetRecords&lt;T&gt;()</c> and no
/// <c>[Ignore]</c>, so every column is named where it is written. That verbosity is the whole
/// point — CsvHelper bound rows to properties by reflection, which no trimmer can follow, and it
/// was the last thing keeping this project off <c>IsAotCompatible</c>.
/// </para>
/// <para>
/// A header is written from the column names of the <em>first</em> row, in the order they are set,
/// so an empty sequence writes an empty file rather than a lone header. That round-trips —
/// <see cref="Palettes.RampStore.ReadAsync"/> reads an empty file as no ramps — so it is left alone.
/// </para>
/// </remarks>
internal static class Csv
{
    private static readonly Sep Comma = Sep.New(',');

    /// <summary>Opens a writer over <paramref name="writer"/>, which the caller still owns.</summary>
    /// <param name="writer">The destination. Left open when the returned writer is disposed.</param>
    /// <returns>
    /// A writer that flushes on dispose. Creation stays synchronous because opening a writer
    /// writes nothing — Sep has no <c>To*Async</c> for that reason. It is
    /// <c>await using</c> on the writer and on each row that does the I/O.
    /// </returns>
    [SuppressMessage(
        "Usage",
        "VSTHRD200:Use \"Async\" suffix for async methods",
        Justification = "Synchronous. SepWriter is IAsyncDisposable, which the analyzer reads as "
            + "an awaitable return type; nothing here awaits and there is no async counterpart "
            + "to name.")]
    internal static SepWriter Writer(TextWriter writer) =>
        Comma.Writer().Strict().To(writer, leaveOpen: true);

    /// <summary>Opens a reader over <paramref name="reader"/>, which the caller still owns.</summary>
    /// <param name="reader">The source. Left open when the returned reader is disposed.</param>
    /// <param name="cancellationToken">Cancels the read of the header row.</param>
    /// <returns>
    /// A reader positioned past the header. Structural faults — a row whose column count disagrees
    /// with the header, an unterminated quote — surface as <see cref="InvalidDataException"/>
    /// during enumeration, not here.
    /// </returns>
    /// <remarks>
    /// Creation is the awaited half: opening a reader has to read the header to learn the column
    /// names. Enumeration is asynchronous only if the caller writes <c>await foreach</c> — a plain
    /// <c>foreach</c> compiles and silently reads synchronously, which is the one Sep trap with no
    /// compiler diagnostic behind it.
    /// </remarks>
    internal static ValueTask<SepReader> ReaderAsync(
        TextReader reader,
        CancellationToken cancellationToken) =>
        Comma.Reader().Strict().FromAsync(reader, leaveOpen: true, cancellationToken);
}

using CommunityToolkit.Diagnostics;
using DotNext;
using Microsoft.IO;
using SkiaSharp;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// Encodes a baked sheet in the container its recipe names, verified either way.
/// </summary>
/// <remarks>
/// The one place the format-to-encoder mapping lives, exactly as
/// <see cref="SheetWriter.ExtensionFor"/> is the one place the format-to-extension mapping lives.
/// Two mappings, two call sites, and a third format has to be answered in both — which is a change
/// that fails to compile rather than one that writes a sheet nothing can open.
/// </remarks>
public static class SheetEncoder
{
    /// <summary>
    /// Encodes <paramref name="bitmap"/> and proves the result before returning it.
    /// </summary>
    /// <param name="bitmap">The finished sheet.</param>
    /// <param name="format">Which container to encode into.</param>
    /// <returns>The pooled stream, which the caller owns and disposes, or why encoding failed.</returns>
    public static Result<RecyclableMemoryStream, BakeFailure> EncodeVerified(
        SKBitmap bitmap,
        SheetFormat format)
    {
        Guard.IsNotNull(bitmap);

        return format switch
        {
            SheetFormat.Webp => LosslessWebp.EncodeVerified(bitmap),
            SheetFormat.Png => LosslessPng.EncodeVerified(bitmap),
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<Result<RecyclableMemoryStream, BakeFailure>>(
                nameof(format)),
        };
    }
}

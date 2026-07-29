using System.Text.Json.Serialization;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// Source-generated context. The reflection-based <c>JsonSerializer</c> overloads are banned —
/// they are trim-unsafe and this app publishes with <c>PublishTrimmed=true</c>.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PackSettings))]
internal sealed partial class PackSettingsContext : JsonSerializerContext;

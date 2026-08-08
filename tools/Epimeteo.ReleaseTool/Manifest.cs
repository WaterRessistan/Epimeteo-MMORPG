using System.Text.Json.Serialization;

namespace Epimeteo.ReleaseTool;

/// <summary>Una entrada del manifiesto: un fichero de la build, su hash y su tamaño.</summary>
public sealed class ManifestEntry
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("size")]
    public required long Size { get; init; }
}

/// <summary>
/// El manifiesto que sirve <c>/files/manifest.json</c> (FASE-15 §2 D2). Rutas siempre con <c>/</c>,
/// nunca <c>\</c>, para que el mismo fichero valga a un launcher en Windows o en Linux.
/// </summary>
public sealed class Manifest
{
    [JsonPropertyName("generatedAtUtc")]
    public required DateTime GeneratedAtUtc { get; init; }

    [JsonPropertyName("files")]
    public required IReadOnlyList<ManifestEntry> Files { get; init; }

    public const string FileName = "manifest.json";
}

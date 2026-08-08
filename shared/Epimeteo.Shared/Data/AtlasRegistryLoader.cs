using System.Text.Json;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Parsea <c>client/assets/atlas_registry.json</c>: un array de <see cref="AtlasRegion"/>, vacío
/// mientras no haya arte real (FASE-12 §1). Separado de <see cref="AtlasRegistry"/> —que sólo
/// envuelve el diccionario resultante, mismo patrón que <c>SkillCatalog</c>/<c>SkillLoader</c>—
/// para poder probar la validación con JSON en memoria, sin tocar disco ni Godot.
/// </summary>
public static class AtlasRegistryLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Lee y valida un fichero. Lanza <see cref="InvalidOperationException"/> si algo no cuadra.</summary>
    public static IReadOnlyDictionary<string, AtlasRegion> Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>La parte pura y testeable: JSON en memoria, con <paramref name="source"/> sólo para los mensajes de error.</summary>
    public static IReadOnlyDictionary<string, AtlasRegion> Parse(string json, string source)
    {
        RawAtlasRegion[]? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawAtlasRegion[]>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{source}: JSON inválido (se esperaba un array) — {ex.Message}", ex);
        }

        if (raw is null)
        {
            throw new InvalidOperationException($"{source}: JSON vacío.");
        }

        var byKey = new Dictionary<string, AtlasRegion>(StringComparer.Ordinal);

        foreach (var entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                throw new InvalidOperationException($"{source}: una entrada no tiene 'key'.");
            }

            if (string.IsNullOrWhiteSpace(entry.AtlasPath))
            {
                throw new InvalidOperationException($"{source}: '{entry.Key}' no tiene 'atlasPath'.");
            }

            if (entry.Width <= 0 || entry.Height <= 0)
            {
                throw new InvalidOperationException(
                    $"{source}: '{entry.Key}' tiene un tamaño inválido ({entry.Width}×{entry.Height}).");
            }

            if (entry.X < 0 || entry.Y < 0)
            {
                throw new InvalidOperationException($"{source}: '{entry.Key}' tiene coordenadas negativas.");
            }

            if (!byKey.TryAdd(entry.Key, new AtlasRegion
            {
                Key = entry.Key,
                AtlasPath = entry.AtlasPath,
                X = entry.X,
                Y = entry.Y,
                Width = entry.Width,
                Height = entry.Height,
            }))
            {
                throw new InvalidOperationException($"{source}: clave de atlas duplicada '{entry.Key}'.");
            }
        }

        return byKey;
    }

    /// <summary>Forma cruda del JSON, antes de validar. Nunca sale de este fichero.</summary>
    private sealed record RawAtlasRegion
    {
        public string? Key { get; init; }

        public string? AtlasPath { get; init; }

        public int X { get; init; }

        public int Y { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }
    }
}

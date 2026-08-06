using System.Text.Json;

namespace Epimeteo.Server.Content;

/// <summary>
/// Parsea y valida un <c>content/crops/*.json</c>. Separado de <see cref="CropCatalog"/> para
/// poder probar la validación con JSON en memoria, sin tocar disco — mismo patrón que
/// <c>ItemLoader</c>/<c>ShopLoader</c> (Fases 6–7).
/// </summary>
public static class CropLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Lee y valida un fichero. Lanza <see cref="InvalidOperationException"/> si algo no cuadra.</summary>
    public static CropDefinition Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>La parte pura y testeable: JSON en memoria, con <paramref name="source"/> sólo para los mensajes de error.</summary>
    public static CropDefinition Parse(string json, string source)
    {
        var raw = JsonSerializer.Deserialize<RawCropDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{source}: JSON vacío o inválido");

        if (string.IsNullOrWhiteSpace(raw.Key))
        {
            throw new InvalidOperationException($"{source}: falta 'key'.");
        }

        if (string.IsNullOrWhiteSpace(raw.SeedDefKey))
        {
            throw new InvalidOperationException($"{source}: falta 'seedDefKey'.");
        }

        if (string.IsNullOrWhiteSpace(raw.YieldDefKey))
        {
            throw new InvalidOperationException($"{source}: falta 'yieldDefKey'.");
        }

        if (raw.YieldQuantity < 1)
        {
            throw new InvalidOperationException($"{source}: 'yieldQuantity' tiene que ser al menos 1.");
        }

        if (raw.GrowthDaysNeeded <= 0)
        {
            throw new InvalidOperationException($"{source}: 'growthDaysNeeded' tiene que ser mayor que 0.");
        }

        if (raw.Stages is null || raw.Stages.Length == 0)
        {
            throw new InvalidOperationException($"{source}: 'stages' no puede estar vacío.");
        }

        var season = ParseSeason(raw.Season, source);

        return new CropDefinition
        {
            Key = raw.Key,
            DisplayName = raw.DisplayName ?? raw.Key,
            SeedDefKey = raw.SeedDefKey,
            YieldDefKey = raw.YieldDefKey,
            YieldQuantity = raw.YieldQuantity,
            GrowthDaysNeeded = raw.GrowthDaysNeeded,
            Season = season,
            Stages = raw.Stages,
        };
    }

    private static FarmSeason ParseSeason(string? raw, string source) => raw switch
    {
        null or "Any" => FarmSeason.Any,
        "Spring" => FarmSeason.Spring,
        "Summer" => FarmSeason.Summer,
        "Autumn" => FarmSeason.Autumn,
        "Winter" => FarmSeason.Winter,
        _ => throw new InvalidOperationException(
            $"{source}: 'season' desconocida '{raw}' (Any, Spring, Summer, Autumn, Winter)."),
    };

    /// <summary>Forma cruda del JSON, antes de validar. Nunca sale de este fichero.</summary>
    private sealed record RawCropDefinition
    {
        public string? Key { get; init; }

        public string? DisplayName { get; init; }

        public string? SeedDefKey { get; init; }

        public string? YieldDefKey { get; init; }

        public int YieldQuantity { get; init; }

        public float GrowthDaysNeeded { get; init; }

        public string? Season { get; init; }

        public string[]? Stages { get; init; }
    }
}

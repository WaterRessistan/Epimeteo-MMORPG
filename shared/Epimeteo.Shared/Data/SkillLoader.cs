using System.Text.Json;
using Epimeteo.Shared.Net.Messages;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Parsea y valida un <c>content/skills/*.json</c>. Separado de <see cref="SkillCatalog"/> para
/// poder probar la validación con JSON en memoria, sin tocar disco — mismo patrón que
/// <c>ItemLoader</c>/<c>ShopLoader</c> (Fases 6–7).
/// </summary>
public static class SkillLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Lee y valida un fichero. Lanza <see cref="InvalidOperationException"/> si algo no cuadra.</summary>
    public static SkillDefinition Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>La parte pura y testeable: JSON en memoria, con <paramref name="source"/> sólo para los mensajes de error.</summary>
    public static SkillDefinition Parse(string json, string source)
    {
        var raw = JsonSerializer.Deserialize<RawSkillDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{source}: JSON vacío o inválido");

        if (string.IsNullOrWhiteSpace(raw.Key))
        {
            throw new InvalidOperationException($"{source}: falta 'key'.");
        }

        if (string.IsNullOrWhiteSpace(raw.ClassKey))
        {
            throw new InvalidOperationException($"{source}: falta 'classKey'.");
        }

        if (raw.RequiredLevel < 1)
        {
            throw new InvalidOperationException($"{source}: 'requiredLevel' tiene que ser al menos 1.");
        }

        if (raw.ManaCost < 0)
        {
            throw new InvalidOperationException($"{source}: 'manaCost' no puede ser negativo.");
        }

        if (raw.CooldownMs < 1)
        {
            throw new InvalidOperationException($"{source}: 'cooldownMs' tiene que ser al menos 1.");
        }

        var kind = ParseKind(raw.Kind, source);

        if (raw.Power < 1)
        {
            throw new InvalidOperationException($"{source}: 'power' tiene que ser al menos 1.");
        }

        if (kind == CombatEventKind.Damage && raw.RangeTiles <= 0)
        {
            throw new InvalidOperationException($"{source}: 'rangeTiles' tiene que ser positivo para una habilidad de daño.");
        }

        return new SkillDefinition
        {
            Key = raw.Key,
            DisplayName = raw.DisplayName ?? raw.Key,
            ClassKey = raw.ClassKey,
            RequiredLevel = raw.RequiredLevel,
            ManaCost = raw.ManaCost,
            CooldownMs = raw.CooldownMs,
            Kind = kind,
            Power = raw.Power,
            RangeTiles = raw.RangeTiles,
        };
    }

    private static CombatEventKind ParseKind(string? raw, string source) => raw switch
    {
        "Damage" => CombatEventKind.Damage,
        "Heal" => CombatEventKind.Heal,
        _ => throw new InvalidOperationException($"{source}: 'kind' desconocido '{raw}' (Damage, Heal)."),
    };

    /// <summary>Forma cruda del JSON, antes de validar. Nunca sale de este fichero.</summary>
    private sealed record RawSkillDefinition
    {
        public string? Key { get; init; }

        public string? DisplayName { get; init; }

        public string? ClassKey { get; init; }

        public int RequiredLevel { get; init; } = 1;

        public int ManaCost { get; init; }

        public int CooldownMs { get; init; } = 1000;

        public string? Kind { get; init; }

        public int Power { get; init; }

        public float RangeTiles { get; init; } = CombatConstants.MeleeRangeTiles;
    }
}

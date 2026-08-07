using Epimeteo.Shared.Net.Messages;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Forma de <c>content/skills/*.json</c>. Vive en <c>Shared</c> —a diferencia de
/// <c>MonsterDefinition</c>/<c>CropDefinition</c>, servidor-only— porque la barra de habilidades
/// del cliente necesita saber qué tiene la clase, su coste de maná, su cooldown y desde qué nivel,
/// para pintarla y para el cooldown visual (FASE-10 §2 D6). La validación de verdad —si se puede
/// lanzar ahora mismo— la sigue haciendo el servidor.
/// </summary>
public sealed record SkillDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Sólo esta clase puede lanzarla.</summary>
    public required string ClassKey { get; init; }

    public required int RequiredLevel { get; init; }

    public required int ManaCost { get; init; }

    public required int CooldownMs { get; init; }

    /// <summary><c>Damage</c> o <c>Heal</c> (FASE-09: <c>CombatEventKind.Heal</c> reservado sin usar hasta ahora).</summary>
    public required CombatEventKind Kind { get; init; }

    /// <summary>
    /// Bonus plano: se suma al ataque del lanzador antes de <c>CombatFormulas.Hit</c> si es
    /// <c>Damage</c>, o es la curación en seco (sin RNG) si es <c>Heal</c> (FASE-10 §2 D8).
    /// </summary>
    public required int Power { get; init; }

    /// <summary>Alcance en tiles. Sin efecto en las curaciones (D9: siempre se apuntan a uno mismo).</summary>
    public required float RangeTiles { get; init; }
}

namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Qué es una entidad del mundo. Viaja en <c>EntitySpawn</c> para que el cliente sepa cómo
/// dibujarla y qué interacciones ofrecer. Sólo <see cref="Player"/> existe en la Fase 4; los
/// demás están declarados para que su número no cambie cuando lleguen.
/// </summary>
public enum EntityType : byte
{
    /// <summary>Personaje de un jugador.</summary>
    Player = 0,

    /// <summary>Monstruo (Fase 9).</summary>
    Monster = 1,

    /// <summary>NPC de pueblo: tendero, armero (Fase 7).</summary>
    Npc = 2,

    /// <summary>Saco de loot en el suelo (Fase 9).</summary>
    LootBag = 3,
}

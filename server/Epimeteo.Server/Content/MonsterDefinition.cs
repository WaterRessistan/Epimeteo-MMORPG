namespace Epimeteo.Server.Content;

/// <summary>
/// Forma de <c>content/monsters/*.json</c>. Servidor-only, como <c>CropDefinition</c> (Fase 8) y
/// <c>ClassDefinition</c>: el cliente no necesita el catálogo — lo que tiene que dibujar le llega
/// en <c>EntitySpawn</c> (nombre, vida, clave visual) como con cualquier otra entidad.
/// <para>
/// Los números son <b>provisionales</b>, igual que los stats base de las clases: los reajusta la
/// Fase 10 con la curva de progresión real.
/// </para>
/// </summary>
public sealed record MonsterDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required int Level { get; init; }

    public required int HpMax { get; init; }

    public required int Attack { get; init; }

    public required int Defense { get; init; }

    /// <summary>Destreza: decide la probabilidad de crítico en <c>CombatFormulas</c>.</summary>
    public required int Dex { get; init; }

    /// <summary>Velocidad al perseguir, en tiles/s.</summary>
    public required float MoveSpeedTiles { get; init; }

    public required float AttackRangeTiles { get; init; }

    public required int AttackCooldownMs { get; init; }

    /// <summary>A qué distancia empieza a perseguir a un jugador que ve.</summary>
    public required float AggroRadiusTiles { get; init; }

    /// <summary>
    /// A qué distancia de su punto de aparición se rinde y vuelve (FASE-09 §2 D7). Sin esto, un
    /// jugador arrastra un monstruo hasta la plaza y lo suelta encima de otro.
    /// </summary>
    public required float LeashRadiusTiles { get; init; }

    public required long XpReward { get; init; }

    public MonsterLootEntry[] Loot { get; init; } = [];
}

/// <summary>Una entrada de la tabla de loot de un monstruo.</summary>
public sealed record MonsterLootEntry
{
    public required string DefKey { get; init; }

    /// <summary>Probabilidad en <c>[0, 1]</c>.</summary>
    public required double Chance { get; init; }

    public required int Min { get; init; }

    public required int Max { get; init; }
}

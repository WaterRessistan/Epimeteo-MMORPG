namespace Epimeteo.Server.Content;

/// <summary>
/// Forma de <c>content/classes/*.json</c> (CLAUDE.md §3: definiciones de contenido en JSON
/// versionado, no en la BD). Stats iniciales explícitamente provisionales — los reajusta la
/// Fase 10 con la curva de progresión real.
/// </summary>
public sealed record ClassDefinition
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required int BaseStr { get; init; }
    public required int BaseInt { get; init; }
    public required int BaseVit { get; init; }
    public required int BaseDex { get; init; }
    public required int BaseHp { get; init; }
    public required int BaseMp { get; init; }

    /// <summary>
    /// Kit inicial (FASE-06 §2 D6): sin tiendas ni loot todavía, es la única forma de que un
    /// personaje recién creado tenga algo que mover o equipar. <c>CharacterService</c> lo inserta
    /// en la misma operación que crea la fila de <c>characters</c>.
    /// </summary>
    public StartingItem[] StartingItems { get; init; } = [];
}

/// <summary>Una entrada del kit inicial de una clase.</summary>
public sealed record StartingItem
{
    public required string DefKey { get; init; }
    public required int Quantity { get; init; }
}

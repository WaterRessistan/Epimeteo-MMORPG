namespace Epimeteo.Shared.Data;

/// <summary>Región declarada en el JSON del mapa (<c>docs/00-arquitectura.md §6</c>).</summary>
public sealed record MapRegionDefinition
{
    /// <summary>Nombre corto; viaja al cliente en <c>ZoneFlagsUpdate</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Rectángulo <c>[x, y, ancho, alto]</c> en tiles.</summary>
    public required int[] Rect { get; init; }

    /// <summary>Flags en texto: <c>safe</c>, <c>pvp</c>, <c>no_monsters</c>, <c>outdoor</c>, <c>indoor</c>.</summary>
    public string[] Flags { get; init; } = [];
}

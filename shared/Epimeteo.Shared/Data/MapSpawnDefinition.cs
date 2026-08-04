namespace Epimeteo.Shared.Data;

/// <summary>Punto de aparición declarado en el JSON del mapa.</summary>
public sealed record MapSpawnDefinition
{
    /// <summary>Coordenada X en tiles (usa <c>.5</c> para el centro de un tile).</summary>
    public required float X { get; init; }

    /// <summary>Coordenada Y en tiles.</summary>
    public required float Y { get; init; }

    /// <summary>Orientación inicial: 0 N, 1 E, 2 S, 3 O.</summary>
    public int Facing { get; init; } = 2;
}

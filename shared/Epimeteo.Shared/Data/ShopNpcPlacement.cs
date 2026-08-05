using Epimeteo.Shared.Simulation;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Dónde y quién es el tendero de una tienda. Vive dentro de <c>content/shops/*.json</c> junto al
/// catálogo (FASE-07 §2 D4): un NPC no es geometría de mapa, así que no se toca
/// <c>content/maps/*.json</c> ni su <c>MapHash</c> por esto.
/// </summary>
public sealed record ShopNpcPlacement
{
    /// <summary>Mapa donde está el tendero.</summary>
    public required string MapKey { get; init; }

    public required float X { get; init; }

    public required float Y { get; init; }

    public required Facing Facing { get; init; }

    /// <summary>Nombre visible sobre el NPC.</summary>
    public required string Name { get; init; }

    /// <summary>Apariencia placeholder mientras no haya sprites (CLAUDE.md §5).</summary>
    public required byte PaletteIndex { get; init; }
}

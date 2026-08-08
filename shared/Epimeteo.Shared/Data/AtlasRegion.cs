namespace Epimeteo.Shared.Data;

/// <summary>
/// Una entrada de <c>client/assets/atlas_registry.json</c>: qué región de qué imagen dibujar para
/// una clave (FASE-12 §2 D1). El servidor no la usa nunca — no conoce assets (CLAUDE.md §5) —
/// pero vive en <c>Shared</c> para que <see cref="AtlasRegistryLoader"/> sea testeable con xUnit
/// sin depender de Godot, mismo criterio que <c>MapLoader</c>.
/// </summary>
public sealed record AtlasRegion
{
    /// <summary>Clave que se busca: un <c>defKey</c> de entidad, o el <c>visualKey</c> de un ítem.</summary>
    public required string Key { get; init; }

    /// <summary>Ruta Godot (<c>res://...</c>) de la imagen que contiene la región.</summary>
    public required string AtlasPath { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }
}

namespace Epimeteo.Server.Content;

/// <summary>
/// Forma de <c>content/crops/*.json</c>. A diferencia de <c>ItemDefinition</c>/<c>ShopDefinition</c>
/// (Shared: el cliente los necesita para tooltips/UI), vive en <c>Server/Content</c> como
/// <c>ClassDefinition</c>/<c>MapCatalog</c>: sin arte, el cliente pinta la clave recortada y el
/// <c>Stage</c> ya resuelto que manda el servidor (FASE-08 §4), no necesita el catálogo entero.
/// </summary>
public sealed record CropDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Ítem que hay que plantar (<c>ItemType.Seed</c>) para sembrar este cultivo.</summary>
    public required string SeedDefKey { get; init; }

    /// <summary>Ítem que se recibe al cosechar.</summary>
    public required string YieldDefKey { get; init; }

    public required int YieldQuantity { get; init; }

    /// <summary>Progreso necesario para pasar a <c>Ready</c> (<c>farm_tiles.growth_needed</c>).</summary>
    public required float GrowthDaysNeeded { get; init; }

    public required FarmSeason Season { get; init; }

    /// <summary>Nombres cosméticos, uno por etapa visual. El índice lo calcula el servidor por progreso.</summary>
    public required string[] Stages { get; init; }
}

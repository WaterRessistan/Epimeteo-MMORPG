using Epimeteo.Shared.Data;

namespace Epimeteo.Server.Farm;

/// <summary>
/// Un tile de granja en memoria, autoritativo mientras dura el proceso — mismo papel que
/// <c>ShopStockState</c> (FASE-07 §5) o <c>ItemStack</c> (Fase 6). <c>fertilizer_key</c> y
/// <c>harvests_left</c> del esquema (<c>docs/02</c>) no tienen campo aquí: sin lógica todavía
/// (FASE-08 §2 D10).
/// </summary>
public sealed class FarmTileState
{
    public required int TileX { get; init; }

    public required int TileY { get; init; }

    public FarmTileStatus Status { get; set; } = FarmTileStatus.Untilled;

    public string? CropKey { get; set; }

    public DateTimeOffset? PlantedAt { get; set; }

    /// <summary><c>null</c> = sin regar en el día de granja actual.</summary>
    public DateTimeOffset? WateredAt { get; set; }

    public float GrowthDays { get; set; }

    public float GrowthNeeded { get; set; }

    /// <summary>Días regados seguidos → bonus de calidad al cosechar (FASE-08 §2 D9).</summary>
    public short WaterStreak { get; set; }

    /// <summary>Estimación optimista de cuándo estará listo, o <c>null</c> si no hay nada plantado.</summary>
    public DateTimeOffset? EtaAt { get; set; }
}

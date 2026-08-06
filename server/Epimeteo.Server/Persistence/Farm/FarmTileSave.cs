using Epimeteo.Server.Farm;

namespace Epimeteo.Server.Persistence.Farm;

/// <summary>
/// Un elemento de la cola de guardado de granja: o bien la instantánea de un tile, o bien el
/// nuevo <c>last_day_index</c> (nunca los dos), igual que <c>EconomySave.Restock</c> reutiliza un
/// solo tipo para "esto no lo escribió un jugador" (FASE-07 §2 D9).
/// </summary>
public readonly record struct FarmTileSave(
    long? PlotId,
    int TileX,
    int TileY,
    byte State,
    string? CropKey,
    DateTimeOffset? PlantedAt,
    DateTimeOffset? WateredAt,
    float GrowthDays,
    float GrowthNeeded,
    short WaterStreak,
    DateTimeOffset? EtaAt,
    int? CalendarDayIndex)
{
    public static FarmTileSave From(long plotId, FarmTileState tile) => new(
        plotId, tile.TileX, tile.TileY, (byte)tile.Status, tile.CropKey,
        tile.PlantedAt, tile.WateredAt, tile.GrowthDays, tile.GrowthNeeded,
        tile.WaterStreak, tile.EtaAt, null);

    public static FarmTileSave Calendar(int dayIndex) =>
        new(null, 0, 0, 0, null, null, null, 0, 0, 0, null, dayIndex);
}

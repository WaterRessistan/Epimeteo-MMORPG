namespace Epimeteo.Server.Persistence.Farm;

/// <summary>Fila cruda de <c>farm_tiles</c>, tal como sale de Dapper. Sólo transporte.</summary>
public sealed record FarmTileRow
{
    public required long PlotId { get; init; }

    public required int TileX { get; init; }

    public required int TileY { get; init; }

    public required short State { get; init; }

    public string? CropKey { get; init; }

    public DateTimeOffset? PlantedAt { get; init; }

    public DateTimeOffset? WateredAt { get; init; }

    public required float GrowthDays { get; init; }

    public required float GrowthNeeded { get; init; }

    public required short WaterStreak { get; init; }

    public DateTimeOffset? EtaAt { get; init; }
}

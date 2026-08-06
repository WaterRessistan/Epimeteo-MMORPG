namespace Epimeteo.Server.Persistence.Farm;

/// <summary>Fila cruda de <c>farm_plots</c>, tal como sale de Dapper. Sólo transporte.</summary>
public sealed record FarmPlotRow
{
    public required long Id { get; init; }

    public required string MapKey { get; init; }

    public required int OriginX { get; init; }

    public required int OriginY { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }
}

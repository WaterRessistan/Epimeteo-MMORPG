namespace Epimeteo.Server.Farm;

/// <summary>Una parcela en memoria: su geometría (fija, de <c>farm_plots</c>) más el estado de cada uno de sus tiles.</summary>
public sealed class FarmPlotRuntime
{
    public required long PlotId { get; init; }

    public required string MapKey { get; init; }

    public required int OriginX { get; init; }

    public required int OriginY { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Un <see cref="FarmTileState"/> por cada <c>(x, y)</c> del rectángulo — nunca crece ni encoge.</summary>
    public required Dictionary<(int X, int Y), FarmTileState> Tiles { get; init; }

    public bool Contains(int tileX, int tileY) =>
        tileX >= OriginX && tileX < OriginX + Width && tileY >= OriginY && tileY < OriginY + Height;
}

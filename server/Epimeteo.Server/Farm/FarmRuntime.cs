using Epimeteo.Server.Persistence.Farm;
using Epimeteo.Shared.Data;

namespace Epimeteo.Server.Farm;

/// <summary>
/// Todas las parcelas en memoria, autoritativas mientras dura el proceso — mismo papel que
/// <c>ShopRuntime</c> (FASE-07 §5). Se construye una vez al arrancar: la geometría sale de
/// <c>farm_plots</c> (fija, D2) y cada tile se sintetiza "virgen" salvo que hubiera fila guardada
/// en <c>farm_tiles</c> (D3, mismo patrón de fusión que <c>ShopRuntime</c>).
/// </summary>
public sealed class FarmRuntime
{
    private readonly List<FarmPlotRuntime> _plots;

    public FarmRuntime(IReadOnlyList<FarmPlotRow> plotRows, IReadOnlyList<FarmTileRow> tileRows, int lastProcessedDayIndex)
    {
        LastProcessedDayIndex = lastProcessedDayIndex;

        var tilesByPlot = tileRows.ToLookup(row => row.PlotId);

        _plots = [.. plotRows.Select(plot =>
        {
            var saved = tilesByPlot[plot.Id].ToDictionary(row => (row.TileX, row.TileY));
            var tiles = new Dictionary<(int, int), FarmTileState>();

            for (var x = plot.OriginX; x < plot.OriginX + plot.Width; x++)
            {
                for (var y = plot.OriginY; y < plot.OriginY + plot.Height; y++)
                {
                    tiles[(x, y)] = saved.TryGetValue((x, y), out var row) ? FromRow(x, y, row) : new FarmTileState
                    {
                        TileX = x,
                        TileY = y,
                    };
                }
            }

            return new FarmPlotRuntime
            {
                PlotId = plot.Id,
                MapKey = plot.MapKey,
                OriginX = plot.OriginX,
                OriginY = plot.OriginY,
                Width = plot.Width,
                Height = plot.Height,
                Tiles = tiles,
            };
        })];
    }

    /// <summary>Último día de granja ya cerrado (FASE-08 §2 D1). El barrido de <c>GameWorld</c> lo avanza.</summary>
    public int LastProcessedDayIndex { get; set; }

    public IReadOnlyList<FarmPlotRuntime> Plots => _plots;

    /// <summary>La parcela que contiene un tile de un mapa, si la hay.</summary>
    public FarmPlotRuntime? FindPlotContaining(string mapKey, int tileX, int tileY) =>
        _plots.FirstOrDefault(plot => plot.MapKey == mapKey && plot.Contains(tileX, tileY));

    /// <summary>
    /// Cierra un día de granja en todas las parcelas (FASE-08 §2 D1). Devuelve los tiles que
    /// cambiaron, para que quien llame decida qué persistir y a quién avisar.
    /// </summary>
    public IReadOnlyList<(FarmPlotRuntime Plot, FarmTileState Tile)> ApplyDailyGrowth(DateTimeOffset dayBoundaryEnd)
    {
        List<(FarmPlotRuntime, FarmTileState)>? changed = null;

        foreach (var plot in _plots)
        {
            foreach (var tile in plot.Tiles.Values)
            {
                if (FarmSystem.ApplyDailyGrowth(tile, dayBoundaryEnd))
                {
                    (changed ??= []).Add((plot, tile));
                }
            }
        }

        return changed ?? [];
    }

    private static FarmTileState FromRow(int x, int y, FarmTileRow row) => new()
    {
        TileX = x,
        TileY = y,
        Status = (FarmTileStatus)row.State,
        CropKey = row.CropKey,
        PlantedAt = row.PlantedAt,
        WateredAt = row.WateredAt,
        GrowthDays = row.GrowthDays,
        GrowthNeeded = row.GrowthNeeded,
        WaterStreak = row.WaterStreak,
        EtaAt = row.EtaAt,
    };
}

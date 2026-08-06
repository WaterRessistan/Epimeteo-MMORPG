using Dapper;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Farm;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Contra Postgres real: <c>UPSERT</c> de <c>farm_tiles</c>. Usa la parcela comunitaria que
/// siembra la migración (FASE-08 §3) como <c>plot_id</c> válido, pero con coordenadas de tile muy
/// lejos de las reales de la parcela (fuera de su rectángulo, sin CHECK que lo impida) para no
/// interferir con partidas de verdad.
/// </summary>
public sealed class FarmTileRepositoryTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);

    private static (int X, int Y) UniqueTile()
    {
        var offset = Random.Shared.Next(100_000, 999_999);
        return (offset, offset);
    }

    private async Task<long> SeededPlotIdAsync()
    {
        var plots = await new FarmPlotRepository(_connections).ListAllAsync();
        return plots.Single(p => p.MapKey == "map.village").Id;
    }

    [PostgresFact]
    public async Task UpsertAsync_LuegoListAllAsync_DevuelveElTile()
    {
        var repo = new FarmTileRepository(_connections);
        var plotId = await SeededPlotIdAsync();
        var (x, y) = UniqueTile();

        try
        {
            await repo.UpsertAsync(new FarmTileSave(
                plotId, x, y, State: 2, CropKey: "crop.wheat",
                PlantedAt: DateTimeOffset.UtcNow, WateredAt: DateTimeOffset.UtcNow,
                GrowthDays: 1.5f, GrowthNeeded: 3f, WaterStreak: 1,
                EtaAt: DateTimeOffset.UtcNow.AddDays(2), CalendarDayIndex: null));

            var rows = await repo.ListAllAsync();
            var row = Assert.Single(rows, r => r.PlotId == plotId && r.TileX == x && r.TileY == y);
            Assert.Equal((short)2, row.State);
            Assert.Equal("crop.wheat", row.CropKey);
            Assert.Equal(1.5f, row.GrowthDays);
            Assert.Equal(1, row.WaterStreak);
        }
        finally
        {
            await DeleteAsync(plotId, x, y);
        }
    }

    [PostgresFact]
    public async Task UpsertAsync_DosVeces_ActualizaLaMismaFilaEnVezDeDuplicar()
    {
        var repo = new FarmTileRepository(_connections);
        var plotId = await SeededPlotIdAsync();
        var (x, y) = UniqueTile();

        try
        {
            await repo.UpsertAsync(new FarmTileSave(
                plotId, x, y, State: 1, CropKey: null, PlantedAt: null, WateredAt: null,
                GrowthDays: 0, GrowthNeeded: 0, WaterStreak: 0, EtaAt: null, CalendarDayIndex: null));
            await repo.UpsertAsync(new FarmTileSave(
                plotId, x, y, State: 3, CropKey: "crop.wheat", PlantedAt: DateTimeOffset.UtcNow, WateredAt: null,
                GrowthDays: 3, GrowthNeeded: 3, WaterStreak: 2, EtaAt: null, CalendarDayIndex: null));

            var rows = await repo.ListAllAsync();
            var matching = rows.Where(r => r.PlotId == plotId && r.TileX == x && r.TileY == y).ToList();

            var row = Assert.Single(matching);
            Assert.Equal((short)3, row.State);
        }
        finally
        {
            await DeleteAsync(plotId, x, y);
        }
    }

    private async Task DeleteAsync(long plotId, int x, int y)
    {
        await using var connection = await _connections.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM farm_tiles WHERE plot_id = @plotId AND tile_x = @x AND tile_y = @y",
            new { plotId, x, y });
    }
}

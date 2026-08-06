using Dapper;

namespace Epimeteo.Server.Persistence.Farm;

/// <summary>
/// Acceso Dapper a <c>farm_tiles</c>: carga inicial y <c>UPSERT</c> por tile. Un tile nunca
/// arado no tiene fila (FASE-08 §2 D3) — <c>UpsertAsync</c> es lo que crea la primera.
/// </summary>
public sealed class FarmTileRepository(NpgsqlConnectionFactory connections)
{
    /// <summary>Todo lo guardado, de todas las parcelas. Se llama una vez al arrancar (<c>FarmRuntime</c>).</summary>
    public async Task<IReadOnlyList<FarmTileRow>> ListAllAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<FarmTileRow>(
            new CommandDefinition(
                """
                SELECT plot_id AS "PlotId", tile_x AS "TileX", tile_y AS "TileY", state AS "State",
                       crop_key AS "CropKey", planted_at AS "PlantedAt", watered_at AS "WateredAt",
                       growth_days AS "GrowthDays", growth_needed AS "GrowthNeeded",
                       water_streak AS "WaterStreak", eta_at AS "EtaAt"
                  FROM farm_tiles
                """,
                cancellationToken: ct)).ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task UpsertAsync(FarmTileSave save, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO farm_tiles
                    (plot_id, tile_x, tile_y, state, crop_key, planted_at, watered_at,
                     growth_days, growth_needed, water_streak, eta_at)
                VALUES
                    (@plotId, @tileX, @tileY, @state, @cropKey, @plantedAt, @wateredAt,
                     @growthDays, @growthNeeded, @waterStreak, @etaAt)
                ON CONFLICT (plot_id, tile_x, tile_y) DO UPDATE
                    SET state = @state, crop_key = @cropKey, planted_at = @plantedAt,
                        watered_at = @wateredAt, growth_days = @growthDays,
                        growth_needed = @growthNeeded, water_streak = @waterStreak, eta_at = @etaAt
                """,
                new
                {
                    plotId = save.PlotId,
                    tileX = save.TileX,
                    tileY = save.TileY,
                    state = save.State,
                    cropKey = save.CropKey,
                    plantedAt = save.PlantedAt,
                    wateredAt = save.WateredAt,
                    growthDays = save.GrowthDays,
                    growthNeeded = save.GrowthNeeded,
                    waterStreak = save.WaterStreak,
                    etaAt = save.EtaAt,
                },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

using Dapper;

namespace Epimeteo.Server.Persistence.Farm;

/// <summary>Acceso Dapper a <c>farm_plots</c>: sólo lectura — la geometría la crea la migración (FASE-08 §2 D2), nadie la escribe en caliente esta fase.</summary>
public sealed class FarmPlotRepository(NpgsqlConnectionFactory connections)
{
    public async Task<IReadOnlyList<FarmPlotRow>> ListAllAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<FarmPlotRow>(
            new CommandDefinition(
                """
                SELECT id AS "Id", map_key AS "MapKey", origin_x AS "OriginX", origin_y AS "OriginY",
                       width AS "Width", height AS "Height"
                  FROM farm_plots
                """,
                cancellationToken: ct)).ConfigureAwait(false);

        return rows.AsList();
    }
}

using Dapper;

namespace Epimeteo.Server.Persistence.Farm;

/// <summary>Acceso Dapper a la fila única de <c>farm_calendar</c> (FASE-08 §2 D1).</summary>
public sealed class FarmCalendarRepository(NpgsqlConnectionFactory connections)
{
    public async Task<int> GetLastDayIndexAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                """SELECT last_day_index FROM farm_calendar WHERE id = 1""",
                cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task SetLastDayIndexAsync(int dayIndex, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO farm_calendar (id, last_day_index)
                VALUES (1, @dayIndex)
                ON CONFLICT (id) DO UPDATE SET last_day_index = @dayIndex
                """,
                new { dayIndex },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

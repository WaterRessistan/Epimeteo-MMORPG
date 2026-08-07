using System.Text.Json;
using Dapper;

namespace Epimeteo.Server.Persistence.Combat;

/// <summary>Acceso Dapper a <c>combat_log</c>. Sólo inserta: es un log append-only.</summary>
public sealed class CombatLogRepository(NpgsqlConnectionFactory connections)
{
    public async Task InsertAsync(CombatLogSave save, CancellationToken ct = default)
    {
        var context = JsonSerializer.Serialize(new { region = save.Region });

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO combat_log
                    (victim_id, killer_id, map_key, region, victim_level, killer_level, xp_lost, context)
                VALUES
                    (@victimId, @killerId, @mapKey, @region, @victimLevel, @killerLevel, @xpLost, @context::jsonb)
                """,
                new
                {
                    victimId = save.VictimId,
                    killerId = save.KillerId,
                    mapKey = save.MapKey,
                    region = save.Region,
                    victimLevel = save.VictimLevel,
                    killerLevel = save.KillerLevel,
                    xpLost = save.XpLost,
                    context,
                },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

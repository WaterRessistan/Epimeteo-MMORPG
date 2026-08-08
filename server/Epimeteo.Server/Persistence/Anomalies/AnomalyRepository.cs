using Dapper;

namespace Epimeteo.Server.Persistence.Anomalies;

/// <summary>Acceso Dapper a <c>anomaly_log</c>. Sólo inserta: es un log append-only.</summary>
public sealed class AnomalyRepository(NpgsqlConnectionFactory connections)
{
    public async Task InsertAsync(AnomalySave save, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO anomaly_log
                    (session_id, character_id, account_id, kind, count_in_window, action_taken, remote_address)
                VALUES
                    (@sessionId, @characterId, @accountId, @kind, @countInWindow, @actionTaken, @remoteAddress)
                """,
                new
                {
                    sessionId = save.SessionId,

                    // 0 es "sin personaje elegido todavía", no el personaje número cero: se
                    // guarda NULL para que el FK no falle y para no mentir en los informes.
                    characterId = save.CharacterId is > 0 ? save.CharacterId : null,
                    accountId = save.AccountId is > 0 ? save.AccountId : null,
                    kind = (short)save.Kind,
                    countInWindow = save.CountInWindow,
                    actionTaken = (short)save.Verdict,
                    remoteAddress = save.RemoteAddress,
                },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

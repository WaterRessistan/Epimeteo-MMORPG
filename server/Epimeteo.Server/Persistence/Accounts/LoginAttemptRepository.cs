using System.Net;
using Dapper;

namespace Epimeteo.Server.Persistence.Accounts;

/// <summary>
/// Acceso Dapper a <c>login_attempts</c>. Es el rate limit de <c>docs/01-protocolo.md</c>
/// (5/minuto por IP, no por sesión): tiene que sobrevivir a una reconexión y a un reinicio del
/// servidor, así que vive en Postgres y no en el <c>SessionRateLimiter</c> en memoria de la Fase 1.
/// </summary>
public sealed class LoginAttemptRepository(NpgsqlConnectionFactory connections)
{
    public async Task<int> CountRecentAsync(IPAddress ip, TimeSpan window, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT count(*)::int FROM login_attempts
                 WHERE ip = @ip::inet AND attempted_at > now() - @window
                """,
                new { ip = ip.ToString(), window },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RecordAsync(IPAddress ip, string? username, bool success, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO login_attempts (ip, username, success)
                VALUES (@ip::inet, @username, @success)
                """,
                new { ip = ip.ToString(), username, success },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

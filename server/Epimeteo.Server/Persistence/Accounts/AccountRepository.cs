using System.Net;
using Dapper;
using Npgsql;

namespace Epimeteo.Server.Persistence.Accounts;

/// <summary>Acceso Dapper a la tabla <c>accounts</c>.</summary>
public sealed class AccountRepository(NpgsqlConnectionFactory connections)
{
    public async Task<Account?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<Account>(
            new CommandDefinition(
                """
                SELECT id, username, password_hash AS "PasswordHash", status, banned_until AS "BannedUntil",
                       is_admin AS "IsAdmin"
                  FROM accounts
                 WHERE username = @username
                """,
                new { username },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Crea la cuenta. Devuelve <c>null</c> si el nombre ya existe — error esperado,
    /// no una excepción que suba hasta el manejador de mensajes (CLAUDE.md §4).</summary>
    public async Task<long?> CreateAsync(string username, string? email, string passwordHash, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            return await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    """
                    INSERT INTO accounts (username, email, password_hash)
                    VALUES (@username, @email, @passwordHash)
                    RETURNING id
                    """,
                    new { username, email, passwordHash },
                    cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="remoteAddress"/> puede no ser una IP parseable (p. ej. "desconocida" cuando
    /// <c>Connection.RemoteIpAddress</c> viene nulo); en ese caso se actualiza sólo la fecha.
    /// </summary>
    public async Task TouchLastLoginAsync(long accountId, string remoteAddress, CancellationToken ct = default)
    {
        var ip = IPAddress.TryParse(remoteAddress, out var parsed) ? parsed.ToString() : null;

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE accounts
                   SET last_login_at = now(),
                       last_login_ip = COALESCE(@ip::inet, last_login_ip)
                 WHERE id = @accountId
                """,
                new { accountId, ip },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

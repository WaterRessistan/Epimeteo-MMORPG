using System.Net;
using System.Security.Cryptography;
using Dapper;

namespace Epimeteo.Server.Persistence.Accounts;

/// <summary>
/// Emite tokens de sesión persistentes. El token en claro sólo existe en memoria durante esta
/// llamada y en el <see cref="Epimeteo.Shared.Net.Messages.S2CAuthResult"/> que se manda al
/// cliente; la BD guarda únicamente su SHA-256, igual que con una contraseña.
/// <para>
/// Esta fase no implementa reconexión con token guardado (ver FASE-02-persistencia.md §13):
/// la fila queda lista para cuando esa pantalla exista, pero hoy nadie la lee de vuelta.
/// </para>
/// </summary>
public sealed class SessionTokenService(NpgsqlConnectionFactory connections)
{
    private const int TokenBytes = 32;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<string> IssueAsync(long accountId, string remoteAddress, CancellationToken ct = default)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(TokenBytes);
        var token = Convert.ToBase64String(tokenBytes);
        var tokenHash = SHA256.HashData(tokenBytes);
        var ip = IPAddress.TryParse(remoteAddress, out var parsed) ? parsed.ToString() : null;

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO account_sessions (account_id, token_hash, expires_at, ip)
                VALUES (@accountId, @tokenHash, now() + @lifetime, @ip::inet)
                """,
                new { accountId, tokenHash, lifetime = Lifetime, ip },
                cancellationToken: ct)).ConfigureAwait(false);

        return token;
    }
}

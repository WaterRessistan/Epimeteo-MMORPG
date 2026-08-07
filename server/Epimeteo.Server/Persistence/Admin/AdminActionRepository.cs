using System.Text.Json;
using Dapper;

namespace Epimeteo.Server.Persistence.Admin;

/// <summary>Acceso Dapper a <c>admin_action_log</c> y, sólo para <c>Ban</c>, a <c>accounts</c> (FASE-11 §2 D7).</summary>
public sealed class AdminActionRepository(NpgsqlConnectionFactory connections)
{
    public async Task InsertAsync(AdminActionSave save, CancellationToken ct = default)
    {
        var details = save.Action switch
        {
            AdminAction.Ban => JsonSerializer.Serialize(new { hours = save.BanHours }),
            AdminAction.Give => JsonSerializer.Serialize(new { defKey = save.DefKey, quantity = save.Quantity }),
            _ => null,
        };

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO admin_action_log
                    (admin_character_id, admin_name, target_character_id, target_name, action, reason, details)
                VALUES
                    (@adminCharacterId, @adminName, @targetCharacterId, @targetName, @action, @reason, @details::jsonb)
                """,
                new
                {
                    adminCharacterId = save.AdminCharacterId,
                    adminName = save.AdminName,
                    targetCharacterId = save.TargetCharacterId,
                    targetName = save.TargetName,
                    action = (short)save.Action,
                    reason = string.IsNullOrEmpty(save.Reason) ? null : save.Reason,
                    details,
                },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// El efecto de verdad de un <c>/ban</c>: la cuenta no vuelve a entrar hasta que pase
    /// <paramref name="hours"/> (<c>AuthService.LoginAsync</c> ya rechaza <c>AccountStatus.Banned</c>).
    /// </summary>
    public async Task BanAccountByCharacterAsync(long targetCharacterId, int hours, string? reason, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE accounts
                   SET status = 2,
                       banned_until = now() + make_interval(hours => @hours),
                       ban_reason = @reason
                 WHERE id = (SELECT account_id FROM characters WHERE id = @targetCharacterId)
                """,
                new { targetCharacterId, hours, reason },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

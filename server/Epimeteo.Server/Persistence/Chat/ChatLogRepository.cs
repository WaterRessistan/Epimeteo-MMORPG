using Dapper;

namespace Epimeteo.Server.Persistence.Chat;

/// <summary>Acceso Dapper a <c>chat_log</c>. Sólo inserta: es un log append-only.</summary>
public sealed class ChatLogRepository(NpgsqlConnectionFactory connections)
{
    public async Task InsertAsync(ChatLogSave save, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO chat_log (character_id, channel, body)
                VALUES (@characterId, @channel, @body)
                """,
                new
                {
                    characterId = save.CharacterId,
                    channel = (short)save.Channel,
                    body = save.Body,
                },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

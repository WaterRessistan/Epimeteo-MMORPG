using System.Text.Json;
using Dapper;

namespace Epimeteo.Server.Persistence.Economy;

/// <summary>Acceso Dapper a <c>economy_log</c>: sólo <c>INSERT</c>, append-only por diseño.</summary>
public sealed class EconomyLogRepository(NpgsqlConnectionFactory connections)
{
    public async Task InsertAsync(EconomySave save, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO economy_log (kind, character_id, def_key, quantity, gold_delta, gold_after, context)
                VALUES (@kind, @characterId, @defKey, @quantity, @goldDelta, @goldAfter, @context::jsonb)
                """,
                new
                {
                    kind = (short)save.Kind,
                    characterId = save.CharacterId,
                    defKey = save.DefKey,
                    quantity = save.Quantity,
                    goldDelta = save.GoldDelta,
                    goldAfter = save.GoldAfter,
                    context = save.ShopKey is null ? "{}" : JsonSerializer.Serialize(new { shopKey = save.ShopKey }),
                },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

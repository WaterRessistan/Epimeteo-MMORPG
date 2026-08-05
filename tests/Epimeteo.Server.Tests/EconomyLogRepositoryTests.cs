using Dapper;
using Epimeteo.Server.Content;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Characters;
using Epimeteo.Server.Persistence.Economy;
using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Contra Postgres real: <c>economy_log</c> es append-only, cada fila es independiente.</summary>
public sealed class EconomyLogRepositoryTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());
    private static readonly ClassCatalog Classes = new(ContentPaths.ResolveContentRoot());

    [PostgresFact]
    public async Task InsertAsync_EscribeUnaFilaConLosDatosEsperados()
    {
        var (characterId, accountId) = await CreateCharacterAsync();
        var repo = new EconomyLogRepository(_connections);

        try
        {
            await repo.InsertAsync(new EconomySave(
                EconomyLogKind.Buy, characterId, "item.iron_sword", 1, -80, 20, "shop.armory", 4, 5, DateTimeOffset.UtcNow));

            var row = await QuerySingleAsync(characterId);
            Assert.Equal((short)EconomyLogKind.Buy, row.Kind);
            Assert.Equal("item.iron_sword", row.DefKey);
            Assert.Equal(1, row.Quantity);
            Assert.Equal(-80, row.GoldDelta);
            Assert.Equal(20, row.GoldAfter);
        }
        finally
        {
            await CleanupAsync(characterId, accountId);
        }
    }

    [PostgresFact]
    public async Task InsertAsync_DosVeces_DejaDosFilasIndependientes()
    {
        var (characterId, accountId) = await CreateCharacterAsync();
        var repo = new EconomyLogRepository(_connections);

        try
        {
            await repo.InsertAsync(new EconomySave(EconomyLogKind.Buy, characterId, "item.iron_sword", 1, -80, 20, "shop.armory", 4, 5, DateTimeOffset.UtcNow));
            await repo.InsertAsync(new EconomySave(EconomyLogKind.Sell, characterId, "item.wooden_shield", 1, 12, 32, "shop.armory", 5, 5, DateTimeOffset.UtcNow));

            await using var connection = await _connections.OpenAsync();
            var count = await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM economy_log WHERE character_id = @characterId", new { characterId });

            Assert.Equal(2, count);
        }
        finally
        {
            await CleanupAsync(characterId, accountId);
        }
    }

    [PostgresFact]
    public async Task InsertAsync_SinTienda_GuardaContextoVacio()
    {
        var (characterId, accountId) = await CreateCharacterAsync();
        var repo = new EconomyLogRepository(_connections);

        try
        {
            await repo.InsertAsync(new EconomySave(EconomyLogKind.Drop, characterId, "item.iron_ore", 3, 0, 0, null, null, null, null));

            var row = await QuerySingleAsync(characterId);
            Assert.Equal((short)EconomyLogKind.Drop, row.Kind);
            Assert.Equal("{}", row.Context.Trim());
        }
        finally
        {
            await CleanupAsync(characterId, accountId);
        }
    }

    private async Task<(short Kind, string DefKey, int Quantity, long GoldDelta, long GoldAfter, string Context)> QuerySingleAsync(long characterId)
    {
        await using var connection = await _connections.OpenAsync();
        return await connection.QuerySingleAsync<(short, string, int, long, long, string)>(
            """
            SELECT kind, def_key, quantity, gold_delta, gold_after, context::text
              FROM economy_log
             WHERE character_id = @characterId
            """,
            new { characterId });
    }

    private async Task<(long CharacterId, long AccountId)> CreateCharacterAsync()
    {
        Assert.True(Classes.TryGet("class.warrior", out var warrior));
        var characters = new CharacterRepository(_connections, Items);

        await using var connection = await _connections.OpenAsync();
        var accountId = await connection.ExecuteScalarAsync<long>(
            "INSERT INTO accounts (username, password_hash) VALUES (@username, 'hash-de-prueba') RETURNING id",
            new { username = $"epi_test_{Guid.NewGuid():N}"[..20] });

        var (id, error) = await characters.CreateAsync(accountId, 0, $"epi_test_{Guid.NewGuid():N}"[..15], warrior!, 0);
        Assert.Equal(CharacterCreateError.None, error);

        return (id!.Value, accountId);
    }

    /// <summary>
    /// <c>economy_log</c> no tiene FK a <c>characters</c> a propósito (una fila de auditoría
    /// sobrevive al borrado de la cuenta) — así que hay que limpiarla a mano, la cascada de borrar
    /// la cuenta no la toca.
    /// </summary>
    private async Task CleanupAsync(long characterId, long accountId)
    {
        await using var connection = await _connections.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM economy_log WHERE character_id = @characterId", new { characterId });
        await connection.ExecuteAsync("DELETE FROM accounts WHERE id = @accountId", new { accountId });
    }
}

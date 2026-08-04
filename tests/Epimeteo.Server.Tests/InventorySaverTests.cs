using Dapper;
using Epimeteo.Server.Content;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Characters;
using Epimeteo.Server.Persistence.Items;
using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Contra Postgres real: cada <c>InventorySave</c> es una instantánea completa, no un delta
/// (FASE-06 §2 D2) — el test que importa de verdad es que la segunda sustituye a la primera
/// entera, no que se combinen.
/// </summary>
public sealed class InventorySaverTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());
    private static readonly ClassCatalog Classes = new(ContentPaths.ResolveContentRoot());

    private static ItemStackSnapshot Snapshot(string defKey, ContainerId container, byte slot, int quantity = 1) =>
        new(defKey, (byte)container, slot, quantity, null, null, 0, null);

    [PostgresFact]
    public async Task Enqueue_DejaPostgresConExactamenteEseConjunto()
    {
        Assert.True(Classes.TryGet("class.hybrid", out var hybrid));

        var characters = new CharacterRepository(_connections, Items);
        var itemRepo = new ItemRepository(_connections);
        var saver = new InventorySaver(itemRepo);
        var accountId = await CreateAccountAsync();

        try
        {
            var created = await characters.CreateAsync(accountId, 0, UniqueName(), hybrid!, paletteIndex: 0);
            var characterId = created.Id!.Value;

            await saver.StartAsync(CancellationToken.None);

            saver.Enqueue(new InventorySave(characterId, [
                Snapshot("item.iron_ore", ContainerId.General, 0, quantity: 42),
            ]));

            await WaitForQueueToDrainAsync(saver);

            var rows = await itemRepo.ListByCharacterAsync(characterId);
            var row = Assert.Single(rows);
            Assert.Equal("item.iron_ore", row.DefKey);
            Assert.Equal(42, row.Quantity);

            // Segunda instantánea, contenido distinto: tiene que reemplazar, no acumular.
            saver.Enqueue(new InventorySave(characterId, [
                Snapshot("item.wheat_seed", ContainerId.General, 0, quantity: 7),
                Snapshot("item.health_potion", ContainerId.General, 1, quantity: 3),
            ]));

            await WaitForQueueToDrainAsync(saver);

            var afterSecond = await itemRepo.ListByCharacterAsync(characterId);
            Assert.Equal(2, afterSecond.Count);
            Assert.DoesNotContain(afterSecond, r => r.DefKey == "item.iron_ore");
            Assert.Contains(afterSecond, r => r.DefKey == "item.wheat_seed" && r.Quantity == 7);
            Assert.Contains(afterSecond, r => r.DefKey == "item.health_potion" && r.Quantity == 3);
        }
        finally
        {
            await saver.StopAsync(CancellationToken.None);
            await DeleteAccountAsync(accountId);
        }
    }

    private static async Task WaitForQueueToDrainAsync(InventorySaver saver)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (saver.PendingCount > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        // Margen extra: PendingCount llega a 0 cuando se saca de la cola, no cuando termina el
        // INSERT/DELETE contra Postgres.
        await Task.Delay(100);
    }

    private static string UniqueName() => $"epi_test_{Guid.NewGuid():N}"[..20];

    private async Task<long> CreateAccountAsync()
    {
        await using var connection = await _connections.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "INSERT INTO accounts (username, password_hash) VALUES (@username, 'hash-de-prueba') RETURNING id",
            new { username = UniqueName() });
    }

    private async Task DeleteAccountAsync(long accountId)
    {
        await using var connection = await _connections.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM accounts WHERE id = @accountId", new { accountId });
    }
}

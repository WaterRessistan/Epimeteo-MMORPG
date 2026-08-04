using Dapper;
using Epimeteo.Server.Content;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Characters;
using Epimeteo.Server.Persistence.Items;
using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Contra Postgres real: <c>ListByCharacterAsync</c> tras crear un personaje con kit inicial de
/// verdad (mismo camino que recorre un jugador nuevo, no datos insertados a mano).
/// </summary>
public sealed class ItemRepositoryTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());
    private static readonly ClassCatalog Classes = new(ContentPaths.ResolveContentRoot());

    [PostgresFact]
    public async Task ListByCharacterAsync_TrasCrearConKitInicial_DevuelveElKit()
    {
        Assert.True(Classes.TryGet("class.warrior", out var warrior));

        var characters = new CharacterRepository(_connections, Items);
        var itemRepo = new ItemRepository(_connections);
        var accountId = await CreateAccountAsync();

        try
        {
            var (id, error) = await characters.CreateAsync(accountId, 0, UniqueName(), warrior!, paletteIndex: 0);
            Assert.Equal(CharacterCreateError.None, error);

            var rows = await itemRepo.ListByCharacterAsync(id!.Value);

            // El guerrero lleva espada, escudo y 2 pociones (content/classes/warrior.json).
            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => r.DefKey == "item.iron_sword" && r.Container == 1);
            Assert.Contains(rows, r => r.DefKey == "item.wooden_shield" && r.Container == 1);
            Assert.Contains(rows, r => r.DefKey == "item.health_potion" && r.Container == 0 && r.Quantity == 2);
        }
        finally
        {
            await DeleteAccountAsync(accountId);
        }
    }

    [PostgresFact]
    public async Task ListByCharacterAsync_NoDevuelveItemsDeOtroPersonaje()
    {
        Assert.True(Classes.TryGet("class.mage", out var mage));

        var characters = new CharacterRepository(_connections, Items);
        var itemRepo = new ItemRepository(_connections);
        var accountId = await CreateAccountAsync();

        try
        {
            var a = await characters.CreateAsync(accountId, 0, UniqueName(), mage!, paletteIndex: 0);
            var b = await characters.CreateAsync(accountId, 1, UniqueName(), mage!, paletteIndex: 0);

            var itemsOfA = await itemRepo.ListByCharacterAsync(a.Id!.Value);
            var itemsOfB = await itemRepo.ListByCharacterAsync(b.Id!.Value);

            Assert.NotEmpty(itemsOfA);
            Assert.NotEmpty(itemsOfB);
            Assert.DoesNotContain(itemsOfA, rowA => itemsOfB.Any(rowB => rowB.Id == rowA.Id));
        }
        finally
        {
            await DeleteAccountAsync(accountId);
        }
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

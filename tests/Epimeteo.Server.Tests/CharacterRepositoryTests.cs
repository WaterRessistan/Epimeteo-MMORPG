using Dapper;
using Epimeteo.Server.Content;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Characters;
using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Contra Postgres real, no mocks: es la única forma honesta de probar
/// <c>characters_account_slot_uq</c>, <c>characters_name_uq</c> y el borrado lógico
/// (FASE-03-personajes.md §9). Se salta si no hay Postgres configurado — ver
/// <see cref="PostgresFactAttribute"/>.
/// </summary>
public sealed class CharacterRepositoryTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());

    private static readonly ClassDefinition Warrior = new()
    {
        Key = "class.warrior",
        DisplayName = "Guerrero",
        BaseStr = 8,
        BaseInt = 2,
        BaseVit = 6,
        BaseDex = 4,
        BaseHp = 120,
        BaseMp = 20,
    };

    [PostgresFact]
    public async Task CreateAsync_LuegoListByAccountAsync_DevuelveElPersonajeCreado()
    {
        var repo = new CharacterRepository(_connections, Items);
        var accountId = await CreateAccountAsync();

        try
        {
            var (id, error) = await repo.CreateAsync(accountId, 0, UniqueName(), Warrior, paletteIndex: 1);
            Assert.Equal(CharacterCreateError.None, error);
            Assert.NotNull(id);

            var list = await repo.ListByAccountAsync(accountId);

            var character = Assert.Single(list);
            Assert.Equal(id, character.Id);
            Assert.Equal(0, character.Slot);
            Assert.Equal(Warrior.Key, character.ClassKey);
            Assert.Equal(Warrior.BaseHp, character.Hp);
            Assert.Equal(1, character.PaletteIndex);
        }
        finally
        {
            await DeleteAccountAsync(accountId);
        }
    }

    [PostgresFact]
    public async Task CreateAsync_ConSlotYaOcupado_DevuelveSlotOccupied()
    {
        var repo = new CharacterRepository(_connections, Items);
        var accountId = await CreateAccountAsync();

        try
        {
            var first = await repo.CreateAsync(accountId, 0, UniqueName(), Warrior, 0);
            Assert.Equal(CharacterCreateError.None, first.Error);

            var second = await repo.CreateAsync(accountId, 0, UniqueName(), Warrior, 0);
            Assert.Equal(CharacterCreateError.SlotOccupied, second.Error);
            Assert.Null(second.Id);
        }
        finally
        {
            await DeleteAccountAsync(accountId);
        }
    }

    [PostgresFact]
    public async Task CreateAsync_ConNombreYaUsado_DevuelveNameTaken()
    {
        var repo = new CharacterRepository(_connections, Items);
        var accountId = await CreateAccountAsync();
        var name = UniqueName();

        try
        {
            var first = await repo.CreateAsync(accountId, 0, name, Warrior, 0);
            Assert.Equal(CharacterCreateError.None, first.Error);

            var second = await repo.CreateAsync(accountId, 1, name, Warrior, 0);
            Assert.Equal(CharacterCreateError.NameTaken, second.Error);
        }
        finally
        {
            await DeleteAccountAsync(accountId);
        }
    }

    [PostgresFact]
    public async Task SoftDeleteAsync_LiberaElSlotParaUnPersonajeNuevo()
    {
        var repo = new CharacterRepository(_connections, Items);
        var accountId = await CreateAccountAsync();

        try
        {
            var first = await repo.CreateAsync(accountId, 2, UniqueName(), Warrior, 0);
            Assert.NotNull(first.Id);

            var deleted = await repo.SoftDeleteAsync(first.Id!.Value, accountId);
            Assert.True(deleted);

            var afterDelete = await repo.ListByAccountAsync(accountId);
            Assert.Empty(afterDelete);

            var second = await repo.CreateAsync(accountId, 2, UniqueName(), Warrior, 0);
            Assert.Equal(CharacterCreateError.None, second.Error);
        }
        finally
        {
            await DeleteAccountAsync(accountId);
        }
    }

    [PostgresFact]
    public async Task GetOwnedAsync_ConCuentaAjena_DevuelveNull()
    {
        var repo = new CharacterRepository(_connections, Items);
        var ownerAccountId = await CreateAccountAsync();
        var otherAccountId = await CreateAccountAsync();

        try
        {
            var created = await repo.CreateAsync(ownerAccountId, 0, UniqueName(), Warrior, 0);
            Assert.NotNull(created.Id);

            var owned = await repo.GetOwnedAsync(created.Id!.Value, otherAccountId);

            Assert.Null(owned);
        }
        finally
        {
            await DeleteAccountAsync(ownerAccountId);
            await DeleteAccountAsync(otherAccountId);
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
        // ON DELETE CASCADE en characters.account_id se lleva también los personajes de prueba.
        await connection.ExecuteAsync("DELETE FROM accounts WHERE id = @accountId", new { accountId });
    }
}

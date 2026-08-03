using Dapper;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Accounts;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Contra Postgres real, no mocks: es la única forma honesta de probar el índice único de
/// <c>username</c> y el <c>CHECK</c> de formato (FASE-02-persistencia.md §11). Se salta si no
/// hay Postgres configurado — ver <see cref="PostgresFactAttribute"/>.
/// </summary>
public sealed class AccountRepositoryTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);

    private static string UniqueUsername() => $"epi_test_{Guid.NewGuid():N}"[..20];

    [PostgresFact]
    public async Task CreateAsync_LuegoFindByUsernameAsync_DevuelveLaCuentaCreada()
    {
        var repo = new AccountRepository(_connections);
        var username = UniqueUsername();

        try
        {
            var id = await repo.CreateAsync(username, "test@example.com", "hash-de-prueba");
            Assert.NotNull(id);

            var account = await repo.FindByUsernameAsync(username);

            Assert.NotNull(account);
            Assert.Equal(id, account!.Id);
            Assert.Equal("hash-de-prueba", account.PasswordHash);
            Assert.Equal(AccountStatus.Active, account.Status);
        }
        finally
        {
            await DeleteAccountAsync(username);
        }
    }

    [PostgresFact]
    public async Task CreateAsync_ConUsernameYaExistente_DevuelveNull()
    {
        var repo = new AccountRepository(_connections);
        var username = UniqueUsername();

        try
        {
            var first = await repo.CreateAsync(username, null, "hash-1");
            Assert.NotNull(first);

            var second = await repo.CreateAsync(username, null, "hash-2");
            Assert.Null(second);
        }
        finally
        {
            await DeleteAccountAsync(username);
        }
    }

    [PostgresFact]
    public async Task FindByUsernameAsync_ConUsernameInexistente_DevuelveNull()
    {
        var repo = new AccountRepository(_connections);

        var account = await repo.FindByUsernameAsync(UniqueUsername());

        Assert.Null(account);
    }

    [PostgresFact]
    public async Task TouchLastLoginAsync_ConIpValida_ActualizaFechaEIp()
    {
        var repo = new AccountRepository(_connections);
        var username = UniqueUsername();

        try
        {
            var id = await repo.CreateAsync(username, null, "hash-de-prueba");
            Assert.NotNull(id);

            await repo.TouchLastLoginAsync(id!.Value, "203.0.113.7");

            await using var connection = await _connections.OpenAsync();
            var row = await connection.QuerySingleAsync<(DateTimeOffset? LastLoginAt, string? LastLoginIp)>(
                "SELECT last_login_at AS LastLoginAt, host(last_login_ip) AS LastLoginIp FROM accounts WHERE id = @id",
                new { id });

            Assert.NotNull(row.LastLoginAt);
            Assert.Equal("203.0.113.7", row.LastLoginIp);
        }
        finally
        {
            await DeleteAccountAsync(username);
        }
    }

    private async Task DeleteAccountAsync(string username)
    {
        await using var connection = await _connections.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM accounts WHERE username = @username", new { username });
    }
}

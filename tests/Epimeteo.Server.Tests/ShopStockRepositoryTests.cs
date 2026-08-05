using Dapper;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Economy;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Contra Postgres real: <c>UPSERT</c> de <c>shop_stock</c>, una fila por (tienda, ítem).</summary>
public sealed class ShopStockRepositoryTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);

    private static string UniqueShopKey() => $"shop.test_{Guid.NewGuid():N}"[..24];

    [PostgresFact]
    public async Task UpsertAsync_LuegoListAllAsync_DevuelveLaFila()
    {
        var repo = new ShopStockRepository(_connections);
        var shopKey = UniqueShopKey();
        var restockAt = DateTimeOffset.UtcNow.AddHours(6);

        try
        {
            await repo.UpsertAsync(shopKey, "item.iron_sword", stock: 3, stockMax: 5, restockAt);

            var rows = await repo.ListAllAsync();
            var row = Assert.Single(rows, r => r.ShopKey == shopKey);
            Assert.Equal("item.iron_sword", row.DefKey);
            Assert.Equal(3, row.Stock);
            Assert.Equal(5, row.StockMax);
        }
        finally
        {
            await DeleteAsync(shopKey);
        }
    }

    [PostgresFact]
    public async Task UpsertAsync_DosVeces_ActualizaLaMismaFilaEnVezDeDuplicar()
    {
        var repo = new ShopStockRepository(_connections);
        var shopKey = UniqueShopKey();

        try
        {
            await repo.UpsertAsync(shopKey, "item.iron_sword", stock: 5, stockMax: 5, DateTimeOffset.UtcNow);
            await repo.UpsertAsync(shopKey, "item.iron_sword", stock: 2, stockMax: 5, DateTimeOffset.UtcNow.AddHours(1));

            var rows = await repo.ListAllAsync();
            var matching = rows.Where(r => r.ShopKey == shopKey && r.DefKey == "item.iron_sword").ToList();

            var row = Assert.Single(matching);
            Assert.Equal(2, row.Stock);
        }
        finally
        {
            await DeleteAsync(shopKey);
        }
    }

    private async Task DeleteAsync(string shopKey)
    {
        await using var connection = await _connections.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM shop_stock WHERE shop_key = @shopKey", new { shopKey });
    }
}

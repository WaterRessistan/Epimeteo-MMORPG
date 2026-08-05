using Dapper;

namespace Epimeteo.Server.Persistence.Economy;

/// <summary>Acceso Dapper a <c>shop_stock</c>: carga inicial y <c>UPSERT</c> por entrada.</summary>
public sealed class ShopStockRepository(NpgsqlConnectionFactory connections)
{
    /// <summary>Todo lo guardado, de todas las tiendas. Se llama una vez al arrancar (<c>ShopRuntime</c>).</summary>
    public async Task<IReadOnlyList<ShopStockRow>> ListAllAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ShopStockRow>(
            new CommandDefinition(
                """
                SELECT shop_key AS "ShopKey", def_key AS "DefKey", stock AS "Stock", stock_max AS "StockMax",
                       price_buy AS "PriceBuy", price_sell AS "PriceSell", restock_at AS "RestockAt"
                  FROM shop_stock
                """,
                cancellationToken: ct)).ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task UpsertAsync(
        string shopKey, string defKey, int stock, int stockMax, DateTimeOffset restockAt, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shop_stock (shop_key, def_key, stock, stock_max, restock_at)
                VALUES (@shopKey, @defKey, @stock, @stockMax, @restockAt)
                ON CONFLICT (shop_key, def_key) DO UPDATE
                    SET stock = @stock, stock_max = @stockMax, restock_at = @restockAt
                """,
                new { shopKey, defKey, stock, stockMax, restockAt },
                cancellationToken: ct)).ConfigureAwait(false);
    }
}

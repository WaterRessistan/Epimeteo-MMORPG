namespace Epimeteo.Server.Persistence.Economy;

/// <summary>Fila cruda de <c>shop_stock</c>, tal como sale de Dapper. Sólo transporte.</summary>
public sealed record ShopStockRow
{
    public required string ShopKey { get; init; }

    public required string DefKey { get; init; }

    public required int Stock { get; init; }

    public required int StockMax { get; init; }

    public long? PriceBuy { get; init; }

    public long? PriceSell { get; init; }

    public DateTimeOffset? RestockAt { get; init; }
}

namespace Epimeteo.Shared.Data;

/// <summary>
/// Una entrada del catálogo de una tienda. El orden dentro de <c>ShopDefinition.Items</c> **es**
/// el <c>shopSlot</c> del protocolo (<c>docs/01</c>: <c>ShopBuy{shopSlot,...}</c>) — estable
/// mientras no se reordene el JSON a mano.
/// </summary>
public sealed record ShopItemDefinition
{
    public required string DefKey { get; init; }

    public required long PriceBuy { get; init; }

    public required long PriceSell { get; init; }

    /// <summary><c>null</c> = stock infinito (<c>docs/02</c>).</summary>
    public int? StockMax { get; init; }
}

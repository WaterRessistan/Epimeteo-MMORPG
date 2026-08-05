using System.Diagnostics.CodeAnalysis;
using Epimeteo.Server.Persistence.Economy;
using Epimeteo.Shared.Data;

namespace Epimeteo.Server.Shop;

/// <summary>
/// El stock en memoria de todas las tiendas, autoritativo mientras dura el proceso — no por zona:
/// una tienda es una entidad económica única aunque su NPC esté en un mapa concreto (FASE-07 §5).
/// Se construye una vez al arrancar con lo que hubiera guardado en <c>shop_stock</c> (si no había
/// fila para un ítem, arranca a <c>stockMax</c>, como si estuviera recién repuesta) y vive el
/// resto de la sesión sólo en memoria: el guardado a Postgres es asíncrono, igual que
/// <c>InventorySaver</c> (FASE-06 §2 D2, FASE-07 §2 D1).
/// </summary>
public sealed class ShopRuntime
{
    private readonly Dictionary<string, Dictionary<string, ShopStockState>> _byShop = new(StringComparer.Ordinal);

    public ShopRuntime(ShopCatalog shops, IReadOnlyList<ShopStockRow> savedRows)
    {
        var saved = savedRows.ToDictionary(row => (row.ShopKey, row.DefKey));

        foreach (var shop in shops.All)
        {
            var items = new Dictionary<string, ShopStockState>(StringComparer.Ordinal);

            foreach (var itemDef in shop.Items)
            {
                items[itemDef.DefKey] = saved.TryGetValue((shop.Key, itemDef.DefKey), out var row)
                    ? new ShopStockState
                    {
                        DefKey = itemDef.DefKey,
                        Stock = itemDef.StockMax is null ? null : row.Stock,
                        PriceBuyOverride = row.PriceBuy,
                        PriceSellOverride = row.PriceSell,
                        RestockAt = row.RestockAt ?? DateTimeOffset.UtcNow.AddMinutes(shop.RestockMinutes),
                    }
                    : new ShopStockState
                    {
                        DefKey = itemDef.DefKey,
                        Stock = itemDef.StockMax,
                        RestockAt = DateTimeOffset.UtcNow.AddMinutes(shop.RestockMinutes),
                    };
            }

            _byShop[shop.Key] = items;
        }
    }

    public bool TryGetShopStock(string shopKey, [MaybeNullWhen(false)] out IReadOnlyDictionary<string, ShopStockState> stock)
    {
        if (_byShop.TryGetValue(shopKey, out var items))
        {
            stock = items;
            return true;
        }

        stock = null;
        return false;
    }

    /// <summary>
    /// Repone a <c>stockMax</c> todo lo que le toque a cada tienda (FASE-07 §2 D8: un
    /// temporizador por tienda entera, no por ítem). Devuelve las tiendas que cambiaron, para que
    /// quien llame decida si persiste y si avisa a quien la tenga abierta.
    /// </summary>
    public IReadOnlyList<string> SweepRestock(ShopCatalog shops, DateTimeOffset now)
    {
        List<string>? restocked = null;

        foreach (var (shopKey, items) in _byShop)
        {
            if (!shops.TryGet(shopKey, out var shopDef))
            {
                continue;
            }

            var changed = false;

            foreach (var itemDef in shopDef.Items)
            {
                if (itemDef.StockMax is null)
                {
                    continue; // Infinito: nada que reponer.
                }

                var state = items[itemDef.DefKey];
                if (now < state.RestockAt)
                {
                    continue;
                }

                state.Stock = itemDef.StockMax;
                state.RestockAt = now.AddMinutes(shopDef.RestockMinutes);
                changed = true;
            }

            if (changed)
            {
                (restocked ??= []).Add(shopKey);
            }
        }

        return restocked ?? [];
    }
}

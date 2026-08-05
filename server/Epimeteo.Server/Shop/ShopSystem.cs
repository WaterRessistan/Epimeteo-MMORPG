using Epimeteo.Server.Content;
using Epimeteo.Server.Inventory;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Shop;

/// <summary>Resultado de una acción de tienda. Nunca lanza (CLAUDE.md §4).</summary>
public readonly record struct ShopOpResult(bool Ok, ResultCode Code, IReadOnlyList<SlotRef> InventoryTouched, long NewGold)
{
    public static ShopOpResult Fail(ResultCode code, long currentGold) => new(false, code, [], currentGold);

    public static ShopOpResult Success(long newGold, params SlotRef[] touched) => new(true, ResultCode.Ok, touched, newGold);
}

/// <summary>
/// Comprar, vender y reparar — puro dado un <see cref="PlayerInventory"/>, el oro actual y el
/// stock de una tienda, sin I/O (mismo espíritu que <c>InventorySystem</c>, FASE-06). La
/// distancia al NPC y si el jugador tiene la tienda abierta **no** se comprueban aquí: son
/// estado del jugador, no de la tienda, y los valida <c>GameWorld</c> antes de llamar (mismo
/// reparto de capas que ya usa <c>InventorySystem</c> con los huecos bien formados).
/// </summary>
public static class ShopSystem
{
    /// <summary>Oro que cuesta cada punto de durabilidad recuperado (FASE-07 §2 D6).</summary>
    private const int RepairCostPerDurabilityPoint = 2;

    public static ShopOpResult TryBuy(
        PlayerInventory inventory, ItemCatalog items, ShopDefinition shop,
        IReadOnlyDictionary<string, ShopStockState> stock, long currentGold,
        byte shopSlot, int quantity, long expectedPrice)
    {
        if (shopSlot >= shop.Items.Length)
        {
            return ShopOpResult.Fail(ResultCode.ItemNotFound, currentGold);
        }

        if (quantity <= 0)
        {
            return ShopOpResult.Fail(ResultCode.NotEnoughItems, currentGold);
        }

        var shopItem = shop.Items[shopSlot];
        if (!items.TryGet(shopItem.DefKey, out var itemDef))
        {
            return ShopOpResult.Fail(ResultCode.UnknownError, currentGold);
        }

        var state = stock[shopItem.DefKey];
        if (state.Stock is { } available && available < quantity)
        {
            return ShopOpResult.Fail(ResultCode.OutOfStock, currentGold);
        }

        var unitPrice = state.PriceBuyOverride ?? shopItem.PriceBuy;
        var totalPrice = unitPrice * quantity;
        if (totalPrice != expectedPrice)
        {
            return ShopOpResult.Fail(ResultCode.PriceChanged, currentGold);
        }

        if (currentGold < totalPrice)
        {
            return ShopOpResult.Fail(ResultCode.NotEnoughGold, currentGold);
        }

        // Un ítem recién comprado nace con la durabilidad de fábrica llena, si el ítem se
        // desgasta (FASE-07 §4).
        var add = InventorySystem.TryAddNew(
            inventory, items, shopItem.DefKey, quantity,
            durability: itemDef.DurabilityMax, durabilityMax: itemDef.DurabilityMax);

        if (!add.Ok)
        {
            // Nada cambió: TryAddNew comprueba la capacidad entera antes de tocar nada.
            return ShopOpResult.Fail(add.Code, currentGold);
        }

        if (state.Stock is not null)
        {
            state.Stock -= quantity;
        }

        return new ShopOpResult(true, ResultCode.Ok, add.Touched, currentGold - totalPrice);
    }

    public static ShopOpResult TrySell(
        PlayerInventory inventory, ShopDefinition shop, IReadOnlyDictionary<string, ShopStockState> stock,
        long currentGold, ContainerId container, byte slot, int quantity, long expectedPrice)
    {
        if (container == ContainerId.Equipped)
        {
            return ShopOpResult.Fail(ResultCode.NotEquippable, currentGold);
        }

        var stackItem = inventory.Find(container, slot);
        if (stackItem is null)
        {
            return ShopOpResult.Fail(ResultCode.ItemNotFound, currentGold);
        }

        if (quantity <= 0 || quantity > stackItem.Quantity)
        {
            return ShopOpResult.Fail(ResultCode.NotEnoughItems, currentGold);
        }

        // Una tienda sólo recompra lo que ella misma vende (FASE-07 §2 D5): evita que cualquier
        // tienda se convierta en un vertedero universal de todo lo que un jugador no quiere.
        var shopItem = Array.Find(shop.Items, item => item.DefKey == stackItem.DefKey);
        if (shopItem is null)
        {
            return ShopOpResult.Fail(ResultCode.ItemNotFound, currentGold);
        }

        var state = stock[shopItem.DefKey];
        var unitPrice = state.PriceSellOverride ?? shopItem.PriceSell;
        var totalPrice = unitPrice * quantity;
        if (totalPrice != expectedPrice)
        {
            return ShopOpResult.Fail(ResultCode.PriceChanged, currentGold);
        }

        stackItem.Quantity -= quantity;
        if (stackItem.Quantity == 0)
        {
            inventory.Remove(stackItem);
        }

        if (state.Stock is { } current && shopItem.StockMax is { } max)
        {
            state.Stock = Math.Min(max, current + quantity);
        }

        return ShopOpResult.Success(currentGold + totalPrice, new SlotRef(container, slot));
    }

    public static ShopOpResult TryRepair(
        PlayerInventory inventory, ItemCatalog items, ShopDefinition shop, long currentGold,
        ContainerId container, byte slot)
    {
        if (!shop.CanRepair)
        {
            return ShopOpResult.Fail(ResultCode.NotEquippable, currentGold);
        }

        var stackItem = inventory.Find(container, slot);
        if (stackItem is null)
        {
            return ShopOpResult.Fail(ResultCode.ItemNotFound, currentGold);
        }

        if (!items.TryGet(stackItem.DefKey, out var def) || def.DurabilityMax is not { } maxDurability)
        {
            return ShopOpResult.Fail(ResultCode.NotEquippable, currentGold);
        }

        var missing = maxDurability - (stackItem.Durability ?? 0);
        if (missing <= 0)
        {
            // Ya está al máximo: éxito sin cambios, mismo criterio que apilar ya al máximo (Fase 6).
            return ShopOpResult.Success(currentGold);
        }

        var cost = (long)missing * RepairCostPerDurabilityPoint;
        if (currentGold < cost)
        {
            return ShopOpResult.Fail(ResultCode.NotEnoughGold, currentGold);
        }

        stackItem.Durability = maxDurability;
        stackItem.DurabilityMax = maxDurability;

        return ShopOpResult.Success(currentGold - cost, new SlotRef(container, slot));
    }
}

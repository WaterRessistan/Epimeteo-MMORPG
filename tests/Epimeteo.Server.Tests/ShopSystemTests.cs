using Epimeteo.Server.Content;
using Epimeteo.Server.Inventory;
using Epimeteo.Server.Shop;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Sin Postgres ni tick: <see cref="ShopSystem"/> es puro sobre <see cref="PlayerInventory"/> +
/// oro + <see cref="ShopStockState"/>, igual que <c>InventorySystemTests</c> lo es para
/// inventario. Usa el catálogo real de <c>content/shops/</c> y <c>content/items/</c>.
/// </summary>
public sealed class ShopSystemTests
{
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());
    private static readonly ShopCatalog Shops = new(ContentPaths.ResolveContentRoot());

    private static ShopDefinition Armory => Shops.TryGet("shop.armory", out var shop) ? shop! : throw new InvalidOperationException();

    private static ShopDefinition GeneralStore =>
        Shops.TryGet("shop.general_store", out var shop) ? shop! : throw new InvalidOperationException();

    private static Dictionary<string, ShopStockState> FreshStock(ShopDefinition shop) => shop.Items.ToDictionary(
        item => item.DefKey,
        item => new ShopStockState { DefKey = item.DefKey, Stock = item.StockMax });

    private static byte SlotOf(ShopDefinition shop, string defKey) =>
        (byte)Array.FindIndex(shop.Items, item => item.DefKey == defKey);

    // ── Comprar ──────────────────────────────────────────────────────────

    [Fact]
    public void Buy_ConPrecioEsperadoCorrecto_DescuentaOroYAñadeElItem()
    {
        var shop = Armory;
        var stock = FreshStock(shop);
        var inv = new PlayerInventory([]);
        var slot = SlotOf(shop, "item.iron_sword");
        var price = shop.Items[slot].PriceBuy;

        var result = ShopSystem.TryBuy(inv, Items, shop, stock, currentGold: 100, slot, quantity: 1, expectedPrice: price);

        Assert.True(result.Ok);
        Assert.Equal(100 - price, result.NewGold);
        Assert.NotNull(inv.Find(ContainerId.WeaponBag, 0));
        Assert.Equal("item.iron_sword", inv.Find(ContainerId.WeaponBag, 0)!.DefKey);
    }

    [Fact]
    public void Buy_ElItemComprado_NaceConDurabilidadLlena()
    {
        var shop = Armory;
        var stock = FreshStock(shop);
        var inv = new PlayerInventory([]);
        var slot = SlotOf(shop, "item.iron_sword");
        Assert.True(Items.TryGet("item.iron_sword", out var def));

        ShopSystem.TryBuy(inv, Items, shop, stock, currentGold: 1000, slot, quantity: 1, expectedPrice: shop.Items[slot].PriceBuy);

        var bought = inv.Find(ContainerId.WeaponBag, 0);
        Assert.Equal(def!.DurabilityMax, bought!.Durability);
        Assert.Equal(def.DurabilityMax, bought.DurabilityMax);
    }

    [Fact]
    public void Buy_ConPrecioEsperadoIncorrecto_Falla()
    {
        var shop = Armory;
        var stock = FreshStock(shop);
        var inv = new PlayerInventory([]);
        var slot = SlotOf(shop, "item.iron_sword");

        var result = ShopSystem.TryBuy(inv, Items, shop, stock, currentGold: 1000, slot, quantity: 1, expectedPrice: 1);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.PriceChanged, result.Code);
        Assert.Equal(1000, result.NewGold);
        Assert.Empty(inv.Stacks);
    }

    [Fact]
    public void Buy_SinOroSuficiente_Falla()
    {
        var shop = Armory;
        var stock = FreshStock(shop);
        var inv = new PlayerInventory([]);
        var slot = SlotOf(shop, "item.iron_sword");
        var price = shop.Items[slot].PriceBuy;

        var result = ShopSystem.TryBuy(inv, Items, shop, stock, currentGold: price - 1, slot, quantity: 1, expectedPrice: price);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEnoughGold, result.Code);
        Assert.Empty(inv.Stacks);
    }

    [Fact]
    public void Buy_ConLaBolsaLlena_FallaYNoDescuentaOro()
    {
        var shop = Armory;
        var stock = FreshStock(shop);
        var stacks = new List<ItemStack>();
        for (byte s = 0; s < InventoryConstants.WeaponBagCapacity; s++)
        {
            stacks.Add(new ItemStack { DefKey = "item.wooden_shield", Container = ContainerId.WeaponBag, Slot = s, Quantity = 1 });
        }

        var inv = new PlayerInventory(stacks);
        var slot = SlotOf(shop, "item.iron_sword");
        var price = shop.Items[slot].PriceBuy;

        var result = ShopSystem.TryBuy(inv, Items, shop, stock, currentGold: 1000, slot, quantity: 1, expectedPrice: price);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.InventoryFull, result.Code);
        Assert.Equal(1000, result.NewGold);
    }

    [Fact]
    public void Buy_AgotandoElStockExacto_FuncionaYDejaStockEnCero()
    {
        var shop = GeneralStore;
        var stock = FreshStock(shop);
        var slot = SlotOf(shop, "item.iron_ore");
        var maxStock = shop.Items[slot].StockMax!.Value;
        var price = shop.Items[slot].PriceBuy * maxStock;
        var inv = new PlayerInventory([]);

        var result = ShopSystem.TryBuy(inv, Items, shop, stock, currentGold: 10_000, slot, quantity: maxStock, expectedPrice: price);

        Assert.True(result.Ok);
        Assert.Equal(0, stock["item.iron_ore"].Stock);
    }

    [Fact]
    public void Buy_MasDeLoQueHayEnStock_OutOfStock()
    {
        var shop = GeneralStore;
        var stock = FreshStock(shop);
        var slot = SlotOf(shop, "item.iron_ore");
        var maxStock = shop.Items[slot].StockMax!.Value;
        var quantity = maxStock + 1;
        var price = shop.Items[slot].PriceBuy * quantity;
        var inv = new PlayerInventory([]);

        var result = ShopSystem.TryBuy(inv, Items, shop, stock, currentGold: 100_000, slot, quantity, expectedPrice: price);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.OutOfStock, result.Code);
    }

    // ── Vender ───────────────────────────────────────────────────────────

    [Fact]
    public void Sell_SubeElOroYElStockDeLaTienda_BajaElInventario()
    {
        var shop = Armory;
        var stock = FreshStock(shop);
        // Un hueco por debajo del máximo, para que vender de verdad tenga sitio donde subir
        // (el stock nunca pasa de stockMax, aunque se venda más de lo que cabría).
        stock["item.wooden_shield"].Stock -= 1;
        var initialStock = stock["item.wooden_shield"].Stock;
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.wooden_shield", Container = ContainerId.WeaponBag, Slot = 0, Quantity = 1 },
        ]);
        var shopItem = Array.Find(shop.Items, i => i.DefKey == "item.wooden_shield")!;

        var result = ShopSystem.TrySell(inv, shop, stock, currentGold: 50, ContainerId.WeaponBag, 0, quantity: 1, expectedPrice: shopItem.PriceSell);

        Assert.True(result.Ok);
        Assert.Equal(50 + shopItem.PriceSell, result.NewGold);
        Assert.Null(inv.Find(ContainerId.WeaponBag, 0));
        Assert.Equal(initialStock + 1, stock["item.wooden_shield"].Stock);
    }

    [Fact]
    public void Sell_AlgoQueLaTiendaNoCompra_Falla()
    {
        var shop = Armory; // no vende pociones
        var stock = FreshStock(shop);
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.health_potion", Container = ContainerId.General, Slot = 0, Quantity = 3 },
        ]);

        var result = ShopSystem.TrySell(inv, shop, stock, currentGold: 0, ContainerId.General, 0, quantity: 1, expectedPrice: 1);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.ItemNotFound, result.Code);
    }

    [Fact]
    public void Sell_MasCantidadDeLaQueHay_Falla()
    {
        var shop = Armory;
        var stock = FreshStock(shop);
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.wooden_shield", Container = ContainerId.WeaponBag, Slot = 0, Quantity = 1 },
        ]);
        var shopItem = Array.Find(shop.Items, i => i.DefKey == "item.wooden_shield")!;

        var result = ShopSystem.TrySell(
            inv, shop, stock, currentGold: 0, ContainerId.WeaponBag, 0, quantity: 2, expectedPrice: shopItem.PriceSell * 2);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEnoughItems, result.Code);
    }

    // ── Reparar ──────────────────────────────────────────────────────────

    [Fact]
    public void Repair_RestauraLaDurabilidadYCobraLoJusto()
    {
        var shop = Armory;
        Assert.True(Items.TryGet("item.iron_sword", out var def));
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.iron_sword", Container = ContainerId.Equipped, Slot = 0, Quantity = 1, Durability = 40, DurabilityMax = def!.DurabilityMax },
        ]);
        var missing = def.DurabilityMax!.Value - 40;

        var result = ShopSystem.TryRepair(inv, Items, shop, currentGold: 1000, ContainerId.Equipped, 0);

        Assert.True(result.Ok);
        Assert.Equal(1000 - (missing * 2), result.NewGold);
        Assert.Equal(def.DurabilityMax, inv.Find(ContainerId.Equipped, 0)!.Durability);
    }

    [Fact]
    public void Repair_YaAlMaximo_ExitoSinCambios()
    {
        var shop = Armory;
        Assert.True(Items.TryGet("item.iron_sword", out var def));
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.iron_sword", Container = ContainerId.Equipped, Slot = 0, Quantity = 1, Durability = def!.DurabilityMax, DurabilityMax = def.DurabilityMax },
        ]);

        var result = ShopSystem.TryRepair(inv, Items, shop, currentGold: 1000, ContainerId.Equipped, 0);

        Assert.True(result.Ok);
        Assert.Equal(1000, result.NewGold);
    }

    [Fact]
    public void Repair_UnItemSinDurabilidad_NotEquippable()
    {
        var shop = Armory;
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.health_potion", Container = ContainerId.General, Slot = 0, Quantity = 1 },
        ]);

        var result = ShopSystem.TryRepair(inv, Items, shop, currentGold: 1000, ContainerId.General, 0);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEquippable, result.Code);
    }

    [Fact]
    public void Repair_EnTiendaSinCanRepair_NotEquippable()
    {
        var shop = GeneralStore;
        Assert.True(Items.TryGet("item.iron_sword", out var def));
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.iron_sword", Container = ContainerId.Equipped, Slot = 0, Quantity = 1, Durability = 10, DurabilityMax = def!.DurabilityMax },
        ]);

        var result = ShopSystem.TryRepair(inv, Items, shop, currentGold: 1000, ContainerId.Equipped, 0);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEquippable, result.Code);
    }

    [Fact]
    public void Repair_SinOroSuficiente_Falla()
    {
        var shop = Armory;
        Assert.True(Items.TryGet("item.iron_sword", out var def));
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.iron_sword", Container = ContainerId.Equipped, Slot = 0, Quantity = 1, Durability = 0, DurabilityMax = def!.DurabilityMax },
        ]);

        var result = ShopSystem.TryRepair(inv, Items, shop, currentGold: 0, ContainerId.Equipped, 0);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEnoughGold, result.Code);
    }
}

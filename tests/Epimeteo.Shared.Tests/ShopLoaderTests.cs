using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class ShopLoaderTests
{
    private static string ShopJson(
        string key = "shop.test",
        bool canRepair = false,
        int restockMinutes = 60,
        string npcMapKey = "map.test",
        string npcName = "Tendero",
        int npcFacing = 2,
        string items = """[{ "defKey": "item.a", "priceBuy": 10, "priceSell": 3 }]""")
    {
        return $$"""
        {
          "key": "{{key}}",
          "displayName": "Tienda de prueba",
          "canRepair": {{(canRepair ? "true" : "false")}},
          "restockMinutes": {{restockMinutes}},
          "npc": { "mapKey": "{{npcMapKey}}", "x": 1.5, "y": 1.5, "facing": {{npcFacing}}, "name": "{{npcName}}", "paletteIndex": 0 },
          "items": {{items}}
        }
        """;
    }

    [Fact]
    public void UnaTiendaValida_SeCargaEntera()
    {
        var shop = ShopLoader.Parse(ShopJson(), "test");

        Assert.Equal("shop.test", shop.Key);
        Assert.Equal("map.test", shop.Npc.MapKey);
        Assert.Single(shop.Items);
        Assert.Equal("item.a", shop.Items[0].DefKey);
        Assert.Null(shop.Items[0].StockMax);
    }

    [Fact]
    public void StockMaxAusente_EsInfinito()
    {
        var shop = ShopLoader.Parse(ShopJson(items: """[{ "defKey": "item.a", "priceBuy": 10, "priceSell": 3 }]"""), "test");

        Assert.Null(shop.Items[0].StockMax);
    }

    [Fact]
    public void StockMaxPresente_SeRespeta()
    {
        var shop = ShopLoader.Parse(
            ShopJson(items: """[{ "defKey": "item.a", "priceBuy": 10, "priceSell": 3, "stockMax": 5 }]"""), "test");

        Assert.Equal(5, shop.Items[0].StockMax);
    }

    [Fact]
    public void FaltaKey_Falla()
    {
        var json = """{ "npc": { "mapKey": "m", "name": "T" }, "items": [{ "defKey": "a", "priceBuy": 1, "priceSell": 1 }] }""";

        Assert.Throws<InvalidOperationException>(() => ShopLoader.Parse(json, "test"));
    }

    [Fact]
    public void ItemsVacio_Falla()
    {
        Assert.Throws<InvalidOperationException>(() => ShopLoader.Parse(ShopJson(items: "[]"), "test"));
    }

    [Fact]
    public void ClaveDeItemRepetida_Falla()
    {
        var items = """[{ "defKey": "item.a", "priceBuy": 1, "priceSell": 1 }, { "defKey": "item.a", "priceBuy": 2, "priceSell": 1 }]""";

        Assert.Throws<InvalidOperationException>(() => ShopLoader.Parse(ShopJson(items: items), "test"));
    }

    [Fact]
    public void PrecioNegativo_Falla()
    {
        var items = """[{ "defKey": "item.a", "priceBuy": -1, "priceSell": 1 }]""";

        Assert.Throws<InvalidOperationException>(() => ShopLoader.Parse(ShopJson(items: items), "test"));
    }

    [Fact]
    public void StockMaxCero_Falla()
    {
        var items = """[{ "defKey": "item.a", "priceBuy": 1, "priceSell": 1, "stockMax": 0 }]""";

        Assert.Throws<InvalidOperationException>(() => ShopLoader.Parse(ShopJson(items: items), "test"));
    }

    [Fact]
    public void FaltaNpc_Falla()
    {
        var json = """{ "key": "shop.x", "items": [{ "defKey": "a", "priceBuy": 1, "priceSell": 1 }] }""";

        Assert.Throws<InvalidOperationException>(() => ShopLoader.Parse(json, "test"));
    }

    [Fact]
    public void NpcFacingFueraDeRango_Falla()
    {
        Assert.Throws<InvalidOperationException>(() => ShopLoader.Parse(ShopJson(npcFacing: 7), "test"));
    }

    [Fact]
    public void RestockMinutesMenorQueUno_Falla()
    {
        Assert.Throws<InvalidOperationException>(() => ShopLoader.Parse(ShopJson(restockMinutes: 0), "test"));
    }

    [Fact]
    public void CanRepair_SePreservaTalCual()
    {
        Assert.True(ShopLoader.Parse(ShopJson(canRepair: true), "test").CanRepair);
        Assert.False(ShopLoader.Parse(ShopJson(canRepair: false), "test").CanRepair);
    }
}

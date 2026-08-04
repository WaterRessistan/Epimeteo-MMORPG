using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class ItemLoaderTests
{
    private static string ItemJson(
        string key = "item.test_sword",
        string type = "Weapon",
        int maxStack = 1,
        string? equipCategory = "MainHand",
        int bonusStr = 2,
        int healAmount = 0)
    {
        var equipLine = equipCategory is null ? "" : $$"""
            , "equipCategory": "{{equipCategory}}"
            """;

        return $$"""
        {
          "key": "{{key}}",
          "displayName": "Espada de prueba",
          "type": "{{type}}",
          "maxStack": {{maxStack}}
          {{equipLine}}
          , "bonusStr": {{bonusStr}}
          , "healAmount": {{healAmount}}
        }
        """;
    }

    [Fact]
    public void UnArmaValida_SeCargaEntera()
    {
        var item = ItemLoader.Parse(ItemJson(), "test");

        Assert.Equal("item.test_sword", item.Key);
        Assert.Equal(ItemType.Weapon, item.Type);
        Assert.Equal(EquipCategory.MainHand, item.EquipCategory);
        Assert.Equal(2, item.BonusStr);
    }

    [Fact]
    public void UnConsumible_SinEquipCategory_SeCargaEntero()
    {
        var json = ItemJson(key: "item.potion", type: "Consumable", maxStack: 20, equipCategory: null, healAmount: 30);

        var item = ItemLoader.Parse(json, "test");

        Assert.Equal(ItemType.Consumable, item.Type);
        Assert.Null(item.EquipCategory);
        Assert.Equal(30, item.HealAmount);
        Assert.Equal(20, item.MaxStack);
    }

    [Fact]
    public void FaltaKey_Falla()
    {
        var json = """{ "type": "Material", "maxStack": 1 }""";

        Assert.Throws<InvalidOperationException>(() => ItemLoader.Parse(json, "test"));
    }

    [Fact]
    public void TypeDesconocido_Falla()
    {
        var json = ItemJson(type: "Gadget", equipCategory: null);

        Assert.Throws<InvalidOperationException>(() => ItemLoader.Parse(json, "test"));
    }

    [Fact]
    public void MaxStackMenorQueUno_Falla()
    {
        var json = ItemJson(maxStack: 0);

        Assert.Throws<InvalidOperationException>(() => ItemLoader.Parse(json, "test"));
    }

    [Fact]
    public void ArmaSinEquipCategory_Falla()
    {
        var json = ItemJson(type: "Weapon", equipCategory: null);

        Assert.Throws<InvalidOperationException>(() => ItemLoader.Parse(json, "test"));
    }

    [Fact]
    public void ConsumibleConEquipCategory_Falla()
    {
        var json = ItemJson(type: "Consumable", equipCategory: "MainHand");

        Assert.Throws<InvalidOperationException>(() => ItemLoader.Parse(json, "test"));
    }

    [Fact]
    public void EquipCategoryDesconocida_Falla()
    {
        var json = ItemJson(equipCategory: "Wings");

        Assert.Throws<InvalidOperationException>(() => ItemLoader.Parse(json, "test"));
    }

    [Fact]
    public void SinDisplayName_UsaLaClave()
    {
        var json = """{ "key": "item.nameless", "type": "Material", "maxStack": 5 }""";

        var item = ItemLoader.Parse(json, "test");

        Assert.Equal("item.nameless", item.DisplayName);
    }
}

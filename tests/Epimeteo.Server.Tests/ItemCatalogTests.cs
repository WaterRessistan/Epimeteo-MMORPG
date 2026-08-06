using Epimeteo.Server.Content;
using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Sin Postgres: valida el contenido real de <c>content/items/</c>, incluidos los
/// <c>startingItems</c> de <c>content/classes/</c> (que referencian estas claves por texto, sin
/// comprobación en tiempo de compilación).
/// </summary>
public sealed class ItemCatalogTests
{
    private static ItemCatalog LoadItems() => new(ContentPaths.ResolveContentRoot());

    private static ClassCatalog LoadClasses() => new(ContentPaths.ResolveContentRoot());

    [Theory]
    [InlineData("item.iron_sword", ItemType.Weapon)]
    [InlineData("item.wooden_shield", ItemType.Weapon)]
    [InlineData("item.leather_chest", ItemType.Armor)]
    [InlineData("item.copper_ring", ItemType.Armor)]
    [InlineData("item.health_potion", ItemType.Consumable)]
    [InlineData("item.iron_ore", ItemType.Material)]
    [InlineData("item.wheat_seed", ItemType.Seed)]
    [InlineData("item.wheat", ItemType.Material)]
    [InlineData("item.hoe", ItemType.Weapon)]
    [InlineData("item.watering_can", ItemType.Weapon)]
    public void Constructor_CargaLosDiezItems(string key, ItemType expectedType)
    {
        var catalog = LoadItems();

        Assert.True(catalog.TryGet(key, out var item));
        Assert.Equal(expectedType, item!.Type);
    }

    [Fact]
    public void SonDiezItems_NiUnoMas()
    {
        Assert.Equal(10, LoadItems().All.Count);
    }

    [Theory]
    [InlineData("item.hoe", FarmToolAction.Till)]
    [InlineData("item.watering_can", FarmToolAction.Water)]
    public void HerramientasDeGranja_DeclaranSuAccion(string key, FarmToolAction expected)
    {
        Assert.True(LoadItems().TryGet(key, out var tool));
        Assert.Equal(EquipCategory.Tool, tool!.EquipCategory);
        Assert.Equal(expected, tool.FarmToolAction);
    }

    [Fact]
    public void CopperRing_EsDeCategoriaAnillo()
    {
        Assert.True(LoadItems().TryGet("item.copper_ring", out var ring));
        Assert.Equal(EquipCategory.Ring, ring!.EquipCategory);
    }

    [Fact]
    public void HealthPotion_CuraYNoEsEquipable()
    {
        Assert.True(LoadItems().TryGet("item.health_potion", out var potion));
        Assert.True(potion!.HealAmount > 0);
        Assert.Null(potion.EquipCategory);
    }

    /// <summary>
    /// Cada <c>defKey</c> de cada <c>startingItems</c> tiene que existir de verdad en el catálogo
    /// de ítems: si alguien renombra un ítem sin actualizar las clases, esto lo caza en el test
    /// en vez de en producción con un personaje recién creado sin su kit.
    /// </summary>
    [Fact]
    public void TodosLosStartingItems_ExistenEnElCatalogo()
    {
        var items = LoadItems();
        var classes = LoadClasses();

        foreach (var classDef in classes.All)
        {
            foreach (var starting in classDef.StartingItems)
            {
                Assert.True(
                    items.TryGet(starting.DefKey, out _),
                    $"{classDef.Key} referencia '{starting.DefKey}', que no existe en content/items/");
                Assert.True(starting.Quantity > 0, $"{classDef.Key}: cantidad no positiva para {starting.DefKey}");
            }
        }
    }

    [Fact]
    public void LasTresClases_TienenAlMenosUnItemInicial()
    {
        var classes = LoadClasses();

        foreach (var classDef in classes.All)
        {
            Assert.True(classDef.StartingItems.Length > 0, $"{classDef.Key} no tiene startingItems");
        }
    }
}

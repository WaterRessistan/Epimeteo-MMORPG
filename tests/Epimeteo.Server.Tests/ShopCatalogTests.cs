using Epimeteo.Server.Content;
using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Sin Postgres: valida el contenido real de <c>content/shops/</c>, incluido que cada
/// <c>defKey</c> exista de verdad en <c>content/items/</c> — mismo criterio que
/// <c>ItemCatalogTests.TodosLosStartingItems_ExistenEnElCatalogo</c> (Fase 6).
/// </summary>
public sealed class ShopCatalogTests
{
    private static ShopCatalog LoadShops() => new(ContentPaths.ResolveContentRoot());

    private static ItemCatalog LoadItems() => new(ContentPaths.ResolveContentRoot());

    [Fact]
    public void Constructor_CargaLasDosTiendas()
    {
        var catalog = LoadShops();

        Assert.True(catalog.TryGet("shop.general_store", out _));
        Assert.True(catalog.TryGet("shop.armory", out _));
        Assert.Equal(2, catalog.All.Count);
    }

    [Fact]
    public void SoloLaArmeria_Repara()
    {
        Assert.True(LoadShops().TryGet("shop.general_store", out var general));
        Assert.True(LoadShops().TryGet("shop.armory", out var armory));

        Assert.False(general!.CanRepair);
        Assert.True(armory!.CanRepair);
    }

    [Fact]
    public void LosDosNpcs_EstanEnElPuebloYEnHuecosDistintos()
    {
        Assert.True(LoadShops().TryGet("shop.general_store", out var general));
        Assert.True(LoadShops().TryGet("shop.armory", out var armory));

        Assert.Equal("map.village", general!.Npc.MapKey);
        Assert.Equal("map.village", armory!.Npc.MapKey);
        Assert.NotEqual((general.Npc.X, general.Npc.Y), (armory.Npc.X, armory.Npc.Y));
    }

    /// <summary>Si alguien renombra un ítem sin actualizar las tiendas, esto lo caza aquí, no en producción.</summary>
    [Fact]
    public void TodosLosDefKeyDeLasTiendas_ExistenEnElCatalogoDeItems()
    {
        var shops = LoadShops();
        var items = LoadItems();

        foreach (var shop in shops.All)
        {
            foreach (var entry in shop.Items)
            {
                Assert.True(items.TryGet(entry.DefKey, out _), $"{shop.Key} vende '{entry.DefKey}', que no existe en content/items/");
            }
        }
    }

    [Fact]
    public void LaArmeria_SoloVendeArmasYArmaduras()
    {
        Assert.True(LoadShops().TryGet("shop.armory", out var armory));
        var items = LoadItems();

        foreach (var entry in armory!.Items)
        {
            Assert.True(items.TryGet(entry.DefKey, out var def));
            Assert.True(def!.Type is ItemType.Weapon or ItemType.Armor, $"{entry.DefKey} no es arma ni armadura");
        }
    }
}

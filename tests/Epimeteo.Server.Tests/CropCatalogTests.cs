using Epimeteo.Server.Content;
using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Sin Postgres: valida el contenido real de <c>content/crops/</c> contra <c>content/items/</c>.</summary>
public sealed class CropCatalogTests
{
    private static CropCatalog LoadCrops() => new(ContentPaths.ResolveContentRoot());

    private static ItemCatalog LoadItems() => new(ContentPaths.ResolveContentRoot());

    [Fact]
    public void Constructor_CargaElTrigo()
    {
        var catalog = LoadCrops();

        Assert.True(catalog.TryGet("crop.wheat", out var wheat));
        Assert.Equal("item.wheat_seed", wheat!.SeedDefKey);
        Assert.Equal("item.wheat", wheat.YieldDefKey);
        Assert.True(wheat.GrowthDaysNeeded > 0);
        Assert.NotEmpty(wheat.Stages);
    }

    [Fact]
    public void TryGetBySeed_EncuentraElCultivoPorSuSemilla()
    {
        var catalog = LoadCrops();

        Assert.True(catalog.TryGetBySeed("item.wheat_seed", out var crop));
        Assert.Equal("crop.wheat", crop!.Key);
    }

    /// <summary>Cada semilla y cada rendimiento de cada cultivo tienen que existir de verdad en <c>content/items/</c>.</summary>
    [Fact]
    public void TodosLosCultivos_ReferencianItemsQueExisten()
    {
        var crops = LoadCrops();
        var items = LoadItems();

        foreach (var crop in crops.All)
        {
            Assert.True(items.TryGet(crop.SeedDefKey, out var seedDef), $"{crop.Key}: falta la semilla '{crop.SeedDefKey}'");
            Assert.Equal(ItemType.Seed, seedDef!.Type);

            Assert.True(items.TryGet(crop.YieldDefKey, out _), $"{crop.Key}: falta el rendimiento '{crop.YieldDefKey}'");
        }
    }

    [Fact]
    public void GeneralStore_VendeLasDosHerramientasDeGranja()
    {
        var shops = new ShopCatalog(ContentPaths.ResolveContentRoot());
        Assert.True(shops.TryGet("shop.general_store", out var shop));

        Assert.Contains(shop!.Items, item => item.DefKey == "item.hoe");
        Assert.Contains(shop.Items, item => item.DefKey == "item.watering_can");
    }
}

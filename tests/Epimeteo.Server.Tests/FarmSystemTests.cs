using Epimeteo.Server.Content;
using Epimeteo.Server.Farm;
using Epimeteo.Server.Inventory;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Sin Postgres ni tick: <see cref="FarmSystem"/> es puro sobre <see cref="FarmTileState"/> +
/// <see cref="PlayerInventory"/>, igual que <c>ShopSystemTests</c> lo es para tiendas. Usa el
/// catálogo real de <c>content/items/</c> y un cultivo sintético para probar <c>WrongSeason</c>
/// sin depender de en qué mes real corra (FASE-08 §2 D8: <c>crop.wheat</c> es <c>Any</c>).
/// </summary>
public sealed class FarmSystemTests
{
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());
    private static readonly CropCatalog Crops = new(ContentPaths.ResolveContentRoot());

    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static FarmTileState Tile(FarmTileStatus status = FarmTileStatus.Untilled) => new()
    {
        TileX = 6,
        TileY = 82,
        Status = status,
    };

    private static PlayerInventory InventoryWithTool(FarmToolAction? action)
    {
        if (action is null)
        {
            return new PlayerInventory([]);
        }

        var defKey = action == FarmToolAction.Till ? "item.hoe" : "item.watering_can";
        return new PlayerInventory([
            new ItemStack { DefKey = defKey, Container = ContainerId.Equipped, Slot = (byte)EquipSlot.Tool, Quantity = 1 },
        ]);
    }

    // ── Arar ─────────────────────────────────────────────────────────────

    [Fact]
    public void Till_SinHerramientaEquipada_WrongTool()
    {
        var tile = Tile();
        var result = FarmSystem.TryTill(tile, InventoryWithTool(null), Items);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.WrongTool, result.Code);
        Assert.Equal(FarmTileStatus.Untilled, tile.Status);
    }

    [Fact]
    public void Till_ConLaHerramientaEquivocada_WrongTool()
    {
        var tile = Tile();
        var result = FarmSystem.TryTill(tile, InventoryWithTool(FarmToolAction.Water), Items);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.WrongTool, result.Code);
    }

    [Fact]
    public void Till_ConAzada_DejaElTileArado()
    {
        var tile = Tile();
        var result = FarmSystem.TryTill(tile, InventoryWithTool(FarmToolAction.Till), Items);

        Assert.True(result.Ok);
        Assert.Equal(FarmTileStatus.Tilled, tile.Status);
    }

    [Fact]
    public void Till_UnTileYaArado_TileOccupied()
    {
        var tile = Tile(FarmTileStatus.Tilled);
        var result = FarmSystem.TryTill(tile, InventoryWithTool(FarmToolAction.Till), Items);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.TileOccupied, result.Code);
    }

    // ── Plantar ──────────────────────────────────────────────────────────

    [Fact]
    public void Plant_UnTileNoArado_TileNotTilled()
    {
        var tile = Tile();
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.wheat_seed", Container = ContainerId.General, Slot = 0, Quantity = 1 },
        ]);

        var result = FarmSystem.TryPlant(tile, inv, Items, Crops, ContainerId.General, 0, Now);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.TileNotTilled, result.Code);
    }

    [Fact]
    public void Plant_UnItemQueNoEsSemillaDeNingunCultivo_UnknownError()
    {
        var tile = Tile(FarmTileStatus.Tilled);
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.health_potion", Container = ContainerId.General, Slot = 0, Quantity = 1 },
        ]);

        var result = FarmSystem.TryPlant(tile, inv, Items, Crops, ContainerId.General, 0, Now);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.UnknownError, result.Code);
    }

    [Fact]
    public void Plant_ConLaSemillaCorrecta_ConsumeLaSemillaYDejaElTilePlantado()
    {
        var tile = Tile(FarmTileStatus.Tilled);
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.wheat_seed", Container = ContainerId.General, Slot = 0, Quantity = 1 },
        ]);

        var result = FarmSystem.TryPlant(tile, inv, Items, Crops, ContainerId.General, 0, Now);

        Assert.True(result.Ok);
        Assert.Equal(FarmTileStatus.Planted, tile.Status);
        Assert.Equal("crop.wheat", tile.CropKey);
        Assert.Equal(0, tile.GrowthDays);
        Assert.True(tile.GrowthNeeded > 0);
        Assert.Null(inv.Find(ContainerId.General, 0));
    }

    [Fact]
    public void Plant_FueraDeEstacion_WrongSeason()
    {
        var onlySpring = new CropDefinition
        {
            Key = "crop.test_spring_only",
            DisplayName = "Prueba",
            SeedDefKey = "item.wheat_seed",
            YieldDefKey = "item.wheat",
            YieldQuantity = 1,
            GrowthDaysNeeded = 1,
            Season = FarmSeason.Spring,
            Stages = ["x"],
        };
        var crops = new CropCatalog([onlySpring]);

        var tile = Tile(FarmTileStatus.Tilled);
        var inv = new PlayerInventory([
            new ItemStack { DefKey = "item.wheat_seed", Container = ContainerId.General, Slot = 0, Quantity = 1 },
        ]);

        // Now = 2026-06-15 → verano (FarmCalendar.SeasonOf), el cultivo sintético sólo crece en primavera.
        var result = FarmSystem.TryPlant(tile, inv, Items, crops, ContainerId.General, 0, Now);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.WrongSeason, result.Code);
        Assert.Equal(FarmTileStatus.Tilled, tile.Status);
        Assert.NotNull(inv.Find(ContainerId.General, 0));
    }

    // ── Regar ────────────────────────────────────────────────────────────

    [Fact]
    public void Water_UnTileSinPlantar_NotSeeded()
    {
        var tile = Tile(FarmTileStatus.Tilled);
        var result = FarmSystem.TryWater(tile, InventoryWithTool(FarmToolAction.Water), Items, Now);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotSeeded, result.Code);
    }

    [Fact]
    public void Water_ConLaHerramientaEquivocada_WrongTool()
    {
        var tile = Tile(FarmTileStatus.Planted);
        var result = FarmSystem.TryWater(tile, InventoryWithTool(FarmToolAction.Till), Items, Now);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.WrongTool, result.Code);
    }

    [Fact]
    public void Water_ConRegadera_MarcaElTileComoRegado()
    {
        var tile = Tile(FarmTileStatus.Planted);
        var result = FarmSystem.TryWater(tile, InventoryWithTool(FarmToolAction.Water), Items, Now);

        Assert.True(result.Ok);
        Assert.Equal(Now, tile.WateredAt);
    }

    // ── Cosechar ─────────────────────────────────────────────────────────

    [Fact]
    public void Harvest_UnTileNoListo_NotReadyToHarvest()
    {
        var tile = Tile(FarmTileStatus.Planted);
        var result = FarmSystem.TryHarvest(tile, new PlayerInventory([]), Items, Crops);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotReadyToHarvest, result.Code);
    }

    [Fact]
    public void Harvest_UnTileListo_DaElItemYDejaElTileArado()
    {
        var tile = Tile(FarmTileStatus.Ready);
        tile.CropKey = "crop.wheat";
        var inv = new PlayerInventory([]);

        var result = FarmSystem.TryHarvest(tile, inv, Items, Crops);

        Assert.True(result.Ok);
        Assert.Equal(FarmTileStatus.Tilled, tile.Status);
        Assert.Null(tile.CropKey);
        var harvested = inv.Find(ContainerId.General, 0);
        Assert.NotNull(harvested);
        Assert.Equal("item.wheat", harvested!.DefKey);
        Assert.Equal(3, harvested.Quantity);
    }

    [Fact]
    public void Harvest_SubeLaCalidadSegunLaRachaDeRiego()
    {
        var tile = Tile(FarmTileStatus.Ready);
        tile.CropKey = "crop.wheat";
        tile.WaterStreak = 2;
        var inv = new PlayerInventory([]);

        FarmSystem.TryHarvest(tile, inv, Items, Crops);

        Assert.Equal(2, inv.Find(ContainerId.General, 0)!.Quality);
    }

    [Fact]
    public void Harvest_LaRachaSeTopaEnTres()
    {
        var tile = Tile(FarmTileStatus.Ready);
        tile.CropKey = "crop.wheat";
        tile.WaterStreak = 10;
        var inv = new PlayerInventory([]);

        FarmSystem.TryHarvest(tile, inv, Items, Crops);

        Assert.Equal(3, inv.Find(ContainerId.General, 0)!.Quality);
    }

    // ── Crecimiento diario ───────────────────────────────────────────────

    [Fact]
    public void ApplyDailyGrowth_UnTileNoPlantado_NoCambiaNada()
    {
        var tile = Tile(FarmTileStatus.Tilled);
        var changed = FarmSystem.ApplyDailyGrowth(tile, Now);

        Assert.False(changed);
    }

    [Fact]
    public void ApplyDailyGrowth_Regado_SubeUnDiaEnteroYLaRacha()
    {
        var tile = Tile(FarmTileStatus.Planted);
        tile.GrowthNeeded = 3;
        tile.WateredAt = Now;

        var changed = FarmSystem.ApplyDailyGrowth(tile, Now.AddDays(1));

        Assert.True(changed);
        Assert.Equal(1.0f, tile.GrowthDays);
        Assert.Equal(1, tile.WaterStreak);
        Assert.Null(tile.WateredAt);
        Assert.Equal(FarmTileStatus.Planted, tile.Status);
    }

    [Fact]
    public void ApplyDailyGrowth_SinRegar_SubeMedioDiaYRompeLaRacha()
    {
        var tile = Tile(FarmTileStatus.Planted);
        tile.GrowthNeeded = 3;
        tile.WaterStreak = 2;
        tile.WateredAt = null;

        FarmSystem.ApplyDailyGrowth(tile, Now.AddDays(1));

        Assert.Equal(0.5f, tile.GrowthDays);
        Assert.Equal(0, tile.WaterStreak);
    }

    [Fact]
    public void ApplyDailyGrowth_AlAlcanzarElProgresoNecesario_PasaAListo()
    {
        var tile = Tile(FarmTileStatus.Planted);
        tile.GrowthNeeded = 1;
        tile.WateredAt = Now;

        FarmSystem.ApplyDailyGrowth(tile, Now.AddDays(1));

        Assert.Equal(FarmTileStatus.Ready, tile.Status);
        Assert.Null(tile.EtaAt);
    }
}

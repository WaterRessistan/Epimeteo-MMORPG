using Epimeteo.Server.Content;
using Epimeteo.Server.Inventory;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Farm;

/// <summary>Resultado de una acción de granja. Nunca lanza (CLAUDE.md §4), mismo molde que <c>InventoryOpResult</c>/<c>ShopOpResult</c>.</summary>
public readonly record struct FarmActionResult(bool Ok, ResultCode Code, IReadOnlyList<SlotRef> InventoryTouched)
{
    public static FarmActionResult Fail(ResultCode code) => new(false, code, []);

    public static FarmActionResult Success(params SlotRef[] touched) => new(true, ResultCode.Ok, touched);
}

/// <summary>
/// Arar, plantar, regar, cosechar y el crecimiento diario — puro dado un <see cref="FarmTileState"/>,
/// un <see cref="PlayerInventory"/> y los catálogos, sin I/O, para que el tick lo ejecute sin
/// tocar Postgres y se pueda probar sin servidor ni BD. Mismo espíritu que <c>ShopSystem</c>
/// (Fase 7); <see cref="ApplyDailyGrowth"/> es la función que en <c>docs/00 §7</c> iba a ser un
/// <c>UPDATE</c> SQL directo (FASE-08 §2 D1).
/// </summary>
public static class FarmSystem
{
    private const short QualityStreakCap = 3;

    public static FarmActionResult TryTill(FarmTileState tile, PlayerInventory inventory, ItemCatalog items)
    {
        if (tile.Status != FarmTileStatus.Untilled)
        {
            return FarmActionResult.Fail(ResultCode.TileOccupied);
        }

        if (!HasToolEquipped(inventory, items, FarmToolAction.Till))
        {
            return FarmActionResult.Fail(ResultCode.WrongTool);
        }

        tile.Status = FarmTileStatus.Tilled;
        return FarmActionResult.Success();
    }

    public static FarmActionResult TryPlant(
        FarmTileState tile, PlayerInventory inventory, ItemCatalog items, CropCatalog crops,
        ContainerId container, byte slot, DateTimeOffset now)
    {
        if (tile.Status != FarmTileStatus.Tilled)
        {
            return FarmActionResult.Fail(ResultCode.TileNotTilled);
        }

        var stack = inventory.Find(container, slot);
        if (stack is null)
        {
            return FarmActionResult.Fail(ResultCode.ItemNotFound);
        }

        if (!crops.TryGetBySeed(stack.DefKey, out var crop))
        {
            return FarmActionResult.Fail(ResultCode.UnknownError);
        }

        if (crop.Season != FarmSeason.Any && crop.Season != FarmCalendar.SeasonOf(now))
        {
            return FarmActionResult.Fail(ResultCode.WrongSeason);
        }

        var consume = InventorySystem.TryDrop(inventory, container, slot, 1);
        if (!consume.Ok)
        {
            return FarmActionResult.Fail(consume.Code);
        }

        tile.Status = FarmTileStatus.Planted;
        tile.CropKey = crop.Key;
        tile.PlantedAt = now;
        tile.WateredAt = null;
        tile.GrowthDays = 0;
        tile.GrowthNeeded = crop.GrowthDaysNeeded;
        tile.WaterStreak = 0;
        tile.EtaAt = FarmCalendar.EstimateEta(now, tile.GrowthDays, tile.GrowthNeeded);

        return new FarmActionResult(true, ResultCode.Ok, consume.Touched);
    }

    public static FarmActionResult TryWater(FarmTileState tile, PlayerInventory inventory, ItemCatalog items, DateTimeOffset now)
    {
        if (tile.Status != FarmTileStatus.Planted)
        {
            return FarmActionResult.Fail(ResultCode.NotSeeded);
        }

        if (!HasToolEquipped(inventory, items, FarmToolAction.Water))
        {
            return FarmActionResult.Fail(ResultCode.WrongTool);
        }

        tile.WateredAt = now;
        return FarmActionResult.Success();
    }

    public static FarmActionResult TryHarvest(FarmTileState tile, PlayerInventory inventory, ItemCatalog items, CropCatalog crops)
    {
        if (tile.Status != FarmTileStatus.Ready)
        {
            return FarmActionResult.Fail(ResultCode.NotReadyToHarvest);
        }

        if (tile.CropKey is null || !crops.TryGet(tile.CropKey, out var crop))
        {
            return FarmActionResult.Fail(ResultCode.UnknownError);
        }

        var quality = (byte)Math.Min(tile.WaterStreak, QualityStreakCap);
        var add = InventorySystem.TryAddNew(inventory, items, crop.YieldDefKey, crop.YieldQuantity, quality: quality);
        if (!add.Ok)
        {
            return FarmActionResult.Fail(add.Code);
        }

        // D10: vuelve a arado, no a virgen — la tierra no se desara sola. Sin multicosecha esta
        // fase: cosechar siempre limpia el cultivo entero.
        tile.Status = FarmTileStatus.Tilled;
        tile.CropKey = null;
        tile.PlantedAt = null;
        tile.WateredAt = null;
        tile.GrowthDays = 0;
        tile.GrowthNeeded = 0;
        tile.WaterStreak = 0;
        tile.EtaAt = null;

        return new FarmActionResult(true, ResultCode.Ok, add.Touched);
    }

    /// <summary>
    /// Cierra un día de granja para un tile: la función que sustituye al <c>UPDATE</c> masivo de
    /// <c>docs/00 §7</c> (FASE-08 §2 D1). Devuelve si el tile cambió (para saber si hay que
    /// persistirlo/emitirlo). <paramref name="dayBoundaryEnd"/> es la frontera de las 05:00 UTC
    /// que cierra el día procesado — se usa para recalcular la ETA, no para decidir "regado hoy"
    /// (eso ya lo decide <see cref="FarmTileState.WateredAt"/> no nulo, limpio cada día cerrado).
    /// </summary>
    public static bool ApplyDailyGrowth(FarmTileState tile, DateTimeOffset dayBoundaryEnd)
    {
        if (tile.Status != FarmTileStatus.Planted)
        {
            return false;
        }

        var watered = tile.WateredAt is not null;
        tile.GrowthDays += watered ? 1.0f : 0.5f;
        tile.WaterStreak = watered ? (short)(tile.WaterStreak + 1) : (short)0;
        tile.WateredAt = null;

        if (tile.GrowthDays >= tile.GrowthNeeded)
        {
            tile.Status = FarmTileStatus.Ready;
            tile.EtaAt = null;
        }
        else
        {
            tile.EtaAt = FarmCalendar.EstimateEta(dayBoundaryEnd, tile.GrowthDays, tile.GrowthNeeded);
        }

        return true;
    }

    private static bool HasToolEquipped(PlayerInventory inventory, ItemCatalog items, FarmToolAction action)
    {
        var tool = inventory.Find(ContainerId.Equipped, (byte)EquipSlot.Tool);
        return tool is not null && items.TryGet(tool.DefKey, out var def) && def.FarmToolAction == action;
    }
}

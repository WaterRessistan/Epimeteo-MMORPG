using Epimeteo.Server.Content;
using Epimeteo.Server.Inventory;
using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.Combat;

/// <summary>Cuántos niveles (y puntos de stat) concedió una tirada de XP.</summary>
public readonly record struct LevelGrantResult(int LevelsGained, int StatPointsGained)
{
    public bool LeveledUp => LevelsGained > 0;
}

/// <summary>
/// Subir de nivel y gastar puntos de stat — puro dado el <see cref="PlayerEntity"/> y los
/// catálogos, sin I/O, mismo espíritu que <c>CombatSystem</c>/<c>FarmSystem</c> (FASE-10 §2 D2).
/// </summary>
public static class LevelingSystem
{
    /// <summary>
    /// Aplica una concesión de XP. En un bucle: por si una única concesión cruzara más de un
    /// nivel (hoy no pasa con los premios de la Fase 9, pero el código no depende de que no pase).
    /// Cada nivel de más concede <see cref="ProgressionConstants.StatPointsPerLevel"/> puntos de
    /// stat y cura del todo (D2): subir de nivel nunca deja a nadie peor de lo que estaba.
    /// </summary>
    public static LevelGrantResult GrantXp(PlayerEntity player, long amount, ClassCatalog classes, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(items);

        if (amount <= 0)
        {
            return default;
        }

        player.Xp += amount;
        var levelsGained = 0;

        while (player.Xp >= LevelingFormulas.XpRequiredForNextLevel(player.Level))
        {
            player.Xp -= LevelingFormulas.XpRequiredForNextLevel(player.Level);
            player.Level++;
            levelsGained++;
        }

        if (levelsGained == 0)
        {
            return default;
        }

        var statPointsGained = levelsGained * ProgressionConstants.StatPointsPerLevel;
        player.StatPoints += statPointsGained;

        if (classes.TryGet(player.DefKey, out var classDef))
        {
            var stats = InventorySystem.ComputeDerivedStats(
                player.Inventory, items, classDef, player.Str, player.IntStat, player.Vit, player.Dex, player.Level);

            player.HpMax = stats.HpMax;
            player.MpMax = stats.MpMax;
            player.AttackPower = stats.Attack;
            player.Defense = stats.Defense;
            player.DexEffective = stats.DexEffective;
        }

        player.Hp = player.HpMax;
        player.Mp = player.MpMax;

        return new LevelGrantResult(levelsGained, statPointsGained);
    }

    /// <summary>Gasta un punto de stat sin gastar (FASE-10 §2 D4): un punto por llamada, nunca un valor final.</summary>
    public static ResultCode TryAllocateStatPoint(PlayerEntity player, StatKind stat)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.StatPoints <= 0)
        {
            return ResultCode.NoStatPointsAvailable;
        }

        player.StatPoints--;

        switch (stat)
        {
            case StatKind.Str:
                player.Str++;
                break;

            case StatKind.Int:
                player.IntStat++;
                break;

            case StatKind.Vit:
                player.Vit++;
                break;

            case StatKind.Dex:
                player.Dex++;
                break;
        }

        return ResultCode.Ok;
    }
}

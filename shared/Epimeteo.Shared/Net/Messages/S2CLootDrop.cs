using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Un saco de loot en el suelo con su contenido visible (opcode 0x8062). Que se vea no significa
/// que se pueda coger: durante <c>CombatConstants.LootRightsSeconds</c> sólo lo abre quien más
/// daño hizo, y eso lo decide el servidor en cada <see cref="C2SLootTake"/> (FASE-09 §2 D9).
/// </summary>
[MessagePackObject]
public sealed record S2CLootDrop
{
    [Key(0)]
    public required int EntityId { get; init; }

    [Key(1)]
    public required float X { get; init; }

    [Key(2)]
    public required float Y { get; init; }

    [Key(3)]
    public required LootItemInfo[] Items { get; init; }
}

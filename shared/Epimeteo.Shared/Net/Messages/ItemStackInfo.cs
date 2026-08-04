using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Un stack de ítems tal como lo ve el cliente, con dónde está. Se reutiliza en
/// <c>InventoryFull</c>, <c>InventoryDelta</c> (dentro de <see cref="InventoryChangeEntry"/>) y
/// <c>EquipmentUpdate</c> (ahí <see cref="Slot"/> es un <see cref="EquipSlot"/> en vez de una
/// posición de bolsa). Sin <c>affixes</c> por ahora: nadie los genera todavía (FASE-06 §6) — el
/// mismo criterio que "no calcules lo que nada consume".
/// </summary>
[MessagePackObject]
public sealed record ItemStackInfo
{
    [Key(0)]
    public required string DefKey { get; init; }

    [Key(1)]
    public required ContainerId Container { get; init; }

    [Key(2)]
    public required byte Slot { get; init; }

    [Key(3)]
    public required int Quantity { get; init; }

    [Key(4)]
    public int? Durability { get; init; }

    [Key(5)]
    public int? DurabilityMax { get; init; }

    [Key(6)]
    public required byte Quality { get; init; }

    [Key(7)]
    public long? BoundTo { get; init; }
}

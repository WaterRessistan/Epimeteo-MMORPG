using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Equipar el ítem de <c>(container, slot)</c> en <see cref="EquipSlot"/>. El servidor valida que
/// el hueco pedido esté entre los que resuelve la categoría del ítem (FASE-06 §2 D4) — el cliente
/// elige el hueco concreto (por ejemplo, cuál de los dos anillos), no una categoría abstracta.
/// </summary>
[MessagePackObject]
public sealed record C2SEquip
{
    [Key(0)]
    public required ContainerId Container { get; init; }

    [Key(1)]
    public required byte Slot { get; init; }

    [Key(2)]
    public required EquipSlot EquipSlot { get; init; }
}

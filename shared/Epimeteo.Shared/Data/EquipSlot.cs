namespace Epimeteo.Shared.Data;

/// <summary>
/// Hueco físico de equipo. Es el valor de <c>item_instances.slot</c> cuando
/// <c>container = ContainerId.Equipped</c> (<c>docs/02 § Ítems</c>). Un ítem no declara uno de
/// estos directamente — declara una <see cref="EquipCategory"/>, que <see cref="EquipSlots"/>
/// resuelve a uno o más de estos valores (FASE-06 §2 D4).
/// </summary>
public enum EquipSlot : byte
{
    MainHand = 0,
    OffHand = 1,
    Head = 2,
    Chest = 3,
    Hands = 4,
    Legs = 5,
    Feet = 6,
    Cloak = 7,
    Ring1 = 8,
    Ring2 = 9,
    Amulet = 10,
    Tool = 11,
}

namespace Epimeteo.Shared.Data;

/// <summary>
/// Categoría de equipo que declara un <see cref="ItemDefinition"/> de tipo <c>Weapon</c> o
/// <c>Armor</c>. <see cref="EquipSlots.Resolve"/> la traduce a los <see cref="EquipSlot"/>
/// físicos donde encaja (FASE-06 §2 D4).
/// </summary>
public enum EquipCategory : byte
{
    MainHand = 0,
    OffHand = 1,
    Head = 2,
    Chest = 3,
    Hands = 4,
    Legs = 5,
    Feet = 6,
    Cloak = 7,

    /// <summary>El único caso de "uno de varios": resuelve a <c>Ring1</c> <b>o</b> <c>Ring2</c>.</summary>
    Ring = 8,

    Amulet = 9,
    Tool = 10,
}

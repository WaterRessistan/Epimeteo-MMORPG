namespace Epimeteo.Shared.Data;

/// <summary>
/// Resuelve una <see cref="EquipCategory"/> a los <see cref="EquipSlot"/> físicos donde un ítem de
/// esa categoría puede ir. Pura, sin estado: la usan servidor y cliente por igual (el servidor
/// para validar <c>Equip</c>, el cliente para saber en qué huecos de la UI se puede soltar un
/// ítem al arrastrarlo).
/// </summary>
public static class EquipSlots
{
    private static readonly EquipSlot[] RingSlots = [EquipSlot.Ring1, EquipSlot.Ring2];

    /// <summary>Huecos donde encaja un ítem de esta categoría. Siempre al menos uno.</summary>
    public static IReadOnlyList<EquipSlot> Resolve(EquipCategory category) => category switch
    {
        EquipCategory.MainHand => [EquipSlot.MainHand],
        EquipCategory.OffHand => [EquipSlot.OffHand],
        EquipCategory.Head => [EquipSlot.Head],
        EquipCategory.Chest => [EquipSlot.Chest],
        EquipCategory.Hands => [EquipSlot.Hands],
        EquipCategory.Legs => [EquipSlot.Legs],
        EquipCategory.Feet => [EquipSlot.Feet],
        EquipCategory.Cloak => [EquipSlot.Cloak],
        EquipCategory.Ring => RingSlots,
        EquipCategory.Amulet => [EquipSlot.Amulet],
        EquipCategory.Tool => [EquipSlot.Tool],
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Categoría de equipo desconocida."),
    };

    /// <summary>Verdadero si <paramref name="slot"/> es uno de los huecos válidos para <paramref name="category"/>.</summary>
    public static bool IsValid(EquipCategory category, EquipSlot slot) => Resolve(category).Contains(slot);
}

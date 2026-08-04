namespace Epimeteo.Shared.Data;

/// <summary>
/// Números de inventario que tienen que valer lo mismo en cliente y servidor — mismo motivo que
/// <c>Simulation/SimulationConstants</c>: el cliente necesita el tamaño de cada bolsa para dibujar
/// la rejilla y saber dónde puede soltar un ítem al arrastrar, y el servidor para validar. Si se
/// desincronizaran, un slot "legal" para el cliente sería basura para el servidor y viceversa.
/// </summary>
public static class InventoryConstants
{
    public const int GeneralCapacity = 30;
    public const int WeaponBagCapacity = 20;
    public const int ArmorBagCapacity = 20;

    /// <summary>
    /// Tamaño de un contenedor de bolsa. Sólo tiene sentido para <see cref="ContainerId.General"/>,
    /// <see cref="ContainerId.WeaponBag"/> y <see cref="ContainerId.ArmorBag"/> — el resto no son
    /// rejillas lineales (el equipo tiene 12 huecos fijos de <see cref="EquipSlot"/>) o no están
    /// implementados todavía (FASE-06 §1).
    /// </summary>
    public static int CapacityOf(ContainerId container) => container switch
    {
        ContainerId.General => GeneralCapacity,
        ContainerId.WeaponBag => WeaponBagCapacity,
        ContainerId.ArmorBag => ArmorBagCapacity,
        _ => throw new ArgumentOutOfRangeException(nameof(container), container, "Este contenedor no tiene una capacidad de bolsa."),
    };

    /// <summary>
    /// Contenedor no-equipado que le corresponde a un tipo de ítem (FASE-06 §2 D3): la regla de
    /// negocio de "una arma sólo entra en la bolsa de armas" en una sola función, no una lista de
    /// excepciones repartida por el código.
    /// </summary>
    public static ContainerId AllowedContainer(ItemType type) => type switch
    {
        ItemType.Weapon => ContainerId.WeaponBag,
        ItemType.Armor => ContainerId.ArmorBag,
        ItemType.Consumable or ItemType.Material or ItemType.Seed => ContainerId.General,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de ítem desconocido."),
    };

    /// <summary>
    /// Verdadero si <paramref name="slot"/> es un hueco que existe de verdad en
    /// <paramref name="container"/>. Un cliente honesto nunca manda un slot fuera de esto —igual
    /// que nunca manda una dirección de movimiento fuera de [-1,1]— así que fallar esta
    /// comprobación es <c>Kick(ProtocolError)</c>, no un <c>ResultCode</c> blando.
    /// </summary>
    public static bool IsWellFormedSlot(ContainerId container, byte slot) => container switch
    {
        ContainerId.General or ContainerId.WeaponBag or ContainerId.ArmorBag => slot < CapacityOf(container),
        ContainerId.Equipped => slot <= (byte)EquipSlot.Tool,
        _ => false,
    };
}

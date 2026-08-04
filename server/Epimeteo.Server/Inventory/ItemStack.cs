using Epimeteo.Shared.Data;

namespace Epimeteo.Server.Inventory;

/// <summary>
/// Un stack de ítems en memoria, autoritativo mientras dura la sesión (FASE-06 §2 D1). Sin id de
/// Postgres: la persistencia es una instantánea completa (D2), no hay ninguna operación que
/// necesite referenciar la fila por id desde el tick.
/// </summary>
public sealed class ItemStack
{
    public required string DefKey { get; init; }

    public required ContainerId Container { get; set; }

    /// <summary>Posición en la bolsa, o el <see cref="EquipSlot"/> si <see cref="Container"/> es <c>Equipped</c>.</summary>
    public required byte Slot { get; set; }

    public required int Quantity { get; set; }

    public int? Durability { get; set; }

    public int? DurabilityMax { get; set; }

    public byte Quality { get; set; }

    public long? BoundTo { get; set; }
}

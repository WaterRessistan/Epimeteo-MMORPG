namespace Epimeteo.Server.Persistence.Items;

/// <summary>
/// Copia inmutable de un <c>Server.Inventory.ItemStack</c> en el instante de encolar el guardado.
/// Nunca una referencia al objeto vivo: éste sigue mutando en el tick después de encolarse, y si
/// <see cref="InventorySaver"/> lo leyera más tarde vería un estado que ya no es el que se pidió
/// guardar (mismo motivo por el que <c>PositionSave</c> copia <c>x</c>/<c>y</c> en vez de guardar
/// el <c>PlayerEntity</c>).
/// </summary>
public readonly record struct ItemStackSnapshot(
    string DefKey,
    byte Container,
    byte Slot,
    int Quantity,
    int? Durability,
    int? DurabilityMax,
    byte Quality,
    long? BoundTo);

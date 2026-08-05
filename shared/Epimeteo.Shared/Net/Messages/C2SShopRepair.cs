using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Reparar el ítem de <c>(container, slot)</c> en la tienda abierta. Sólo tiendas con
/// <c>canRepair</c> (FASE-07 §2 D6). El éxito se ve en un <c>InventoryDelta</c> (la durabilidad
/// cambió) y un <c>CurrencyUpdate</c> (el oro bajó) — sin mensaje de respuesta propio.
/// </summary>
[MessagePackObject]
public sealed record C2SShopRepair
{
    [Key(0)]
    public required ContainerId Container { get; init; }

    [Key(1)]
    public required byte Slot { get; init; }
}

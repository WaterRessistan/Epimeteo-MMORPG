using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Mover (o apilar, o dividir) un ítem entre dos huecos del propio inventario. El cliente nunca
/// dice qué ítem es ni cuánto queda: sólo el origen, el destino y cuánto quiere mover
/// (CLAUDE.md §4, sólo intenciones).
/// </summary>
[MessagePackObject]
public sealed record C2SInvMove
{
    [Key(0)]
    public required ContainerId FromContainer { get; init; }

    [Key(1)]
    public required byte FromSlot { get; init; }

    [Key(2)]
    public required ContainerId ToContainer { get; init; }

    [Key(3)]
    public required byte ToSlot { get; init; }

    [Key(4)]
    public required int Quantity { get; init; }
}

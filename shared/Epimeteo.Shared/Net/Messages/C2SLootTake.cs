using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Coger un hueco concreto de un saco de loot. Opcode nuevo de la Fase 9 (§2 D9): el saco es una
/// entidad del mundo, no un contenedor del personaje, así que <c>InvMove</c> no servía.
/// </summary>
[MessagePackObject]
public sealed record C2SLootTake
{
    /// <summary>Id de entidad del saco, el que llegó en <see cref="S2CLootDrop"/>.</summary>
    [Key(0)]
    public required int LootEntityId { get; init; }

    /// <summary>Índice dentro de <see cref="S2CLootDrop.Items"/>.</summary>
    [Key(1)]
    public required byte Slot { get; init; }
}

using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Tirar (destruir) un ítem. Sin saco de loot todavía (Fase 9): lo que se tira desaparece del
/// mundo, no se queda en el suelo (FASE-06 §1).
/// </summary>
[MessagePackObject]
public sealed record C2SInvDrop
{
    [Key(0)]
    public required ContainerId Container { get; init; }

    [Key(1)]
    public required byte Slot { get; init; }

    [Key(2)]
    public required int Quantity { get; init; }
}

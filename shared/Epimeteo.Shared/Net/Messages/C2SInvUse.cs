using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Usar un ítem (por ahora sólo consumibles de curación, FASE-06 §2 D5).</summary>
[MessagePackObject]
public sealed record C2SInvUse
{
    [Key(0)]
    public required ContainerId Container { get; init; }

    [Key(1)]
    public required byte Slot { get; init; }
}

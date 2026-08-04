using Epimeteo.Shared.Simulation;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Una entidad que deja de ser visible, con el motivo.</summary>
[MessagePackObject]
public sealed record EntityDespawnEntry
{
    [Key(0)]
    public required int Id { get; init; }

    [Key(1)]
    public required DespawnReason Reason { get; init; }
}

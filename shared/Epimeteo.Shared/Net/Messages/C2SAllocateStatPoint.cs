using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Gastar un punto de stat sin gastar. Un punto por mensaje, nunca un valor final (FASE-10 §2 D4):
/// repartir varios es mandar esto varias veces.
/// </summary>
[MessagePackObject]
public sealed record C2SAllocateStatPoint
{
    [Key(0)]
    public required StatKind Stat { get; init; }
}

using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Arar un tile de una parcela. El servidor valida que es un tile de verdad (FASE-08 §2 D6).</summary>
[MessagePackObject]
public sealed record C2SFarmTill
{
    [Key(0)]
    public required int TileX { get; init; }

    [Key(1)]
    public required int TileY { get; init; }
}

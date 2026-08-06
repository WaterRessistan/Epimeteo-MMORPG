using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Regar un tile plantado. Acelera el progreso del día (+1,0 en vez de +0,5).</summary>
[MessagePackObject]
public sealed record C2SFarmWater
{
    [Key(0)]
    public required int TileX { get; init; }

    [Key(1)]
    public required int TileY { get; init; }
}

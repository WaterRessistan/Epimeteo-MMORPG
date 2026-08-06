using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Cosechar un tile listo. Deja el tile arado, no virgen (FASE-08 §2 D10).</summary>
[MessagePackObject]
public sealed record C2SFarmHarvest
{
    [Key(0)]
    public required int TileX { get; init; }

    [Key(1)]
    public required int TileY { get; init; }
}

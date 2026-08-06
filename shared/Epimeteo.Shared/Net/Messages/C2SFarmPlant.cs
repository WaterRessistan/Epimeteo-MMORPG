using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Plantar la semilla de <c>(container, slot)</c> en un tile ya arado.</summary>
[MessagePackObject]
public sealed record C2SFarmPlant
{
    [Key(0)]
    public required int TileX { get; init; }

    [Key(1)]
    public required int TileY { get; init; }

    [Key(2)]
    public required ContainerId Container { get; init; }

    [Key(3)]
    public required byte Slot { get; init; }
}

using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Una entrada visible de un saco de loot.</summary>
[MessagePackObject]
public sealed record LootItemInfo
{
    [Key(0)]
    public required string DefKey { get; init; }

    [Key(1)]
    public required int Quantity { get; init; }
}

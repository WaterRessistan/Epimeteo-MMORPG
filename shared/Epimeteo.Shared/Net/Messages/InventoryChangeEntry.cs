using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Un hueco que cambió. <see cref="Item"/> a <c>null</c> significa que el hueco quedó vacío.</summary>
[MessagePackObject]
public sealed record InventoryChangeEntry
{
    [Key(0)]
    public required ContainerId Container { get; init; }

    [Key(1)]
    public required byte Slot { get; init; }

    [Key(2)]
    public ItemStackInfo? Item { get; init; }
}

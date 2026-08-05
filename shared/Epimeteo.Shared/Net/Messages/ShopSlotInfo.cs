using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Un hueco del catálogo de una tienda, con el stock <b>actual</b> (no el máximo).</summary>
[MessagePackObject]
public sealed record ShopSlotInfo
{
    [Key(0)]
    public required string DefKey { get; init; }

    [Key(1)]
    public required long PriceBuy { get; init; }

    [Key(2)]
    public required long PriceSell { get; init; }

    /// <summary><c>-1</c> = stock infinito.</summary>
    [Key(3)]
    public required int Stock { get; init; }
}

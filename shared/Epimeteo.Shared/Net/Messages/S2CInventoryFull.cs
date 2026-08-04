using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Contenedores 0/1/2 completos. Se manda una vez, al entrar al mundo (<c>docs/01</c>).</summary>
[MessagePackObject]
public sealed record S2CInventoryFull
{
    [Key(0)]
    public required ItemStackInfo[] Items { get; init; }
}

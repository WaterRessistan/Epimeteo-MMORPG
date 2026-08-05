using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Abrir la tienda de un NPC. El servidor valida la distancia (FASE-07 §2 D7).</summary>
[MessagePackObject]
public sealed record C2SShopOpen
{
    [Key(0)]
    public required int NpcEntityId { get; init; }
}

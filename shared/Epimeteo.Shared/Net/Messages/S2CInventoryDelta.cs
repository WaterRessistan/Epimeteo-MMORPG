using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Huecos que cambiaron tras una mutación con éxito (mover, apilar, dividir, tirar, usar).</summary>
[MessagePackObject]
public sealed record S2CInventoryDelta
{
    [Key(0)]
    public required InventoryChangeEntry[] Changes { get; init; }
}

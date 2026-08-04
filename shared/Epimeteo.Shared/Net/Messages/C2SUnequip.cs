using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Desequipar lo que haya en <see cref="EquipSlot"/>, de vuelta a la bolsa que le toque por su tipo.</summary>
[MessagePackObject]
public sealed record C2SUnequip
{
    [Key(0)]
    public required EquipSlot EquipSlot { get; init; }
}

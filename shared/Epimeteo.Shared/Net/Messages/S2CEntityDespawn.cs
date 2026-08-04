using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Entidades que salen del área de interés, mueren o se desconectan (opcode 0x8021).</summary>
[MessagePackObject]
public sealed record S2CEntityDespawn
{
    [Key(0)]
    public required EntityDespawnEntry[] Entities { get; init; }
}

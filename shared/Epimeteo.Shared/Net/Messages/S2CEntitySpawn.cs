using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Entidades que entran en el área de interés del jugador (opcode 0x8020). Se manda al entrar al
/// mundo con todo lo visible y luego sólo con lo que va apareciendo al moverse.
/// </summary>
[MessagePackObject]
public sealed record S2CEntitySpawn
{
    [Key(0)]
    public required EntitySpawnInfo[] Entities { get; init; }
}

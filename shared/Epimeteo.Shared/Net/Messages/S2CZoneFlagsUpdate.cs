using Epimeteo.Shared.Simulation;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Región en la que está el jugador (opcode 0x8024). Se manda al entrar al mundo y cada vez que
/// se cruza de región.
/// <para>
/// El cliente lo usa <b>sólo</b> para pintar el aviso de zona hostil. Quién puede atacar a quién
/// lo decide el servidor con las posiciones autoritativas de atacante y víctima
/// (<c>docs/00 §6</c>); mentir aquí no da ninguna ventaja porque el cliente no decide nada.
/// </para>
/// </summary>
[MessagePackObject]
public sealed record S2CZoneFlagsUpdate
{
    [Key(0)]
    public required string RegionName { get; init; }

    [Key(1)]
    public required ZoneFlags Flags { get; init; }
}

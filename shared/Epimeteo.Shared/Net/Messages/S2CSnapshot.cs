using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// La verdad del servidor sobre lo que el jugador ve (opcode 0x8022), a 10 Hz.
/// <para>
/// <see cref="LastAckedInputSeq"/> es la pieza que hace posible la reconciliación: dice hasta qué
/// input está ya incluido en estas posiciones, así que el cliente puede tirar de su buffer todo
/// lo anterior y reejecutar sólo lo que el servidor todavía no ha visto.
/// </para>
/// </summary>
[MessagePackObject]
public sealed record S2CSnapshot
{
    /// <summary>Tick de simulación al que corresponde este estado. Es el reloj de la interpolación.</summary>
    [Key(0)]
    public required long ServerTick { get; init; }

    /// <summary>Último <c>InputState</c> del jugador ya <b>consumido</b> por la simulación.</summary>
    [Key(1)]
    public required uint LastAckedInputSeq { get; init; }

    /// <summary>Entidades cambiadas desde el snapshot anterior, más siempre la del propio jugador.</summary>
    [Key(2)]
    public required EntityDelta[] Entities { get; init; }
}

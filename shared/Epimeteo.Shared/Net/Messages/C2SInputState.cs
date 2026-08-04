using Epimeteo.Shared.Simulation;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// La intención de movimiento de un tick (opcode 0x0020, estado <see cref="SessionState.InWorld"/>).
/// Es lo único que el cliente manda sobre su posición: nunca coordenadas (CLAUDE.md §4).
/// <para>
/// Un <c>InputState</c> equivale a <b>un</b> paso de <see cref="SimulationConstants.TickDtMs"/> ms
/// en los dos lados. <see cref="DtMs"/> viaja para diagnóstico de jitter pero el servidor
/// <b>no lo integra</b>: si el reloj del cliente entrara en la simulación, mentirlo daría
/// velocidad gratis (FASE-04 §2 D1).
/// </para>
/// </summary>
[MessagePackObject]
public sealed record C2SInputState
{
    /// <summary>Número de secuencia, estrictamente creciente. El servidor lo devuelve en el snapshot.</summary>
    [Key(0)]
    public required uint Seq { get; init; }

    /// <summary>-1, 0 o 1. Cualquier otro valor cierra la sesión.</summary>
    [Key(1)]
    public required sbyte DirX { get; init; }

    /// <summary>-1, 0 o 1. Cualquier otro valor cierra la sesión.</summary>
    [Key(2)]
    public required sbyte DirY { get; init; }

    /// <summary>Orientación deseada; sólo cuenta cuando no hay dirección de movimiento.</summary>
    [Key(3)]
    public required Facing Facing { get; init; }

    /// <summary>Bits de acción reservados (correr, etc.). Hoy siempre 0.</summary>
    [Key(4)]
    public required byte Flags { get; init; }

    /// <summary>Milisegundos reales que el cliente acumuló para este paso. Sólo diagnóstico.</summary>
    [Key(5)]
    public required ushort DtMs { get; init; }
}

namespace Epimeteo.Shared.Simulation;

/// <summary>
/// La intención de movimiento de un jugador durante <b>un</b> tick de 50 ms. El cliente nunca
/// manda posiciones; manda esto (CLAUDE.md §4, reglas de seguridad).
/// <para>
/// No lleva <c>dt</c>: un input es siempre un paso de <see cref="SimulationConstants.TickDtMs"/>
/// milisegundos, en los dos lados. El reloj del cliente no entra en la simulación (FASE-04 §2 D1).
/// </para>
/// </summary>
/// <param name="Seq">Número de secuencia, estrictamente creciente por sesión.</param>
/// <param name="DirX">-1, 0 o 1.</param>
/// <param name="DirY">-1, 0 o 1.</param>
/// <param name="Facing">Orientación deseada; sólo se respeta si no hay dirección de movimiento.</param>
public readonly record struct MoveInput(uint Seq, sbyte DirX, sbyte DirY, Facing Facing)
{
    /// <summary>Input sin movimiento, el que se simula cuando la cola del servidor está vacía.</summary>
    public static MoveInput Idle(uint seq, Facing facing) => new(seq, 0, 0, facing);

    /// <summary>Verdadero si las dos componentes están en <c>[-1, 1]</c> y el <c>facing</c> es válido.</summary>
    public bool IsWellFormed() =>
        DirX is >= -1 and <= 1 &&
        DirY is >= -1 and <= 1 &&
        Facing is >= Facing.North and <= Facing.West;
}

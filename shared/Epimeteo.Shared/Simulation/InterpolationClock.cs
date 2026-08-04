namespace Epimeteo.Shared.Simulation;

/// <summary>
/// El reloj con el que se dibujan las entidades remotas. Avanza solo con el tiempo real y se va
/// pegando al objetivo que fija cada snapshot: <c>serverTick − retraso de interpolación</c>.
/// <para>
/// La gracia está en <b>cómo</b> corrige el desfase. Saltar al objetivo cada vez que llega un
/// snapshot haría que todo el mundo se moviera a tirones, porque los snapshots no llegan a
/// intervalos perfectos. En vez de eso se acelera o se frena el reloj un
/// <see cref="Correction"/> —un 10 %, que el ojo no distingue— y sólo se salta cuando el desfase
/// es tan grande que arrastrarlo poco a poco tardaría segundos (FASE-04 §7).
/// </para>
/// </summary>
public sealed class InterpolationClock
{
    /// <summary>Desfase, en ticks, a partir del cual se salta en vez de corregir poco a poco.</summary>
    public const double MaxDriftTicks = 5.0;

    /// <summary>Cuánto se acelera o frena el reloj para recuperar un desfase pequeño.</summary>
    public const double Correction = 0.1;

    /// <summary>Desfase por debajo del cual se considera que ya está en hora y no se toca el ritmo.</summary>
    private const double DeadZoneTicks = 0.5;

    /// <summary>Retraso respecto al último snapshot, en ticks. Derivado de la constante compartida.</summary>
    public static readonly double DelayTicks =
        (double)SimulationConstants.InterpolationDelayMs / SimulationConstants.TickDtMs;

    /// <summary>Instante que se está dibujando, en ticks de servidor (fraccionario).</summary>
    public double RenderTick { get; private set; }

    /// <summary>Instante al que se quiere llegar, según el último snapshot.</summary>
    public double TargetTick { get; private set; }

    /// <summary>Ritmo actual del reloj: 1 = en hora, 1,1 = recuperando, 0,9 = esperando.</summary>
    public double Rate { get; private set; } = 1.0;

    /// <summary>Falso hasta que llega el primer snapshot; antes no hay nada que dibujar.</summary>
    public bool IsStarted { get; private set; }

    /// <summary>Veces que ha habido que saltar. Diagnóstico: saltar se ve, y conviene saberlo.</summary>
    public int Jumps { get; private set; }

    /// <summary>
    /// Fija el objetivo con el tick de un snapshot recién llegado. El primero arranca el reloj en
    /// hora en vez de dejarlo recuperar desde cero.
    /// </summary>
    public void OnSnapshot(long serverTick)
    {
        TargetTick = serverTick - DelayTicks;

        if (!IsStarted)
        {
            RenderTick = TargetTick;
            IsStarted = true;
        }
    }

    /// <summary>Avanza el reloj con el tiempo real transcurrido.</summary>
    /// <param name="deltaSeconds">Segundos desde el frame anterior.</param>
    public void Advance(double deltaSeconds)
    {
        if (!IsStarted)
        {
            return;
        }

        RenderTick += deltaSeconds * SimulationConstants.TickRate * Rate;

        var drift = TargetTick - RenderTick;

        if (drift > MaxDriftTicks || drift < -MaxDriftTicks)
        {
            RenderTick = TargetTick;
            Rate = 1.0;
            Jumps++;
            return;
        }

        Rate = drift switch
        {
            > DeadZoneTicks => 1.0 + Correction,
            < -DeadZoneTicks => 1.0 - Correction,
            _ => 1.0,
        };
    }
}

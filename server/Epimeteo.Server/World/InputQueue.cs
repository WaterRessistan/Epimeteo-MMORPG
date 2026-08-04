using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>Qué hizo la cola con un input que acaba de llegar.</summary>
public enum InputAdmission
{
    /// <summary>Encolado.</summary>
    Accepted,

    /// <summary>Encolado, pero la cola estaba llena y se tiró el más antiguo.</summary>
    AcceptedDroppingOldest,

    /// <summary><c>Seq</c> repetido o hacia atrás: reenvío, reordenación o replay.</summary>
    RejectedStaleSeq,

    /// <summary>Se pasó del presupuesto de inputs por segundo. Cuenta como strike de anticheat.</summary>
    RejectedBudget,
}

/// <summary>
/// La cola de inputs de un jugador: jitter buffer y presupuesto anti-speedhack a la vez
/// (FASE-04 §2 D5).
/// <para>
/// Con inputs de paso fijo, "cuánto puede moverse un jugador por segundo" y "cuántos inputs le
/// acepto por segundo" son la misma pregunta. Por eso el control de velocidad es este cubo de
/// fichas —20/s de ritmo sostenido, ráfaga de 6 para absorber jitter de red— y no una cuenta de
/// distancia con floats: un cliente que inunda de inputs no consigue recorrer más terreno, sólo
/// que se le descarten y se le apunten strikes.
/// </para>
/// </summary>
public sealed class InputQueue
{
    private const int RatePerSecond = SimulationConstants.TickRate;
    private const int Burst = 6;

    private readonly Queue<MoveInput> _pending = new();
    private double _tokens = Burst;
    private long _lastRefillMs;
    private uint _lastSeq;

    /// <param name="nowMs">Instante monotónico en que el jugador entra al mundo.</param>
    public InputQueue(long nowMs) => _lastRefillMs = nowMs;

    /// <summary>Inputs esperando a ser simulados.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Último <c>seq</c> ya consumido por la simulación. Es lo que viaja en el snapshot.</summary>
    public uint LastAckedSeq { get; private set; }

    /// <summary>Encola un input recién llegado del cliente.</summary>
    public InputAdmission TryEnqueue(in MoveInput input, long nowMs)
    {
        // Estrictamente creciente: un seq repetido es un paquete reenviado o un intento de
        // reproducir un movimiento ya validado.
        if (input.Seq <= _lastSeq)
        {
            return InputAdmission.RejectedStaleSeq;
        }

        Refill(nowMs);
        if (_tokens < 1.0)
        {
            return InputAdmission.RejectedBudget;
        }

        _tokens -= 1.0;
        _lastSeq = input.Seq;

        var admission = InputAdmission.Accepted;
        if (_pending.Count >= SimulationConstants.MaxQueuedInputs)
        {
            // Cola desbordada: o el cliente inunda, o viene de un corte largo. Reproducir varios
            // segundos de inputs viejos es peor que dar un salto.
            _pending.Dequeue();
            admission = InputAdmission.AcceptedDroppingOldest;
        }

        _pending.Enqueue(input);
        return admission;
    }

    /// <summary>
    /// Saca los inputs que toca simular en este tick: uno normalmente, dos si la cola se ha
    /// acumulado (llegaron en ráfaga tras un pico de latencia). Devuelve cuántos ha escrito.
    /// Cero significa cola vacía: el llamante debe simular un paso <b>sin dirección</b>, nunca
    /// repetir el último input.
    /// </summary>
    public int Dequeue(Span<MoveInput> destination)
    {
        var count = _pending.Count > SimulationConstants.InputCatchUpThreshold ? 2 : 1;
        count = Math.Min(Math.Min(count, _pending.Count), destination.Length);

        for (var i = 0; i < count; i++)
        {
            var input = _pending.Dequeue();
            destination[i] = input;
            LastAckedSeq = input.Seq;
        }

        return count;
    }

    private void Refill(long nowMs)
    {
        var elapsed = nowMs - _lastRefillMs;
        if (elapsed <= 0)
        {
            return;
        }

        _lastRefillMs = nowMs;
        _tokens = Math.Min(Burst, _tokens + (elapsed * RatePerSecond / 1000.0));
    }
}

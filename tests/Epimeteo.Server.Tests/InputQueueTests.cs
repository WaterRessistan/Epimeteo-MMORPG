using Epimeteo.Server.World;
using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// La cola de inputs es a la vez jitter buffer y control de velocidad (FASE-04 §2 D5): con paso
/// fijo, "cuántos inputs acepto" es exactamente "cuánto puede moverse".
/// </summary>
public sealed class InputQueueTests
{
    private static MoveInput Input(uint seq) => new(seq, 1, 0, Facing.East);

    [Fact]
    public void PorDefecto_SeConsumeUnInputPorTick()
    {
        var queue = new InputQueue(0);
        Span<MoveInput> buffer = stackalloc MoveInput[2];

        queue.TryEnqueue(Input(1), 0);
        queue.TryEnqueue(Input(2), 50);

        Assert.Equal(1, queue.Dequeue(buffer));
        Assert.Equal(1u, queue.LastAckedSeq);
    }

    /// <summary>
    /// Si llegan en ráfaga tras un pico de latencia, se consumen dos por tick para vaciar el
    /// atasco. Sin esto, el jugador arrastraría el retraso para siempre.
    /// </summary>
    [Fact]
    public void ConLaColaAcumulada_SeConsumenDos()
    {
        var queue = new InputQueue(0);
        Span<MoveInput> buffer = stackalloc MoveInput[2];

        for (var i = 1u; i <= 5; i++)
        {
            Assert.Equal(InputAdmission.Accepted, queue.TryEnqueue(Input(i), i * 50));
        }

        Assert.Equal(2, queue.Dequeue(buffer));
        Assert.Equal(2u, queue.LastAckedSeq);
    }

    [Fact]
    public void ConLaColaVacia_NoSeConsumeNada()
    {
        var queue = new InputQueue(0);
        Span<MoveInput> buffer = stackalloc MoveInput[2];

        Assert.Equal(0, queue.Dequeue(buffer));
        Assert.Equal(0u, queue.LastAckedSeq);
    }

    [Fact]
    public void UnSeqRepetidoOAtrasado_SeDescarta()
    {
        var queue = new InputQueue(0);

        Assert.Equal(InputAdmission.Accepted, queue.TryEnqueue(Input(5), 0));
        Assert.Equal(InputAdmission.RejectedStaleSeq, queue.TryEnqueue(Input(5), 50));
        Assert.Equal(InputAdmission.RejectedStaleSeq, queue.TryEnqueue(Input(4), 100));
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void ConLaColaLlena_SeTiraElMasAntiguo()
    {
        var queue = new InputQueue(0);

        // Un segundo entero de inputs a ritmo nominal: la cola no se vacía porque nadie simula.
        InputAdmission last = InputAdmission.Accepted;
        for (var i = 1u; i <= 20; i++)
        {
            var admission = queue.TryEnqueue(Input(i), i * 50);
            if (admission is InputAdmission.Accepted or InputAdmission.AcceptedDroppingOldest)
            {
                last = admission;
            }
        }

        Assert.Equal(InputAdmission.AcceptedDroppingOldest, last);
        Assert.Equal(SimulationConstants.MaxQueuedInputs, queue.PendingCount);
    }

    /// <summary>
    /// El test de anti-speedhack: inundar de inputs no consigue más movimiento, sólo rechazos.
    /// En un segundo no se pueden colar más de 20 + la ráfaga inicial.
    /// </summary>
    [Fact]
    public void InundandoDeInputs_ElPresupuestoLosCorta()
    {
        var queue = new InputQueue(0);
        Span<MoveInput> buffer = stackalloc MoveInput[2];
        var consumed = 0;
        var seq = 1u;

        // 1 segundo simulado a 20 ticks, con el cliente mandando 5 inputs por tick (100/s).
        for (var tick = 0; tick < 20; tick++)
        {
            var nowMs = tick * 50L;
            for (var i = 0; i < 5; i++)
            {
                queue.TryEnqueue(Input(seq++), nowMs);
            }

            consumed += queue.Dequeue(buffer);
        }

        // 20 ticks × 2 (catch-up) es el techo de consumo; lo que importa es que el presupuesto
        // deje el consumo cerca del ritmo honesto y no en los 100 inputs que mandó.
        Assert.InRange(consumed, 20, 30);
    }

    [Fact]
    public void ARitmoNominal_NoSeRechazaNingunInput()
    {
        var queue = new InputQueue(0);
        Span<MoveInput> buffer = stackalloc MoveInput[2];

        for (var tick = 1u; tick <= 200; tick++)
        {
            Assert.Equal(InputAdmission.Accepted, queue.TryEnqueue(Input(tick), tick * 50));
            queue.Dequeue(buffer);
        }

        Assert.Equal(200u, queue.LastAckedSeq);
    }
}

using Epimeteo.Shared.Time;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.World;

/// <summary>
/// Bucle de simulación: un hilo dedicado que despierta a <c>TickRate</c> Hz.
/// En la Fase 1 gira en vacío salvo por el mantenimiento de sesiones; su razón de existir ahora
/// es dejar fijados el reloj, la compensación de deriva y la instrumentación.
/// <para>
/// Es un <see cref="Thread"/> y no un <see cref="Task"/> a propósito: no queremos que el
/// planificador del ThreadPool decida cuándo simula el mundo.
/// </para>
/// </summary>
public sealed class GameLoop : IDisposable
{
    private readonly int _tickIntervalUs;
    private readonly WorldInbox _inbox;
    private readonly Action<long> _onTick;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _log = Log.ForContext<GameLoop>();
    private Thread? _thread;

    /// <param name="tickRate">Ticks por segundo.</param>
    /// <param name="inbox">Cola de entrada que se drena al principio de cada tick.</param>
    /// <param name="onTick">
    /// Trabajo periódico que aún no pertenece a ningún sistema de mundo (barrido de timeouts).
    /// Recibe el número de tick.
    /// </param>
    public GameLoop(int tickRate, WorldInbox inbox, Action<long> onTick)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        _tickIntervalUs = 1_000_000 / tickRate;
        _inbox = inbox;
        _onTick = onTick;
    }

    /// <summary>Número de ticks completados.</summary>
    public long CurrentTick { get; private set; }

    /// <summary>Métricas de duración de tick, consultables desde <c>/status</c>.</summary>
    public TickMetrics Metrics { get; } = new();

    /// <summary>Arranca el hilo de simulación.</summary>
    public void Start()
    {
        if (_thread is not null)
        {
            throw new InvalidOperationException("El bucle de tick ya está arrancado.");
        }

        _thread = new Thread(Run)
        {
            Name = "Epimeteo.GameLoop",
            IsBackground = false,
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    /// <summary>Pide la parada y espera a que el hilo termine el tick en curso.</summary>
    public void Stop()
    {
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(5));
        _thread = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    private void Run()
    {
        _log.Information("Bucle de tick arrancado a {TickRate} Hz ({IntervalMs} ms por tick)",
            1_000_000 / _tickIntervalUs, _tickIntervalUs / 1000.0);

        var nextTickUs = ServerClock.NowUs;
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            var startUs = ServerClock.NowUs;
            Tick();
            var endUs = ServerClock.NowUs;

            nextTickUs += _tickIntervalUs;
            var remainingUs = nextTickUs - endUs;

            Metrics.Record(endUs - startUs, remainingUs < 0);

            if (remainingUs < -_tickIntervalUs)
            {
                // Vamos más de un tick por detrás. No se hace catch-up: acelerar la simulación
                // para recuperar el retraso es peor que perder ticks, porque duplica el
                // desplazamiento por tick y rompe la predicción del cliente.
                _log.Warning("Tick {Tick} con {RetrasoMs:F1} ms de retraso; se descarta el desfase",
                    CurrentTick, -remainingUs / 1000.0);
                nextTickUs = endUs + _tickIntervalUs;
                continue;
            }

            if (remainingUs > 0)
            {
                token.WaitHandle.WaitOne((int)(remainingUs / 1000));
            }
        }

        _log.Information("Bucle de tick detenido tras {Ticks} ticks", CurrentTick);
    }

    private void Tick()
    {
        CurrentTick++;

        // 1. Drenar lo que ha llegado por red desde el tick anterior.
        while (_inbox.TryDequeue(out var message))
        {
            // Sin sistemas de mundo todavía (Fase 4). Si algo llega aquí es un fallo de
            // enrutado, no un mensaje de un cliente: la tabla de opcodes ya lo habría rechazado.
            _log.Warning("Mensaje {Opcode} de la sesión {SessionId} sin sistema que lo atienda",
                message.Opcode, message.SessionId);
        }

        // 2. Sistemas de mundo: vacío en la Fase 1.

        // 3. Mantenimiento periódico (timeouts de sesión).
        _onTick(CurrentTick);
    }
}

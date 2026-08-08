using Epimeteo.Server.Observability;
using Epimeteo.Shared.Time;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.World;

/// <summary>
/// Bucle de simulación: un hilo dedicado que despierta a <c>TickRate</c> Hz y llama al mundo.
/// Aquí sólo vive el ritmo —reloj monotónico, compensación de deriva, métricas—; qué se simula es
/// cosa de <see cref="GameWorld"/>.
/// <para>
/// Es un <see cref="Thread"/> y no un <see cref="Task"/> a propósito: no queremos que el
/// planificador del ThreadPool decida cuándo simula el mundo.
/// </para>
/// </summary>
public sealed class GameLoop : IDisposable
{
    private readonly int _tickIntervalUs;
    private readonly GameWorld _world;
    private readonly Action<long> _onTick;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _log = Log.ForContext<GameLoop>();
    private readonly ServerMetrics? _metrics;
    private Thread? _thread;

    /// <param name="tickRate">Ticks por segundo.</param>
    /// <param name="world">El mundo que se simula en cada tick.</param>
    /// <param name="onTick">
    /// Trabajo periódico que no pertenece a ningún sistema de mundo (barrido de timeouts de
    /// sesión). Recibe el número de tick.
    /// </param>
    public GameLoop(int tickRate, GameWorld world, Action<long> onTick, ServerMetrics? metrics = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        _tickIntervalUs = 1_000_000 / tickRate;
        _world = world;
        _onTick = onTick;
        _metrics = metrics;
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

            var durationUs = endUs - startUs;
            Metrics.Record(durationUs, remainingUs < 0);

            // Las mismas cifras que ya lleva TickMetrics para /status, pero en el formato que
            // Prometheus sabe agregar entre reinicios (FASE-13 §2 D1).
            if (_metrics is not null)
            {
                _metrics.TicksTotal.Increment();
                _metrics.TickDurationMicros.Observe(durationUs);
                if (remainingUs < 0)
                {
                    _metrics.TickOverruns.Increment();
                }
            }

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

        // El mundo drena sus colas, simula, recalcula AOI, manda snapshots y encola guardados,
        // en el orden de docs/00 §4.
        _world.Tick(CurrentTick);

        // Mantenimiento periódico ajeno al mundo (timeouts de sesión).
        _onTick(CurrentTick);
    }
}

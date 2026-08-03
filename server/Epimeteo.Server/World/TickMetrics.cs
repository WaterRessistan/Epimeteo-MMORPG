namespace Epimeteo.Server.World;

/// <summary>Fotografía de las métricas del bucle de tick, en microsegundos.</summary>
/// <param name="Ticks">Ticks ejecutados desde el arranque.</param>
/// <param name="Overruns">Ticks que tardaron más que el intervalo nominal.</param>
/// <param name="LastUs">Duración del último tick.</param>
/// <param name="AvgUs">Media de la ventana.</param>
/// <param name="P99Us">Percentil 99 de la ventana.</param>
/// <param name="MaxUs">Máximo de la ventana.</param>
public readonly record struct TickStats(long Ticks, long Overruns, long LastUs, long AvgUs, long P99Us, long MaxUs);

/// <summary>
/// Ventana deslizante con la duración de los últimos ticks. El coste de mantenerla es una
/// escritura en un array; el <c>lock</c> sólo se disputa 20 veces por segundo contra
/// alguna lectura ocasional de <c>/status</c>.
/// </summary>
public sealed class TickMetrics
{
    private const int WindowSize = 100;

    private readonly long[] _window = new long[WindowSize];
    private readonly object _gate = new();
    private int _count;
    private int _next;
    private long _ticks;
    private long _overruns;
    private long _lastUs;

    /// <summary>Registra la duración de un tick y si se pasó del intervalo.</summary>
    public void Record(long durationUs, bool overran)
    {
        lock (_gate)
        {
            _window[_next] = durationUs;
            _next = (_next + 1) % WindowSize;
            if (_count < WindowSize)
            {
                _count++;
            }

            _ticks++;
            _lastUs = durationUs;
            if (overran)
            {
                _overruns++;
            }
        }
    }

    /// <summary>Calcula las estadísticas actuales. Seguro desde cualquier hilo.</summary>
    public TickStats Snapshot()
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                return new TickStats(0, 0, 0, 0, 0, 0);
            }

            var sample = new long[_count];
            Array.Copy(_window, sample, _count);
            Array.Sort(sample);

            long sum = 0;
            foreach (var v in sample)
            {
                sum += v;
            }

            var p99Index = Math.Min(_count - 1, (int)Math.Ceiling(_count * 0.99) - 1);
            return new TickStats(
                Ticks: _ticks,
                Overruns: _overruns,
                LastUs: _lastUs,
                AvgUs: sum / _count,
                P99Us: sample[Math.Max(0, p99Index)],
                MaxUs: sample[_count - 1]);
        }
    }
}

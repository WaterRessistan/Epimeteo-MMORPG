namespace Epimeteo.Server.Observability;

/// <summary>
/// Histograma de Prometheus con <i>buckets</i> fijos declarados al construirlo. Cada observación
/// suma 1 a todos los buckets cuyo límite superior la contiene — que es como Prometheus espera
/// que sean, acumulativos, para que <c>histogram_quantile()</c> funcione.
/// <para>
/// Sin buckets dinámicos ni etiquetas a propósito (FASE-13 §2 D1): lo que se mide aquí son
/// latencias con un rango conocido de antemano, y un array de <c>long</c> incrementado con
/// <see cref="Interlocked"/> vale de sobra. Si algún día hacen falta etiquetas de cardinalidad
/// alta, ahí sí compensa una librería de verdad.
/// </para>
/// </summary>
public sealed class Histogram
{
    private readonly double[] _upperBounds;
    private readonly long[] _counts;
    private long _count;
    private long _sumMicros;

    /// <param name="upperBounds">Límites superiores, en orden creciente. Se añade <c>+Inf</c> solo.</param>
    public Histogram(string name, string help, double[] upperBounds)
    {
        ArgumentNullException.ThrowIfNull(upperBounds);
        if (upperBounds.Length == 0)
        {
            throw new ArgumentException("Un histograma necesita al menos un bucket.", nameof(upperBounds));
        }

        Name = name;
        Help = help;
        _upperBounds = upperBounds;

        // Un hueco más que límites: el último es el +Inf, que Prometheus exige siempre.
        _counts = new long[upperBounds.Length + 1];
    }

    public string Name { get; }

    public string Help { get; }

    /// <summary>Límites declarados, sin el <c>+Inf</c>.</summary>
    public IReadOnlyList<double> UpperBounds => _upperBounds;

    public long Count => Interlocked.Read(ref _count);

    /// <summary>Suma de todo lo observado. La unidad es la misma con la que se llamó a <see cref="Observe"/>.</summary>
    public double Sum => Interlocked.Read(ref _sumMicros);

    /// <summary>Cuenta acumulada de cada bucket, con el <c>+Inf</c> al final.</summary>
    public long[] CumulativeCounts()
    {
        var snapshot = new long[_counts.Length];
        long running = 0;

        for (var i = 0; i < _counts.Length; i++)
        {
            running += Interlocked.Read(ref _counts[i]);
            snapshot[i] = running;
        }

        return snapshot;
    }

    /// <summary>Anota una observación. Seguro desde cualquier hilo.</summary>
    public void Observe(double value)
    {
        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _sumMicros, (long)value);

        // Se incrementa sólo el bucket exacto y la acumulación se hace al leer: así una
        // observación es un único Interlocked, no uno por bucket.
        for (var i = 0; i < _upperBounds.Length; i++)
        {
            if (value <= _upperBounds[i])
            {
                Interlocked.Increment(ref _counts[i]);
                return;
            }
        }

        Interlocked.Increment(ref _counts[^1]);
    }
}

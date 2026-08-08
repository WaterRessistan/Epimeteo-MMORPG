using System.Globalization;
using System.Text;

namespace Epimeteo.Server.Observability;

/// <summary>
/// El registro de métricas y su exposición en el formato de texto de Prometheus (FASE-13 §2 D1).
/// Hecho a mano en vez de con <c>prometheus-net</c>: el formato es texto plano trivial, las
/// métricas de este servidor son pocas y de tipos simples, y la tabla de stack de CLAUDE.md §2 no
/// incluye ninguna librería de métricas — mismo criterio que <c>TokenBucket</c> o
/// <c>DeterministicRng</c>.
/// </summary>
public sealed class MetricsRegistry
{
    private readonly List<Counter> _counters = [];
    private readonly List<Gauge> _gauges = [];
    private readonly List<Histogram> _histograms = [];

    public Counter Counter(string name, string help)
    {
        var counter = new Counter(name, help);
        _counters.Add(counter);
        return counter;
    }

    public Gauge Gauge(string name, string help)
    {
        var gauge = new Gauge(name, help);
        _gauges.Add(gauge);
        return gauge;
    }

    public Gauge Gauge(string name, string help, Func<double> source)
    {
        var gauge = new Gauge(name, help, source);
        _gauges.Add(gauge);
        return gauge;
    }

    public Histogram Histogram(string name, string help, double[] upperBounds)
    {
        var histogram = new Histogram(name, help, upperBounds);
        _histograms.Add(histogram);
        return histogram;
    }

    /// <summary>
    /// Serializa todo en el formato de exposición de Prometheus. Los números van con
    /// <see cref="CultureInfo.InvariantCulture"/> siempre: en una máquina con locale español,
    /// <c>ToString()</c> escribiría <c>0,5</c> y Prometheus rechazaría la respuesta entera.
    /// </summary>
    public string Render()
    {
        var text = new StringBuilder();

        foreach (var counter in _counters)
        {
            AppendHeader(text, counter.Name, counter.Help, "counter");
            text.Append(counter.Name).Append(' ').Append(counter.Value).Append('\n');
        }

        foreach (var gauge in _gauges)
        {
            AppendHeader(text, gauge.Name, gauge.Help, "gauge");
            text.Append(gauge.Name).Append(' ').Append(Number(gauge.Value)).Append('\n');
        }

        foreach (var histogram in _histograms)
        {
            AppendHeader(text, histogram.Name, histogram.Help, "histogram");

            var cumulative = histogram.CumulativeCounts();
            for (var i = 0; i < histogram.UpperBounds.Count; i++)
            {
                text.Append(histogram.Name)
                    .Append("_bucket{le=\"")
                    .Append(Number(histogram.UpperBounds[i]))
                    .Append("\"} ")
                    .Append(cumulative[i])
                    .Append('\n');
            }

            text.Append(histogram.Name).Append("_bucket{le=\"+Inf\"} ").Append(cumulative[^1]).Append('\n');
            text.Append(histogram.Name).Append("_sum ").Append(Number(histogram.Sum)).Append('\n');
            text.Append(histogram.Name).Append("_count ").Append(histogram.Count).Append('\n');
        }

        return text.ToString();
    }

    private static void AppendHeader(StringBuilder text, string name, string help, string type) =>
        text.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n')
            .Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

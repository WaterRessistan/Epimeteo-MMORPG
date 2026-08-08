namespace Epimeteo.Server.Observability;

/// <summary>
/// Un valor instantáneo de Prometheus: sube y baja (jugadores conectados, profundidad de una
/// cola). A diferencia de <see cref="Counter"/>, el valor se <b>fija</b>, no se acumula.
/// <para>
/// Muchos gauges de este servidor se leen de una estructura que ya existe (el número de jugadores
/// lo sabe <c>GameWorld</c>), así que en vez de obligar a alguien a acordarse de actualizarlos en
/// cada cambio, se construyen con una función que se evalúa al exponer: imposible que se queden
/// desactualizados.
/// </para>
/// </summary>
public sealed class Gauge
{
    private readonly Func<double>? _source;
    private long _value;

    /// <summary>Gauge que alguien fija a mano con <see cref="Set"/>.</summary>
    public Gauge(string name, string help)
    {
        Name = name;
        Help = help;
    }

    /// <summary>Gauge que se lee de <paramref name="source"/> en el momento de exponerlo.</summary>
    public Gauge(string name, string help, Func<double> source)
    {
        Name = name;
        Help = help;
        _source = source;
    }

    public string Name { get; }

    public string Help { get; }

    public double Value => _source is not null ? _source() : Interlocked.Read(ref _value);

    public void Set(long value) => Interlocked.Exchange(ref _value, value);
}

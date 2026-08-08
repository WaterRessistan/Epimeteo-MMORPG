namespace Epimeteo.Server.Observability;

/// <summary>
/// Un contador monótono de Prometheus: sólo sube, y se reinicia a 0 sólo si el proceso reinicia
/// (que es justo lo que Prometheus sabe interpretar con <c>rate()</c>).
/// <para>
/// Lo incrementan varios hilos —la red, el tick, las colas de guardado— así que la escritura va
/// por <see cref="Interlocked"/>. No hay <c>lock</c>: un contador no necesita más.
/// </para>
/// </summary>
public sealed class Counter
{
    private long _value;

    public Counter(string name, string help)
    {
        Name = name;
        Help = help;
    }

    public string Name { get; }

    public string Help { get; }

    public long Value => Interlocked.Read(ref _value);

    public void Increment() => Interlocked.Increment(ref _value);

    public void Add(long amount) => Interlocked.Add(ref _value, amount);
}

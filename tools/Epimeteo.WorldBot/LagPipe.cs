namespace Epimeteo.WorldBot;

/// <summary>
/// Simulador de latencia: retiene cada frame el tiempo pedido antes de dejarlo pasar, en los dos
/// sentidos. Es lo que permite comprobar el criterio de "con 150 ms sigue siendo jugable" sin
/// tocar <c>tc netem</c> en la máquina de producción (FASE-04 §7).
/// <para>
/// Con <c>lagMs = 0</c> no retiene nada, así que el camino sin latencia es el mismo código.
/// </para>
/// </summary>
internal sealed class LagPipe<T>
{
    private readonly Queue<(long DueMs, T Item)> _queue = new();
    private readonly int _lagMs;

    public LagPipe(int lagMs) => _lagMs = lagMs;

    public void Push(T item, long nowMs) => _queue.Enqueue((nowMs + _lagMs, item));

    /// <summary>Saca lo que ya ha cumplido su retardo, en orden de llegada.</summary>
    public bool TryPop(long nowMs, out T item)
    {
        if (_queue.Count > 0 && _queue.Peek().DueMs <= nowMs)
        {
            item = _queue.Dequeue().Item;
            return true;
        }

        item = default!;
        return false;
    }
}

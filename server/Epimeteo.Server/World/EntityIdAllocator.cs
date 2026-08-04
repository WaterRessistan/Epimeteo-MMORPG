namespace Epimeteo.Server.World;

/// <summary>
/// Reparte ids de entidad. Espacio propio, sin relación con <c>characters.id</c>: un id de entidad
/// es efímero (vive lo que la entidad esté en el mundo) y lo comparten jugadores, monstruos y NPCs,
/// así que el cliente puede tratarlos a todos igual.
/// <para>
/// Es atómico porque se reserva desde el hilo de red —en <c>CharSelect</c>, para poder mandarlo en
/// <c>WorldEnter</c>— aunque la entidad se cree luego en el hilo del tick.
/// </para>
/// </summary>
public sealed class EntityIdAllocator
{
    private int _next;

    /// <summary>Devuelve un id nuevo. El 0 no se usa nunca: sirve de "sin entidad".</summary>
    public int Next() => Interlocked.Increment(ref _next);
}

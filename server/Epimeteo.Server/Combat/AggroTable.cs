namespace Epimeteo.Server.Combat;

/// <summary>
/// Amenaza acumulada por cada entidad contra un monstruo (FASE-09 §2 D6).
/// <para>
/// Un solo <c>TargetId</c> no vale: con dos jugadores pegando al mismo monstruo, cada golpe le
/// robaría el objetivo al otro y el monstruo se pasaría la pelea girando sin pegar a nadie. Con
/// una tabla, el objetivo es simplemente quien más amenaza lleva, y sólo cambia cuando alguien de
/// verdad adelanta al que iba primero.
/// </para>
/// </summary>
public sealed class AggroTable
{
    private readonly Dictionary<int, long> _threat = [];

    /// <summary>Entidades con amenaza. Sólo lectura.</summary>
    public IReadOnlyDictionary<int, long> Threat => _threat;

    /// <summary>Suma amenaza a una entidad. El daño hecho es la fuente natural.</summary>
    public void Add(int entityId, long amount)
    {
        if (amount <= 0)
        {
            return;
        }

        _threat[entityId] = _threat.GetValueOrDefault(entityId) + amount;
    }

    /// <summary>Saca a una entidad de la tabla: murió, se fue del mundo o el monstruo la perdió.</summary>
    public void Remove(int entityId) => _threat.Remove(entityId);

    /// <summary>Vacía la tabla. Se llama cuando el monstruo vuelve a su sitio (D7).</summary>
    public void Clear() => _threat.Clear();

    /// <summary>Verdadero si alguien tiene amenaza.</summary>
    public bool Any => _threat.Count > 0;

    /// <summary>
    /// Quién lleva más amenaza, o <c>null</c> si la tabla está vacía. Con empate gana el id más
    /// bajo: da igual quién, pero tiene que ser <b>estable</b> — si el desempate fuera arbitrario,
    /// el monstruo cambiaría de objetivo cada tick sin que pasara nada.
    /// </summary>
    public int? Top()
    {
        int? best = null;
        long bestThreat = 0;

        foreach (var (entityId, threat) in _threat)
        {
            if (best is null || threat > bestThreat || (threat == bestThreat && entityId < best))
            {
                best = entityId;
                bestThreat = threat;
            }
        }

        return best;
    }
}

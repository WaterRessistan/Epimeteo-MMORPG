namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Generador determinista con semilla (xorshift64*). Dos instancias con la misma semilla dan
/// exactamente la misma secuencia, que es lo que permite que <see cref="CombatFormulas"/> se
/// pruebe con daños <b>exactos</b> y no con rangos (FASE-09 §2 D4).
/// <para>
/// La instancia real la crea el servidor y no sale de ahí: el cliente no predice daño ni recibe
/// la semilla. Está en <c>Shared</c> por la misma razón que las fórmulas — para poder probarlo
/// sin levantar un servidor, no porque el cliente lo ejecute.
/// </para>
/// <para>
/// No es criptográfico y no pretende serlo: para tokens de sesión está
/// <c>SessionTokenService</c>, que sí usa un generador seguro.
/// </para>
/// </summary>
public sealed class DeterministicRng
{
    private ulong _state;

    /// <param name="seed">Cualquier valor; el 0 se sustituye porque xorshift se quedaría clavado en 0.</param>
    public DeterministicRng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    /// <summary>Siguiente valor crudo de 64 bits.</summary>
    public ulong NextUInt64()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Entero en <c>[0, exclusiveMax)</c>. <paramref name="exclusiveMax"/> tiene que ser positivo.</summary>
    public int NextInt(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exclusiveMax, 1);

        // Módulo simple: el sesgo con valores tan pequeños (dispersión de daño, tiradas de loot)
        // está muy por debajo de lo que nadie pueda notar o explotar.
        return (int)(NextUInt64() % (ulong)exclusiveMax);
    }

    /// <summary>Entero en <c>[inclusiveMin, exclusiveMax)</c>.</summary>
    public int NextInt(int inclusiveMin, int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(inclusiveMin, exclusiveMax - 1);
        return inclusiveMin + NextInt(exclusiveMax - inclusiveMin);
    }

    /// <summary>Fracción en <c>[0, 1)</c>, con 53 bits de mantisa.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Verdadero con probabilidad <paramref name="probability"/> (0 nunca, 1 siempre).</summary>
    public bool NextChance(double probability) => probability > 0 && NextDouble() < probability;
}

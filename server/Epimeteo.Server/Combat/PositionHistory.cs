using Epimeteo.Shared.Data;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.Combat;

/// <summary>
/// Las últimas posiciones autoritativas de una entidad, para rebobinar el mundo al resolver un
/// golpe (<c>docs/00 §6</c>, FASE-09 §2 D1 y D2).
/// <para>
/// Sin esto, un jugador con 150 ms de latencia le tira a donde ve a su objetivo y falla siempre,
/// porque para cuando el ataque llega al servidor la víctima ya se movió. Con esto, el servidor
/// resuelve el alcance contra la posición que la víctima ocupaba cuando el atacante la vio.
/// </para>
/// <para>
/// Es un anillo de tamaño fijo: <c>CombatConstants.PositionHistoryMs</c> a la frecuencia de tick.
/// Vive en <c>Server</c> y no en <c>Shared</c> a propósito — el cliente no rebobina nada; esto es
/// maquinaria autoritativa, no simulación compartida.
/// </para>
/// </summary>
public sealed class PositionHistory
{
    private readonly (long AtMs, Vec2 Pos)[] _samples;
    private int _count;
    private int _next;

    public PositionHistory()
    {
        var capacity = Math.Max(1, CombatConstants.PositionHistoryMs / SimulationConstants.TickDtMs);
        _samples = new (long, Vec2)[capacity];
    }

    /// <summary>Muestras guardadas ahora mismo. Sólo para tests y diagnóstico.</summary>
    public int Count => _count;

    /// <summary>Anota la posición de este tick. Se llama una vez por tick y por entidad.</summary>
    public void Record(long nowMs, Vec2 position)
    {
        _samples[_next] = (nowMs, position);
        _next = (_next + 1) % _samples.Length;

        if (_count < _samples.Length)
        {
            _count++;
        }
    }

    /// <summary>
    /// Olvida el historial y lo deja con una sola muestra en <paramref name="position"/>. Se llama
    /// al teletransportar: si no, un ataque rebobinado alcanzaría al jugador en el punto del que
    /// acaba de salir de golpe, que no es compensar latencia sino inventarse un cuerpo.
    /// </summary>
    public void Reset(long nowMs, Vec2 position)
    {
        _count = 0;
        _next = 0;
        Record(nowMs, position);
    }

    /// <summary>
    /// Dónde estaba la entidad hace <paramref name="rewindMs"/>, o <paramref name="fallback"/> si
    /// no hay historial que llegue tan atrás.
    /// <para>
    /// El rebobinado ya viene clampado por <see cref="RewindFor"/>; aquí sólo se busca la muestra
    /// más cercana al instante pedido. Se elige la <b>más cercana</b> y no la inmediatamente
    /// anterior porque a 20 Hz la diferencia es de medio tick y redondear al lado que toca reparte
    /// el error entre atacante y víctima en vez de dárselo siempre al mismo.
    /// </para>
    /// </summary>
    public Vec2 PositionAt(long nowMs, int rewindMs, Vec2 fallback)
    {
        if (_count == 0 || rewindMs <= 0)
        {
            return fallback;
        }

        var target = nowMs - rewindMs;
        var bestDistance = long.MaxValue;
        var best = fallback;
        var found = false;

        for (var i = 0; i < _count; i++)
        {
            var (atMs, pos) = _samples[i];
            var distance = Math.Abs(atMs - target);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = pos;
                found = true;
            }
        }

        // Si la muestra más cercana se pasa de la ventana que guardamos, el historial no llega tan
        // atrás: se valida contra la posición actual, como manda docs/00 §6.
        if (!found || bestDistance > CombatConstants.PositionHistoryMs)
        {
            return fallback;
        }

        return best;
    }

    /// <summary>
    /// Cuánto hay que rebobinar para un RTT dado: la mitad del viaje de ida y vuelta, con el tope
    /// duro de <c>CombatConstants.MaxRewindMs</c>.
    /// <para>
    /// Ese tope es lo que acota el beneficio de mentir sobre el propio RTT (D1): por mucho que un
    /// cliente parcheado infle su latencia, no consigue rebobinar más que un jugador honesto con
    /// mala conexión.
    /// </para>
    /// </summary>
    public static int RewindFor(int rttMs) => Math.Clamp(rttMs / 2, 0, CombatConstants.MaxRewindMs);
}

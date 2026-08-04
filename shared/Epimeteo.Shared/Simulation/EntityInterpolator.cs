namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Buffer de muestras e interpolación de una entidad que este cliente <b>no</b> controla. Lo que
/// el jugador ve de los demás no es su posición autoritativa actual, sino dónde estaban hace
/// <see cref="SimulationConstants.InterpolationDelayMs"/> ms: ese retraso es lo que garantiza
/// tener siempre una muestra antes y otra después del instante que se dibuja, y por tanto no
/// tener que inventarse posiciones.
/// <para>
/// Vive en <c>Shared</c> y no en el proyecto de Godot por el mismo motivo que
/// <see cref="ClientPrediction"/>: es netcode, y aquí se puede probar sin abrir un motor gráfico.
/// </para>
/// </summary>
public sealed class EntityInterpolator
{
    /// <summary>
    /// Muestras guardadas. Con snapshots a 10 Hz son 1,6 s de historia: de sobra para los 100 ms
    /// de retraso más cualquier hueco razonable de red.
    /// </summary>
    public const int Capacity = 16;

    private readonly List<(double Tick, MoveState State)> _samples = new(Capacity);

    /// <param name="initial">Pose inicial, la que trae el <c>EntitySpawn</c>.</param>
    public EntityInterpolator(MoveState initial) => Current = initial;

    /// <summary>Pose que toca dibujar ahora mismo.</summary>
    public MoveState Current { get; private set; }

    /// <summary>Muestras en el buffer.</summary>
    public int SampleCount => _samples.Count;

    /// <summary>Guarda lo que el servidor dice que era esta entidad en ese tick.</summary>
    public void PushSample(long serverTick, in MoveState state)
    {
        if (_samples.Count == Capacity)
        {
            _samples.RemoveAt(0);
        }

        _samples.Add((serverTick, state));
    }

    /// <summary>
    /// Coloca la entidad en el instante de render pedido, en ticks de servidor (fraccionario).
    /// <para>
    /// Si el instante cae por delante de la última muestra —el buffer se ha quedado seco porque no
    /// llega nada, o porque la entidad está quieta y el servidor ya no la manda— <b>mantiene la
    /// última pose</b> en vez de extrapolar. Un personaje que se para un momento se ve mucho mejor
    /// que uno que patina hacia donde no fue y luego salta hacia atrás cuando llega el paquete.
    /// </para>
    /// </summary>
    public void Interpolate(double renderTick)
    {
        if (_samples.Count == 0)
        {
            return;
        }

        var last = _samples[^1];
        if (_samples.Count == 1 || renderTick >= last.Tick)
        {
            Current = last.State;
            return;
        }

        var first = _samples[0];
        if (renderTick <= first.Tick)
        {
            Current = first.State;
            return;
        }

        for (var i = _samples.Count - 1; i > 0; i--)
        {
            var to = _samples[i];
            var from = _samples[i - 1];

            if (renderTick < from.Tick)
            {
                continue;
            }

            var span = to.Tick - from.Tick;
            var alpha = span <= 0 ? 1f : (float)((renderTick - from.Tick) / span);

            Current = new MoveState(
                new Vec2(
                    from.State.Pos.X + ((to.State.Pos.X - from.State.Pos.X) * alpha),
                    from.State.Pos.Y + ((to.State.Pos.Y - from.State.Pos.Y) * alpha)),
                to.State.Vel,

                // Orientación y animación son estados discretos: no se interpolan. Se toma la
                // muestra de destino, que es hacia donde la entidad ya va.
                to.State.Facing,
                to.State.Anim);

            return;
        }
    }

    /// <summary>
    /// Tira las muestras que ya no se van a usar. Conserva siempre una por debajo del instante de
    /// render: es el extremo izquierdo del tramo que se está interpolando ahora.
    /// </summary>
    public void TrimBefore(double renderTick)
    {
        while (_samples.Count > 2 && _samples[1].Tick <= renderTick)
        {
            _samples.RemoveAt(0);
        }
    }
}

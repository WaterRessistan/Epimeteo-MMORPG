using Epimeteo.Client.Net;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using Godot;

namespace Epimeteo.Client.World;

/// <summary>
/// El personaje que controla este cliente. Es la pieza que hace que el juego se sienta inmediato
/// con el servidor a 150 ms: el input se aplica <b>ya</b>, en local, y cuando llega la verdad del
/// servidor se compara con lo que se predijo (FASE-04 §2 D1 y D3).
/// <para>
/// El acumulador es lo importante: no se simula por frame con el <c>delta</c> del render, se
/// acumula tiempo real y cada 50 ms exactos se produce un input y se da <b>un</b> paso. El
/// servidor dará ese mismo paso. Si se integrara con el <c>delta</c> variable de cada frame, los
/// dos lados calcularían números distintos y habría corrección constante aunque nadie hiciera
/// trampas.
/// </para>
/// </summary>
public sealed class LocalPlayer
{
    /// <summary>
    /// Pasos de simulación que se permiten en un solo frame. Dos ya cubren un frame de 100 ms;
    /// más que eso es un parón, no una ralentización, y el presupuesto de inputs del servidor
    /// (20/s con ráfaga de 6) no lo perdonaría.
    /// </summary>
    private const int MaxStepsPerFrame = 2;

    private readonly NetClient _net;
    private readonly GameMap _map;

    private double _accumulatorMs;
    private uint _seq;

    public LocalPlayer(NetClient net, GameMap map, Vec2 spawn, Facing facing)
    {
        _net = net;
        _map = map;
        Prediction = new ClientPrediction(map.Collision, MoveState.AtRest(spawn, facing));
        Previous = Prediction.Predicted;
    }

    /// <summary>Predicción y reconciliación. Vive en <c>Shared</c>: es el mismo código que verificó el WorldBot.</summary>
    public ClientPrediction Prediction { get; }

    /// <summary>Estado predicho al final del paso anterior. Con <see cref="Alpha"/> da el dibujo suave.</summary>
    public MoveState Previous { get; private set; }

    /// <summary>Estado predicho ahora mismo.</summary>
    public MoveState Current => Prediction.Predicted;

    /// <summary>
    /// Fracción del paso de 50 ms ya transcurrida, en <c>[0, 1)</c>. El render interpola entre
    /// <see cref="Previous"/> y <see cref="Current"/> con esto: la simulación va a 20 Hz pero el
    /// personaje se dibuja suave a los fps que dé la máquina.
    /// </summary>
    public float Alpha => (float)(_accumulatorMs / SimulationConstants.TickDtMs);

    /// <summary>Región donde el <b>cliente</b> cree estar. Sólo para el HUD; la verdad la dice el servidor.</summary>
    public MapRegion Region => _map.Regions.Resolve(Current.Pos);

    /// <summary>Posición dibujable, interpolada dentro del paso actual.</summary>
    public Vec2 RenderPos => new(
        Previous.Pos.X + ((Current.Pos.X - Previous.Pos.X) * Alpha),
        Previous.Pos.Y + ((Current.Pos.Y - Previous.Pos.Y) * Alpha));

    /// <summary>
    /// Acumula el tiempo del frame y da tantos pasos de 50 ms como quepan. Normalmente cero o uno;
    /// más de uno sólo si el frame se ha ido de tiempo.
    /// <para>
    /// Con un tope: si el cliente se queda parado un rato —alt-tab, un tirón del sistema, el
    /// portátil suspendido— vaciar el acumulador entero mandaría decenas de inputs en un frame, y
    /// el servidor cuenta eso como intento de correr más de la cuenta y acaba echando al jugador.
    /// Se descarta el desfase en vez de recuperarlo, que es <b>la misma decisión que tomó el
    /// servidor en la Fase 1</b> con su bucle de tick: acelerar la simulación para ponerse al día
    /// rompe más de lo que arregla.
    /// </para>
    /// </summary>
    public void Update(double deltaSeconds)
    {
        _accumulatorMs += deltaSeconds * 1000.0;

        var steps = 0;
        while (_accumulatorMs >= SimulationConstants.TickDtMs && steps < MaxStepsPerFrame)
        {
            _accumulatorMs -= SimulationConstants.TickDtMs;
            steps++;
            StepOnce();
        }

        if (_accumulatorMs >= SimulationConstants.TickDtMs)
        {
            // Lo que sobra se tira: son inputs que ya no se pueden mandar sin parecer un tramposo.
            _accumulatorMs %= SimulationConstants.TickDtMs;
        }
    }

    /// <summary>Aplica el estado autoritativo de un snapshot. Devuelve verdadero si hubo corrección.</summary>
    public bool ApplyAuthoritative(in EntityDelta delta, uint lastAckedSeq)
    {
        var authoritative = new MoveState(
            new Vec2(delta.X, delta.Y),
            new Vec2(delta.Vx, delta.Vy),
            delta.Facing,
            delta.Anim);

        var corrected = Prediction.ApplyAuthoritative(authoritative, lastAckedSeq);

        if (corrected)
        {
            // Tras corregir, el punto de partida del dibujo es el nuevo estado: interpolar desde
            // la pose vieja arrastraría el error a la pantalla justo cuando se acaba de arreglar.
            Previous = Prediction.Predicted;
        }

        return corrected;
    }

    private void StepOnce()
    {
        Previous = Prediction.Predicted;

        var input = ReadInput(++_seq);
        Prediction.ApplyInput(input);
        _net.SendInput(input);
    }

    private MoveInput ReadInput(uint seq)
    {
        var dirX = (sbyte)((Input.IsActionPressed(InputActions.MoveRight) ? 1 : 0) -
                           (Input.IsActionPressed(InputActions.MoveLeft) ? 1 : 0));

        var dirY = (sbyte)((Input.IsActionPressed(InputActions.MoveDown) ? 1 : 0) -
                           (Input.IsActionPressed(InputActions.MoveUp) ? 1 : 0));

        // Sin dirección se conserva la orientación actual; con ella la deriva el MovementSystem,
        // así que aquí sólo hay que mandar algo bien formado.
        return new MoveInput(seq, dirX, dirY, Current.Facing);
    }
}

namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Predicción y reconciliación del jugador local. Vive en <c>Shared</c> —y no en el proyecto de
/// Godot— porque no depende del motor y porque así el cliente de verdad y el bot headless de
/// <c>tools/Epimeteo.WorldBot</c> ejecutan <b>exactamente</b> el mismo netcode: lo que verifica el
/// bot es lo que corre en el juego.
/// <para>
/// El ciclo es el de <c>docs/00 §4</c>: cada 50 ms se aplica un input localmente y se guarda
/// <c>(seq, input, resultado)</c>; cuando llega un snapshot, se tira del buffer todo lo que el
/// servidor ya confirmó y, si la posición confirmada no coincide con lo que se predijo para ese
/// mismo <c>seq</c>, se coloca la autoritativa y se <b>reejecutan</b> los inputs que el servidor
/// todavía no ha visto.
/// </para>
/// </summary>
public sealed class ClientPrediction
{
    /// <summary>
    /// Inputs sin confirmar que se guardan. 64 × 50 ms = 3,2 s: más RTT del que es jugable, así
    /// que en la práctica nunca se llena.
    /// </summary>
    public const int Capacity = 64;

    private record struct Entry(uint Seq, MoveInput Input, MoveState Result);

    private readonly List<Entry> _pending = new(Capacity);
    private readonly CollisionMap _map;

    public ClientPrediction(CollisionMap map, MoveState initial)
    {
        _map = map;
        Predicted = initial;
        Confirmed = initial;
    }

    /// <summary>Estado que el cliente debe dibujar: el suyo, ya con los inputs no confirmados aplicados.</summary>
    public MoveState Predicted { get; private set; }

    /// <summary>Último estado autoritativo recibido del servidor.</summary>
    public MoveState Confirmed { get; private set; }

    /// <summary>Inputs pendientes de que el servidor los confirme.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Veces que ha habido que corregir la predicción. Cero es lo esperado sin latencia.</summary>
    public int Corrections { get; private set; }

    /// <summary>Error de la última corrección, en tiles. Sólo diagnóstico: no entra en la simulación.</summary>
    public float LastErrorTiles { get; private set; }

    /// <summary>Mayor error corregido en toda la sesión, en tiles. Sólo diagnóstico.</summary>
    public float MaxErrorTiles { get; private set; }

    /// <summary>Aplica un input localmente (predicción) y lo guarda a la espera de confirmación.</summary>
    public MoveState ApplyInput(in MoveInput input)
    {
        Predicted = MovementSystem.Step(Predicted, input, _map);

        if (_pending.Count == Capacity)
        {
            // Sólo pasa con un corte de varios segundos. Perder el input más viejo desincroniza
            // menos que quedarse sin sitio para los nuevos.
            _pending.RemoveAt(0);
        }

        _pending.Add(new Entry(input.Seq, input, Predicted));
        return Predicted;
    }

    /// <summary>
    /// Aplica el estado autoritativo de un snapshot. Devuelve verdadero si hubo que corregir.
    /// </summary>
    /// <param name="authoritative">Estado del jugador según el servidor.</param>
    /// <param name="lastAckedSeq">Último input que el servidor dice haber consumido.</param>
    public bool ApplyAuthoritative(in MoveState authoritative, uint lastAckedSeq)
    {
        Confirmed = authoritative;

        // 1. Fuera del buffer todo lo que el servidor ya ha simulado, quedándose con lo que se
        //    predijo para el último input confirmado: es lo único comparable con la autoridad.
        MoveState? predictedAtAck = null;
        var acked = 0;
        while (acked < _pending.Count && _pending[acked].Seq <= lastAckedSeq)
        {
            predictedAtAck = _pending[acked].Result;
            acked++;
        }

        if (acked > 0)
        {
            _pending.RemoveRange(0, acked);
        }

        // 2. Si coincide con lo predicho, no se toca nada: el jugador no nota ni un tirón.
        if (predictedAtAck is { } expected &&
            Vec2.DistanceSquared(expected.Pos, authoritative.Pos) <= SimulationConstants.ReconcileToleranceSquared)
        {
            return false;
        }

        var error = predictedAtAck is { } mismatch
            ? MathF.Sqrt(Vec2.DistanceSquared(mismatch.Pos, authoritative.Pos))
            : 0f;

        // 3. Reconciliación: se acepta la verdad del servidor y se reejecutan los inputs que
        //    todavía no ha visto, con el mismo MovementSystem que usó él. Los resultados
        //    guardados se reescriben: si no, la siguiente comparación se haría contra una
        //    predicción que ya se descartó.
        var state = authoritative;
        for (var i = 0; i < _pending.Count; i++)
        {
            var entry = _pending[i];
            state = MovementSystem.Step(state, entry.Input, _map);
            entry.Result = state;
            _pending[i] = entry;
        }

        Predicted = state;

        if (predictedAtAck is not null)
        {
            Corrections++;
            LastErrorTiles = error;
            MaxErrorTiles = MathF.Max(MaxErrorTiles, error);
        }

        return predictedAtAck is not null;
    }
}

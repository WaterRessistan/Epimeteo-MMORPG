namespace Epimeteo.Shared.Simulation;

/// <summary>
/// El corazón de la fase: la función que convierte un input en una posición. Cliente y servidor
/// ejecutan <b>este mismo código</b> — el servidor para mandar la verdad, el cliente para predecirla
/// y para reejecutar los inputs pendientes al reconciliar.
/// <para>
/// Reglas que no se rompen (FASE-04 §2 D2):
/// sólo <c>+ - *</c>, comparaciones y redondeos exactos (<c>Floor</c>/<c>Ceiling</c>/<c>Min</c>/<c>Max</c>);
/// nada de <c>sqrt</c>, trigonometría, <c>double</c>, <c>DateTime</c>, azar ni tipos de Godot.
/// Es una función pura: mismo estado + mismo input + mismo mapa ⇒ mismo resultado, siempre.
/// </para>
/// </summary>
public static class MovementSystem
{
    private const float HalfWidth = SimulationConstants.PlayerHalfWidth;
    private const float HalfHeight = SimulationConstants.PlayerHalfHeight;

    /// <summary>
    /// Avanza un tick de <see cref="SimulationConstants.TickDtMs"/> ms. Los ejes se resuelven por
    /// separado: si X choca pero Y no, la entidad se desliza por la pared en vez de engancharse.
    /// Es lo que hace que un juego top-down se sienta bien, y es una decisión de simulación, no
    /// de render, así que vive aquí.
    /// </summary>
    public static MoveState Step(in MoveState state, in MoveInput input, CollisionMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var moving = input.DirX != 0 || input.DirY != 0;

        var speed = SimulationConstants.WalkSpeedTilesPerSec;
        if (input.DirX != 0 && input.DirY != 0)
        {
            // La diagonal no se normaliza con sqrt: el factor está precalculado.
            speed *= SimulationConstants.DiagonalFactor;
        }

        var step = speed * SimulationConstants.TickDt;
        var pos = state.Pos;

        if (input.DirX != 0)
        {
            var candidate = new Vec2(pos.X + (input.DirX * step), pos.Y);
            pos = map.IsBlocked(candidate, HalfWidth, HalfHeight)
                ? SlideX(pos, candidate.X, input.DirX, map)
                : candidate;
        }

        if (input.DirY != 0)
        {
            var candidate = new Vec2(pos.X, pos.Y + (input.DirY * step));
            pos = map.IsBlocked(candidate, HalfWidth, HalfHeight)
                ? SlideY(pos, candidate.Y, input.DirY, map)
                : candidate;
        }

        var moved = pos != state.Pos;

        return new MoveState(
            pos,
            (pos - state.Pos) * SimulationConstants.InverseTickDt,
            moving ? FacingFrom(input) : state.Facing,
            moving && moved ? AnimState.Walk : AnimState.Idle);
    }

    /// <summary>
    /// Orientación derivada de la dirección de movimiento. La vertical tiene prioridad en las
    /// diagonales: es una regla cosmética, pero tiene que ser la misma en los dos lados o el
    /// personaje remoto mirará hacia otro lado que el local.
    /// </summary>
    private static Facing FacingFrom(in MoveInput input)
    {
        if (input.DirY != 0)
        {
            return input.DirY < 0 ? Facing.North : Facing.South;
        }

        return input.DirX < 0 ? Facing.West : Facing.East;
    }

    /// <summary>
    /// Pega la entidad al borde del tile que la bloquea en lugar de dejarla a hasta 0.2 tiles de
    /// la pared. Nunca retrocede ni se pasa del candidato, y si el resultado sigue bloqueado
    /// (geometría rara) se queda donde estaba: el fallo seguro es no moverse.
    /// </summary>
    private static Vec2 SlideX(Vec2 origin, float candidateX, sbyte dir, CollisionMap map)
    {
        var snapped = dir > 0
            ? MathF.Floor(candidateX + HalfWidth) - HalfWidth
            : MathF.Floor(candidateX - HalfWidth) + 1f + HalfWidth;

        snapped = dir > 0
            ? MathF.Max(origin.X, MathF.Min(snapped, candidateX))
            : MathF.Min(origin.X, MathF.Max(snapped, candidateX));

        var slid = new Vec2(snapped, origin.Y);
        return map.IsBlocked(slid, HalfWidth, HalfHeight) ? origin : slid;
    }

    /// <inheritdoc cref="SlideX"/>
    private static Vec2 SlideY(Vec2 origin, float candidateY, sbyte dir, CollisionMap map)
    {
        var snapped = dir > 0
            ? MathF.Floor(candidateY + HalfHeight) - HalfHeight
            : MathF.Floor(candidateY - HalfHeight) + 1f + HalfHeight;

        snapped = dir > 0
            ? MathF.Max(origin.Y, MathF.Min(snapped, candidateY))
            : MathF.Min(origin.Y, MathF.Max(snapped, candidateY));

        var slid = new Vec2(origin.X, snapped);
        return map.IsBlocked(slid, HalfWidth, HalfHeight) ? origin : slid;
    }
}

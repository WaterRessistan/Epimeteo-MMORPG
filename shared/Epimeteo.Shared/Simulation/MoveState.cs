namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Estado cinemático de una entidad que se mueve. Es lo que entra y sale del
/// <see cref="MovementSystem"/>, lo que el cliente guarda en su buffer de predicción y lo que
/// viaja en los snapshots.
/// </summary>
/// <param name="Pos">Posición en tiles, en los pies de la entidad.</param>
/// <param name="Vel">Velocidad del último paso en tiles/s. Informativa: el cliente la usa para suavizar.</param>
/// <param name="Facing">Orientación.</param>
/// <param name="Anim">Estado de animación deducido del paso.</param>
public readonly record struct MoveState(Vec2 Pos, Vec2 Vel, Facing Facing, AnimState Anim)
{
    /// <summary>Estado quieto en una posición dada.</summary>
    public static MoveState AtRest(Vec2 pos, Facing facing) => new(pos, Vec2.Zero, facing, AnimState.Idle);
}

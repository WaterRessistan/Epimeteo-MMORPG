namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Estado de animación que el servidor deduce de la simulación y manda en los snapshots.
/// El cliente decide qué sprite corresponde; el servidor no conoce sprites.
/// </summary>
public enum AnimState : byte
{
    /// <summary>Quieto.</summary>
    Idle = 0,

    /// <summary>Andando.</summary>
    Walk = 1,
}

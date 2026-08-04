namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Por qué una entidad deja de ser visible. El cliente lo usa para decidir la transición visual:
/// salir del AOI es desaparecer sin más, morir tiene animación.
/// </summary>
public enum DespawnReason : byte
{
    /// <summary>Se alejó: salió de las 9 celdas de interés del observador.</summary>
    OutOfRange = 0,

    /// <summary>Murió (Fase 9).</summary>
    Death = 1,

    /// <summary>Se desconectó o cambió de mapa.</summary>
    Logout = 2,
}

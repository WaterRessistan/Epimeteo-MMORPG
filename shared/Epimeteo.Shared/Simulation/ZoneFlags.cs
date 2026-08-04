namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Propiedades de una región del mapa (<c>docs/00-arquitectura.md §6</c>). El PvP es una propiedad
/// de <b>región</b>, no de mapa: un mismo mapa puede tener un claro seguro dentro de un bosque
/// hostil.
/// <para>
/// Un punto que no cae en ninguna región tiene <see cref="None"/>: ni seguro ni PvP. Por defecto
/// no se puede atacar, que es el fallo seguro.
/// </para>
/// </summary>
[Flags]
public enum ZoneFlags : uint
{
    /// <summary>Sin propiedades declaradas.</summary>
    None = 0,

    /// <summary>Zona segura: no se puede atacar a jugadores.</summary>
    Safe = 1 << 0,

    /// <summary>PvP activo. Exige que atacante y víctima estén los dos en región con este flag.</summary>
    Pvp = 1 << 1,

    /// <summary>No aparecen monstruos (Fase 9).</summary>
    NoMonsters = 1 << 2,

    /// <summary>Exterior: le afecta el ciclo día/noche y el clima.</summary>
    Outdoor = 1 << 3,

    /// <summary>Interior: iluminación propia.</summary>
    Indoor = 1 << 4,
}

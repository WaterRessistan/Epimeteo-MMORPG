namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Orientación del personaje. Los valores coinciden con el comentario de <c>characters.facing</c>
/// en <c>db/migrations/0001_init.sql</c>: no se reordenan sin migrar la columna.
/// </summary>
public enum Facing : byte
{
    /// <summary>Norte (hacia arriba en pantalla).</summary>
    North = 0,

    /// <summary>Este.</summary>
    East = 1,

    /// <summary>Sur (hacia abajo en pantalla). Orientación por defecto al crear un personaje.</summary>
    South = 2,

    /// <summary>Oeste.</summary>
    West = 3,
}

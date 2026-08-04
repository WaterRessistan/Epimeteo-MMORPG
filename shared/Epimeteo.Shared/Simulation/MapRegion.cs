namespace Epimeteo.Shared.Simulation;

/// <summary>Una región rectangular del mapa con sus propiedades. La resuelve <see cref="RegionSet"/>.</summary>
/// <param name="Name">Nombre corto, el que se manda en <c>ZoneFlagsUpdate</c>.</param>
/// <param name="X">Tile izquierdo.</param>
/// <param name="Y">Tile superior.</param>
/// <param name="Width">Ancho en tiles.</param>
/// <param name="Height">Alto en tiles.</param>
/// <param name="Flags">Propiedades de la región.</param>
public readonly record struct MapRegion(string Name, int X, int Y, int Width, int Height, ZoneFlags Flags)
{
    /// <summary>Región vacía sin flags: lo que devuelve una posición que no cae en ninguna.</summary>
    public static MapRegion None => new(string.Empty, 0, 0, 0, 0, ZoneFlags.None);

    /// <summary>Verdadero si el tile cae dentro del rectángulo.</summary>
    public bool Contains(int tileX, int tileY) =>
        tileX >= X && tileX < X + Width &&
        tileY >= Y && tileY < Y + Height;
}

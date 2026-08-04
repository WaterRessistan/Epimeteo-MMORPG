namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Geometría del área de interés: el mapa partido en celdas de
/// <see cref="SimulationConstants.AoiCellTiles"/> tiles de lado. Cada jugador está suscrito a su
/// celda y a las 8 vecinas (<c>docs/00 § Área de interés</c>).
/// <para>
/// Aquí sólo vive la aritmética —qué celda es un punto, qué vecinas tiene—, sin entidades ni red:
/// así se puede probar entera sin levantar un servidor. El sistema que manda
/// <c>EntitySpawn</c>/<c>EntityDespawn</c> es <c>Server/World/AoiSystem</c>.
/// </para>
/// </summary>
public sealed class AoiGrid
{
    /// <summary>Celdas devueltas por <see cref="GetNeighborhood"/> como mucho (3×3).</summary>
    public const int MaxNeighborhood = 9;

    /// <param name="mapWidth">Ancho del mapa en tiles.</param>
    /// <param name="mapHeight">Alto del mapa en tiles.</param>
    public AoiGrid(int mapWidth, int mapHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mapWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(mapHeight, 1);

        // Redondeo hacia arriba: un mapa de 20 tiles necesita 2 celdas de 16, no 1.
        CellsX = ((mapWidth - 1) / SimulationConstants.AoiCellTiles) + 1;
        CellsY = ((mapHeight - 1) / SimulationConstants.AoiCellTiles) + 1;
    }

    /// <summary>Columnas de celdas.</summary>
    public int CellsX { get; }

    /// <summary>Filas de celdas.</summary>
    public int CellsY { get; }

    /// <summary>Número total de celdas.</summary>
    public int CellCount => CellsX * CellsY;

    /// <summary>Índice de celda de una posición del mundo. Las posiciones fuera se recortan al borde.</summary>
    public int CellOf(Vec2 position)
    {
        var tile = position.ToTile();
        var cx = Math.Clamp(tile.X / SimulationConstants.AoiCellTiles, 0, CellsX - 1);
        var cy = Math.Clamp(tile.Y / SimulationConstants.AoiCellTiles, 0, CellsY - 1);
        return (cy * CellsX) + cx;
    }

    /// <summary>
    /// Escribe en <paramref name="destination"/> la celda dada y sus vecinas existentes y devuelve
    /// cuántas hay. En una esquina del mapa son 4; en un borde, 6; en el interior, 9.
    /// </summary>
    public int GetNeighborhood(int cell, Span<int> destination)
    {
        if (destination.Length < MaxNeighborhood)
        {
            throw new ArgumentException(
                $"El destino necesita {MaxNeighborhood} huecos.", nameof(destination));
        }

        var cx = cell % CellsX;
        var cy = cell / CellsX;
        var count = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            var ny = cy + dy;
            if (ny < 0 || ny >= CellsY)
            {
                continue;
            }

            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = cx + dx;
                if (nx < 0 || nx >= CellsX)
                {
                    continue;
                }

                destination[count++] = (ny * CellsX) + nx;
            }
        }

        return count;
    }
}

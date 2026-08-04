namespace Epimeteo.Server.World;

/// <summary>
/// Qué entidades hay en cada celda de AOI. Es el índice espacial del mundo: sin él, saber quién ve
/// a quién sería comparar todos contra todos, y eso es la diferencia entre aguantar 20 jugadores y
/// aguantar 500 (<c>docs/00 § Área de interés</c>).
/// </summary>
public sealed class CellGrid
{
    private static readonly HashSet<int> Empty = [];

    private readonly HashSet<int>[] _cells;

    /// <param name="cellCount">Número de celdas del mapa (<c>AoiGrid.CellCount</c>).</param>
    public CellGrid(int cellCount)
    {
        _cells = new HashSet<int>[cellCount];
        for (var i = 0; i < cellCount; i++)
        {
            _cells[i] = [];
        }
    }

    /// <summary>Mete una entidad en una celda.</summary>
    public void Add(int entityId, int cell) => _cells[cell].Add(entityId);

    /// <summary>Saca una entidad de una celda.</summary>
    public void Remove(int entityId, int cell) => _cells[cell].Remove(entityId);

    /// <summary>Mueve una entidad entre celdas. No hace nada si es la misma.</summary>
    public void Move(int entityId, int from, int to)
    {
        if (from == to)
        {
            return;
        }

        _cells[from].Remove(entityId);
        _cells[to].Add(entityId);
    }

    /// <summary>Entidades de una celda. Devuelve un conjunto vacío si la celda no existe.</summary>
    public IReadOnlySet<int> Occupants(int cell) =>
        (uint)cell < (uint)_cells.Length ? _cells[cell] : Empty;
}

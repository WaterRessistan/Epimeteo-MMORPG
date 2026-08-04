namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Las regiones de un mapa, resueltas por posición. La usa el servidor para decidir el PvP con
/// posiciones autoritativas (<c>docs/00 §6</c>) y el cliente sólo para pintar el aviso de zona
/// hostil: el cliente <b>nunca</b> decide nada con esto.
/// </summary>
public sealed class RegionSet
{
    private readonly MapRegion[] _regions;

    /// <param name="regions">Regiones en el orden en que aparecen en el JSON del mapa.</param>
    public RegionSet(MapRegion[] regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        _regions = regions;
    }

    /// <summary>Conjunto vacío: todo el mapa sin flags.</summary>
    public static RegionSet Empty { get; } = new([]);

    /// <summary>Regiones declaradas, en orden.</summary>
    public IReadOnlyList<MapRegion> Regions => _regions;

    /// <summary>
    /// Región que contiene el punto. Si se solapan, <b>gana la primera del array</b>: permite
    /// declarar un claro seguro antes del bosque hostil que lo rodea sin recortar rectángulos.
    /// Un punto sin región devuelve nombre vacío y <see cref="ZoneFlags.None"/>.
    /// </summary>
    public MapRegion Resolve(Vec2 position)
    {
        var tile = position.ToTile();

        foreach (var region in _regions)
        {
            if (region.Contains(tile.X, tile.Y))
            {
                return region;
            }
        }

        return MapRegion.None;
    }
}

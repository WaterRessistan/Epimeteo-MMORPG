using System.Diagnostics.CodeAnalysis;
using Epimeteo.Shared.Data;

namespace Epimeteo.Server.Content;

/// <summary>
/// Carga <c>content/maps/*.json</c> al arrancar, igual que <see cref="ClassCatalog"/> con las
/// clases. Un mapa mal formado tira el arranque: arrancar con la colisión rota significaría
/// jugadores atravesando paredes y posiciones guardadas dentro de un muro (FASE-04 §8).
/// </summary>
public sealed class MapCatalog
{
    private readonly IReadOnlyDictionary<string, GameMap> _byKey;

    public MapCatalog(string contentRoot)
    {
        var mapsDir = Path.Combine(contentRoot, "maps");
        if (!Directory.Exists(mapsDir))
        {
            throw new InvalidOperationException($"No existe el directorio de mapas: {mapsDir}");
        }

        var byKey = new Dictionary<string, GameMap>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(mapsDir, "*.json"))
        {
            var map = MapLoader.Load(file);
            if (!byKey.TryAdd(map.Key, map))
            {
                throw new InvalidOperationException($"{file}: la clave de mapa '{map.Key}' está repetida.");
            }
        }

        if (byKey.Count == 0)
        {
            throw new InvalidOperationException($"No hay ningún mapa en {mapsDir}.");
        }

        _byKey = byKey;
    }

    public IReadOnlyCollection<GameMap> All => (IReadOnlyCollection<GameMap>)_byKey.Values;

    public bool TryGet(string mapKey, [MaybeNullWhen(false)] out GameMap map) => _byKey.TryGetValue(mapKey, out map);
}

using System.Diagnostics.CodeAnalysis;

namespace Epimeteo.Shared.Data;

/// <summary>
/// El registro ya cargado, para consultar por clave (FASE-12 §2 D1). Si el fichero no existe —hoy
/// siempre, sin arte real— el registro se queda vacío en vez de fallar: a diferencia de los demás
/// catálogos, que sí paran el arranque si falta contenido de juego (CLAUDE.md §4), este es
/// puramente estético y opcional (D3: sin entrada, el render cae al placeholder de siempre).
/// </summary>
public sealed class AtlasRegistry
{
    private readonly IReadOnlyDictionary<string, AtlasRegion> _byKey;

    public AtlasRegistry(string manifestPath)
    {
        _byKey = File.Exists(manifestPath)
            ? AtlasRegistryLoader.Load(manifestPath)
            : new Dictionary<string, AtlasRegion>(StringComparer.Ordinal);
    }

    /// <summary>Construye el registro ya a partir de entradas conocidas (para el cliente Godot, que no puede usar <see cref="File"/> sobre <c>res://</c>).</summary>
    public AtlasRegistry(IReadOnlyDictionary<string, AtlasRegion> entries) => _byKey = entries;

    public int Count => _byKey.Count;

    public bool TryGet(string key, [MaybeNullWhen(false)] out AtlasRegion region) => _byKey.TryGetValue(key, out region);
}

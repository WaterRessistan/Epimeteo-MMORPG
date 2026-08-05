using System.Diagnostics.CodeAnalysis;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Carga <c>content/shops/*.json</c> una vez al arrancar, a memoria (mismo patrón que
/// <c>ItemCatalog</c>/<c>MapCatalog</c>: falla ruidoso si algo no es válido, CLAUDE.md §4).
/// <para>
/// Vive en <c>Shared</c> —no en <c>Server/Content</c>— porque el cliente necesita el catálogo
/// para pintar <c>ShopData</c> con nombres de verdad, no sólo claves (igual que
/// <c>ItemCatalog</c>, FASE-06 §4).
/// </para>
/// </summary>
public sealed class ShopCatalog
{
    private readonly IReadOnlyDictionary<string, ShopDefinition> _byKey;

    public ShopCatalog(string contentRoot)
    {
        var shopsDir = Path.Combine(contentRoot, "shops");
        if (!Directory.Exists(shopsDir))
        {
            throw new InvalidOperationException($"No existe el directorio de tiendas: {shopsDir}");
        }

        var byKey = new Dictionary<string, ShopDefinition>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(shopsDir, "*.json"))
        {
            var definition = ShopLoader.Load(file);
            if (!byKey.TryAdd(definition.Key, definition))
            {
                throw new InvalidOperationException($"{file}: clave de tienda duplicada '{definition.Key}'");
            }
        }

        if (byKey.Count == 0)
        {
            throw new InvalidOperationException($"No hay ninguna tienda en {shopsDir}.");
        }

        _byKey = byKey;
    }

    public IReadOnlyCollection<ShopDefinition> All => (IReadOnlyCollection<ShopDefinition>)_byKey.Values;

    public bool TryGet(string shopKey, [MaybeNullWhen(false)] out ShopDefinition definition) =>
        _byKey.TryGetValue(shopKey, out definition);
}

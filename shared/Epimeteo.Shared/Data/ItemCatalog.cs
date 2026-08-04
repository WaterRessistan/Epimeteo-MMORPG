using System.Diagnostics.CodeAnalysis;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Carga <c>content/items/*.json</c> una vez al arrancar, a memoria (mismo patrón que
/// <c>MapCatalog</c>/<c>ClassCatalog</c>: falla ruidoso si algo no es válido — un catálogo a
/// medias es peor que un servidor que no arranca, CLAUDE.md §4).
/// <para>
/// Vive en <c>Shared</c> y no en <c>Server/Content</c> —a diferencia de <c>ClassCatalog</c>—
/// porque el cliente también necesita el catálogo completo: el inventario puede tener cualquier
/// combinación de ítems a la vez, así que la UI necesita poder resolver cualquier clave, no sólo
/// la de un ítem concreto (a diferencia de un mapa, del que el cliente sólo carga el suyo).
/// </para>
/// </summary>
public sealed class ItemCatalog
{
    private readonly IReadOnlyDictionary<string, ItemDefinition> _byKey;

    public ItemCatalog(string contentRoot)
    {
        var itemsDir = Path.Combine(contentRoot, "items");
        if (!Directory.Exists(itemsDir))
        {
            throw new InvalidOperationException($"No existe el directorio de ítems: {itemsDir}");
        }

        var byKey = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(itemsDir, "*.json"))
        {
            var definition = ItemLoader.Load(file);
            if (!byKey.TryAdd(definition.Key, definition))
            {
                throw new InvalidOperationException($"{file}: clave de ítem duplicada '{definition.Key}'");
            }
        }

        if (byKey.Count == 0)
        {
            throw new InvalidOperationException($"No hay ningún ítem en {itemsDir}.");
        }

        _byKey = byKey;
    }

    public IReadOnlyCollection<ItemDefinition> All => (IReadOnlyCollection<ItemDefinition>)_byKey.Values;

    public bool TryGet(string itemKey, [MaybeNullWhen(false)] out ItemDefinition definition) =>
        _byKey.TryGetValue(itemKey, out definition);
}

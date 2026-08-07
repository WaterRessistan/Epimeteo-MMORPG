using System.Diagnostics.CodeAnalysis;

namespace Epimeteo.Server.Content;

/// <summary>
/// Carga <c>content/monsters/*.json</c> una vez al arrancar (como <c>CropCatalog</c>): falla
/// ruidoso si algo no es válido, que es peor de arreglar en producción (CLAUDE.md §4).
/// </summary>
public sealed class MonsterCatalog
{
    private readonly IReadOnlyDictionary<string, MonsterDefinition> _byKey;

    public MonsterCatalog(string contentRoot)
    {
        var dir = Path.Combine(contentRoot, "monsters");
        if (!Directory.Exists(dir))
        {
            throw new InvalidOperationException($"No existe el directorio de monstruos: {dir}");
        }

        var byKey = new Dictionary<string, MonsterDefinition>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var definition = MonsterLoader.Load(file);
            if (!byKey.TryAdd(definition.Key, definition))
            {
                throw new InvalidOperationException($"{file}: clave de monstruo duplicada '{definition.Key}'");
            }
        }

        if (byKey.Count == 0)
        {
            throw new InvalidOperationException($"No hay ningún monstruo en {dir}.");
        }

        _byKey = byKey;
    }

    /// <summary>Constructor de pruebas: monstruos en memoria, sin tocar disco.</summary>
    internal MonsterCatalog(IEnumerable<MonsterDefinition> definitions) =>
        _byKey = definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<MonsterDefinition> All => (IReadOnlyCollection<MonsterDefinition>)_byKey.Values;

    public bool TryGet(string monsterKey, [MaybeNullWhen(false)] out MonsterDefinition definition) =>
        _byKey.TryGetValue(monsterKey, out definition);
}

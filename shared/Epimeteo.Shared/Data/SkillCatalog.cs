using System.Diagnostics.CodeAnalysis;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Carga <c>content/skills/*.json</c> una vez al arrancar (como <c>ItemCatalog</c>/
/// <c>ShopCatalog</c>): falla ruidoso si algo no es válido (CLAUDE.md §4). Vive en <c>Shared</c>
/// porque el cliente también lo carga, para la barra de habilidades (FASE-10 §2 D6).
/// </summary>
public sealed class SkillCatalog
{
    private readonly IReadOnlyDictionary<string, SkillDefinition> _byKey;
    private readonly ILookup<string, SkillDefinition> _byClass;

    public SkillCatalog(string contentRoot)
    {
        var dir = Path.Combine(contentRoot, "skills");
        if (!Directory.Exists(dir))
        {
            throw new InvalidOperationException($"No existe el directorio de habilidades: {dir}");
        }

        var byKey = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var definition = SkillLoader.Load(file);
            if (!byKey.TryAdd(definition.Key, definition))
            {
                throw new InvalidOperationException($"{file}: clave de habilidad duplicada '{definition.Key}'");
            }
        }

        if (byKey.Count == 0)
        {
            throw new InvalidOperationException($"No hay ninguna habilidad en {dir}.");
        }

        _byKey = byKey;
        _byClass = byKey.Values.ToLookup(s => s.ClassKey, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<SkillDefinition> All => (IReadOnlyCollection<SkillDefinition>)_byKey.Values;

    public bool TryGet(string skillKey, [MaybeNullWhen(false)] out SkillDefinition definition) =>
        _byKey.TryGetValue(skillKey, out definition);

    /// <summary>Las habilidades de una clase, en el orden del contenido. Vacío si la clase no tiene ninguna.</summary>
    public IEnumerable<SkillDefinition> ForClass(string classKey) => _byClass[classKey];
}

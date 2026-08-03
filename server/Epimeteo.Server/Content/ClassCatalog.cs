using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Epimeteo.Server.Content;

/// <summary>
/// Carga <c>content/classes/*.json</c> una vez al arrancar (como <c>MigrationRunner</c>, antes
/// de <c>app.Run()</c>) a memoria. Falla ruidoso si un fichero no es JSON válido: un catálogo a
/// medias es peor que un servidor que no arranca (CLAUDE.md §4).
/// </summary>
public sealed class ClassCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IReadOnlyDictionary<string, ClassDefinition> _byKey;

    public ClassCatalog(string contentRoot)
    {
        var classesDir = Path.Combine(contentRoot, "classes");
        if (!Directory.Exists(classesDir))
        {
            throw new InvalidOperationException($"No existe el directorio de clases: {classesDir}");
        }

        var byKey = new Dictionary<string, ClassDefinition>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(classesDir, "*.json"))
        {
            var json = File.ReadAllText(file);
            var definition = JsonSerializer.Deserialize<ClassDefinition>(json, JsonOptions)
                ?? throw new InvalidOperationException($"{file}: JSON vacío o inválido");

            byKey[definition.Key] = definition;
        }

        _byKey = byKey;
    }

    public IReadOnlyCollection<ClassDefinition> All => (IReadOnlyCollection<ClassDefinition>)_byKey.Values;

    public bool TryGet(string classKey, [MaybeNullWhen(false)] out ClassDefinition definition) =>
        _byKey.TryGetValue(classKey, out definition);
}

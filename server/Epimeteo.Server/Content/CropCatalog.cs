using System.Diagnostics.CodeAnalysis;

namespace Epimeteo.Server.Content;

/// <summary>
/// Carga <c>content/crops/*.json</c> una vez al arrancar, a memoria (mismo patrón que
/// <c>ClassCatalog</c>/<c>MapCatalog</c>: falla ruidoso si algo no es válido, CLAUDE.md §4).
/// </summary>
public sealed class CropCatalog
{
    private readonly IReadOnlyDictionary<string, CropDefinition> _byKey;
    private readonly IReadOnlyDictionary<string, CropDefinition> _bySeedDefKey;

    public CropCatalog(string contentRoot)
    {
        var cropsDir = Path.Combine(contentRoot, "crops");
        if (!Directory.Exists(cropsDir))
        {
            throw new InvalidOperationException($"No existe el directorio de cultivos: {cropsDir}");
        }

        var byKey = new Dictionary<string, CropDefinition>(StringComparer.Ordinal);
        var bySeed = new Dictionary<string, CropDefinition>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(cropsDir, "*.json"))
        {
            var definition = CropLoader.Load(file);
            if (!byKey.TryAdd(definition.Key, definition))
            {
                throw new InvalidOperationException($"{file}: clave de cultivo duplicada '{definition.Key}'");
            }

            if (!bySeed.TryAdd(definition.SeedDefKey, definition))
            {
                throw new InvalidOperationException(
                    $"{file}: la semilla '{definition.SeedDefKey}' ya la usa otro cultivo.");
            }
        }

        if (byKey.Count == 0)
        {
            throw new InvalidOperationException($"No hay ningún cultivo en {cropsDir}.");
        }

        _byKey = byKey;
        _bySeedDefKey = bySeed;
    }

    /// <summary>Constructor de pruebas: cultivos en memoria, sin tocar disco (mismo motivo que <c>ItemLoader.Parse</c> — probar validación/reglas sin depender de <c>content/</c>).</summary>
    internal CropCatalog(IEnumerable<CropDefinition> definitions)
    {
        var byKey = new Dictionary<string, CropDefinition>(StringComparer.Ordinal);
        var bySeed = new Dictionary<string, CropDefinition>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            byKey[definition.Key] = definition;
            bySeed[definition.SeedDefKey] = definition;
        }

        _byKey = byKey;
        _bySeedDefKey = bySeed;
    }

    public IReadOnlyCollection<CropDefinition> All => (IReadOnlyCollection<CropDefinition>)_byKey.Values;

    public bool TryGet(string cropKey, [MaybeNullWhen(false)] out CropDefinition definition) =>
        _byKey.TryGetValue(cropKey, out definition);

    /// <summary>El cultivo que se siembra con una semilla concreta, si la hay.</summary>
    public bool TryGetBySeed(string seedDefKey, [MaybeNullWhen(false)] out CropDefinition definition) =>
        _bySeedDefKey.TryGetValue(seedDefKey, out definition);
}

using System.Security.Cryptography;
using System.Text.Json;
using Epimeteo.ReleaseTool;

// Genera client-build/manifest.json a partir de todo lo que haya en el directorio dado
// (FASE-15 §2 D2). Uso:
//   dotnet run --project tools/Epimeteo.ReleaseTool -- client-build
if (args.Length != 1)
{
    Console.Error.WriteLine("Uso: dotnet run --project tools/Epimeteo.ReleaseTool -- <directorio>");
    return 1;
}

var root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"No existe el directorio: {root}");
    return 1;
}

var entries = new List<ManifestEntry>();

foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(root, filePath).Replace('\\', '/');

    // El propio manifiesto no se incluye a sí mismo: describir su propio hash sería circular
    // (el hash cambiaría cada vez que se recalculase, así que nunca podría ser correcto).
    if (relative == Manifest.FileName)
    {
        continue;
    }

    await using var stream = File.OpenRead(filePath);
    var hashBytes = await SHA256.HashDataAsync(stream);
    var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

    entries.Add(new ManifestEntry
    {
        Path = relative,
        Sha256 = hash,
        Size = stream.Length,
    });
}

// Orden determinista: dos corridas sobre la misma build producen el mismo manifiesto byte a
// byte, así que un `git diff` (si algún día se versionara) o una comparación manual tiene sentido.
entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

var manifest = new Manifest
{
    GeneratedAtUtc = DateTime.UtcNow,
    Files = entries,
};

var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(root, Manifest.FileName), json);

Console.WriteLine($"{Manifest.FileName}: {entries.Count} fichero(s), {entries.Sum(e => e.Size)} bytes en total.");
return 0;

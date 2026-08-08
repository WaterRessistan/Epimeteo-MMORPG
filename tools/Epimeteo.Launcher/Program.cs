using System.Security.Cryptography;
using System.Text.Json;
using Epimeteo.ReleaseTool;

namespace Epimeteo.Launcher;

/// <summary>
/// Parcheador de consola contra <c>/files/</c> (FASE-15 §2 D4): descarga lo que falte o haya
/// cambiado según <c>manifest.json</c>, y borra del directorio local lo que ya no está en el
/// manifiesto. Sin ventana propia porque no hay entorno gráfico donde dársela — la misma lógica
/// vale detrás de una UI el día que exista.
/// <code>
/// dotnet run --project tools/Epimeteo.Launcher -- --dir &lt;destino&gt; [--url http://127.0.0.1:5101]
/// </code>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var url = (Arg(args, "--url") ?? "http://127.0.0.1:5101").TrimEnd('/');
        var dir = Arg(args, "--dir");
        if (dir is null)
        {
            Console.Error.WriteLine("Uso: dotnet run --project tools/Epimeteo.Launcher -- --dir <destino> [--url http://127.0.0.1:5101]");
            return 1;
        }

        Directory.CreateDirectory(dir);
        var root = Path.GetFullPath(dir);

        using var http = new HttpClient();

        Manifest manifest;
        await using (var manifestStream = await http.GetStreamAsync($"{url}/files/{Manifest.FileName}"))
        {
            manifest = await JsonSerializer.DeserializeAsync<Manifest>(manifestStream)
                ?? throw new InvalidOperationException("El manifiesto llegó vacío.");
        }

        Console.WriteLine($"Manifiesto de {url}: {manifest.Files.Count} fichero(s), generado {manifest.GeneratedAtUtc:u}.");

        var downloaded = 0;
        var upToDate = 0;
        var failed = 0;
        var manifestPaths = new HashSet<string>(manifest.Files.Select(f => f.Path), StringComparer.Ordinal);

        foreach (var entry in manifest.Files)
        {
            var localPath = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(localPath) && await Sha256Of(localPath) == entry.Sha256)
            {
                upToDate++;
                continue;
            }

            var tempPath = localPath + ".download";
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            try
            {
                await using (var responseStream = await http.GetStreamAsync($"{url}/files/{entry.Path}"))
                await using (var fileStream = File.Create(tempPath))
                {
                    await responseStream.CopyToAsync(fileStream);
                }

                var actualHash = await Sha256Of(tempPath);
                if (!string.Equals(actualHash, entry.Sha256, StringComparison.Ordinal))
                {
                    // Corrupción o manipulación a medio camino no deja un fichero "casi bueno" en
                    // su sitio: se descarta el temporal y el fichero final (si existía de una
                    // versión anterior) se queda tal cual estaba, no a medias.
                    File.Delete(tempPath);
                    Console.Error.WriteLine($"  [FALLO] {entry.Path}: hash esperado {entry.Sha256}, descargado {actualHash}.");
                    failed++;
                    continue;
                }

                File.Move(tempPath, localPath, overwrite: true);
                Console.WriteLine($"  [OK] {entry.Path} ({entry.Size} bytes)");
                downloaded++;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                Console.Error.WriteLine($"  [FALLO] {entry.Path}: {ex.Message}");
                failed++;
            }
        }

        var deleted = 0;
        foreach (var localFile in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, localFile).Replace('\\', '/');
            if (manifestPaths.Contains(relative))
            {
                continue;
            }

            File.Delete(localFile);
            Console.WriteLine($"  [BORRADO] {relative} (ya no está en el manifiesto)");
            deleted++;
        }

        Console.WriteLine($"\n{downloaded} descargado(s), {upToDate} al día, {deleted} borrado(s), {failed} fallo(s).");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<string> Sha256Of(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? Arg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

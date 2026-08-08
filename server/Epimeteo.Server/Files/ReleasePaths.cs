namespace Epimeteo.Server.Files;

/// <summary>
/// Localiza <c>client-build/</c>, la build del cliente que sirve <c>/files/</c> (FASE-15 §2 D1).
/// Misma estrategia que <see cref="Epimeteo.Server.Content.ContentPaths"/>: primero una carpeta
/// junto al ejecutable (lo que deja <c>deploy/publish.sh</c> en producción), y si no está, subir
/// desde <see cref="AppContext.BaseDirectory"/> hasta <c>Epimeteo.sln</c> — así <c>dotnet run</c>
/// y <c>dotnet test</c> funcionan sin configurar nada.
/// </summary>
public static class ReleasePaths
{
    public static string ResolveReleaseRoot()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "client-build");
        if (Directory.Exists(packaged))
        {
            return packaged;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Epimeteo.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"No se encontró client-build/ junto al ejecutable ni Epimeteo.sln subiendo desde " +
                $"{AppContext.BaseDirectory}; no se puede localizar client-build/.");
        }

        return Path.Combine(dir.FullName, "client-build");
    }
}

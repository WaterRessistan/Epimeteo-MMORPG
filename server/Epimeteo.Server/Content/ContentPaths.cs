namespace Epimeteo.Server.Content;

/// <summary>
/// Localiza <c>content/</c> subiendo desde <see cref="AppContext.BaseDirectory"/> hasta
/// encontrar el directorio que contiene <c>Epimeteo.sln</c> (mismo truco que
/// <c>tests/Epimeteo.Server.Tests/PostgresFactAttribute.cs</c> usa para el repo root). Funciona
/// sin configuración para <c>dotnet run</c> y <c>dotnet test</c>.
/// <para>
/// No sirve tal cual para un <c>dotnet publish</c> de un solo fichero: la Fase 5 tendrá que
/// decidir cómo se despliega <c>content/</c> junto al ejecutable (FASE-03-personajes.md §2).
/// </para>
/// </summary>
public static class ContentPaths
{
    public static string ResolveContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Epimeteo.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"No se encontró Epimeteo.sln subiendo desde {AppContext.BaseDirectory}; " +
                "no se puede localizar content/.");
        }

        return Path.Combine(dir.FullName, "content");
    }
}

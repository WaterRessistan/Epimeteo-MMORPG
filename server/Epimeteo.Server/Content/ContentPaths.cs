namespace Epimeteo.Server.Content;

/// <summary>
/// Localiza <c>content/</c>. Dos estrategias, en orden:
/// <list type="number">
/// <item>Una carpeta <c>content/</c> junto al ejecutable. Es lo que deja
/// <c>deploy/publish.sh</c> (Fase 5) al lado de <c>/opt/epimeteo/app</c>: el publish no lleva el
/// repositorio entero, así que en producción el contenido tiene que estar ahí.</item>
/// <item>Si no está, subir desde <see cref="AppContext.BaseDirectory"/> hasta encontrar
/// <c>Epimeteo.sln</c> (mismo truco que
/// <c>tests/Epimeteo.Server.Tests/PostgresFactAttribute.cs</c> usa para el repo root). Es lo que
/// hace funcionar <c>dotnet run</c> y <c>dotnet test</c> sin configuración, sin tocar nada.</item>
/// </list>
/// La misma idea que <c>ClientContent</c> en el cliente Godot (Fase 4): probar primero el
/// contenido empaquetado junto al binario, y sólo si no está, el del repositorio.
/// </summary>
public static class ContentPaths
{
    public static string ResolveContentRoot()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "content");
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
                $"No se encontró content/ junto al ejecutable ni Epimeteo.sln subiendo desde " +
                $"{AppContext.BaseDirectory}; no se puede localizar content/.");
        }

        return Path.Combine(dir.FullName, "content");
    }
}

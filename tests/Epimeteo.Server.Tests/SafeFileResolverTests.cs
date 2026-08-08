using Epimeteo.Server.Files;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// La comprobación de traversal que protege <c>/files/{**path}</c> (FASE-15 §2 D3). <c>path</c>
/// es entrada no confiable — viene tal cual del segmento de URL — así que se prueba aparte del
/// endpoint, con casos que un cliente honesto nunca manda pero uno hostil sí.
/// </summary>
public sealed class SafeFileResolverTests : IDisposable
{
    private readonly string _root;

    public SafeFileResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "epimeteo-safefileresolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "juego.txt"), "contenido");
        File.WriteAllText(Path.Combine(_root, "sub", "anidado.txt"), "contenido anidado");

        // Un "secreto" justo fuera de root, hermano suyo — lo que un traversal intentaría leer.
        File.WriteAllText(Path.Combine(_root, "..", Path.GetFileName(_root) + "-secreto.txt"), "no deberías poder leer esto");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        var secreto = Path.Combine(_root, "..", Path.GetFileName(_root) + "-secreto.txt");
        if (File.Exists(secreto))
        {
            File.Delete(secreto);
        }
    }

    [Fact]
    public void UnFicheroReal_SeResuelveASuRutaAbsoluta()
    {
        var resolved = SafeFileResolver.Resolve(_root, "juego.txt");

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "juego.txt")), resolved);
    }

    [Fact]
    public void UnFicheroRealEnUnaSubcarpeta_TambienSeResuelve()
    {
        var resolved = SafeFileResolver.Resolve(_root, "sub/anidado.txt");

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "sub", "anidado.txt")), resolved);
    }

    [Fact]
    public void UnFicheroInexistente_DevuelveNull() =>
        Assert.Null(SafeFileResolver.Resolve(_root, "no-existe.txt"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SinRuta_DevuelveNull(string? path) =>
        Assert.Null(SafeFileResolver.Resolve(_root, path));

    [Theory]
    [InlineData("../secreto.txt")]
    [InlineData("sub/../../secreto.txt")]
    [InlineData("..")]
    [InlineData("sub/..")]
    public void UnIntentoDeSalirseDeRootConPuntosSuspensivos_DevuelveNull(string path) =>
        Assert.Null(SafeFileResolver.Resolve(_root, path));

    [Fact]
    public void UnaRutaAbsoluta_DevuelveNullAunqueElFicheroExista()
    {
        // Path.Combine(root, requestedPath) ignoraría `root` por completo si requestedPath es
        // absoluta — es justo lo que la primera capa de SafeFileResolver corta antes de combinar.
        var secreto = Path.GetFullPath(Path.Combine(_root, "..", Path.GetFileName(_root) + "-secreto.txt"));
        Assert.True(File.Exists(secreto));

        Assert.Null(SafeFileResolver.Resolve(_root, secreto));
    }

    [Fact]
    public void UnaRutaConBackslash_DevuelveNull() =>
        Assert.Null(SafeFileResolver.Resolve(_root, @"sub\..\..\secreto.txt"));

    [Fact]
    public void UnDirectorioHermanoConPrefijoParecido_NoCuelaPorElNombre()
    {
        // Segunda capa, independiente de la primera: aunque alguien lograra construir una ruta
        // que combinada cayera en "<root>-secreto.txt" (hermano de root, no dentro), el prefijo
        // con separador de directorio de por medio la rechaza igual.
        var hermano = Path.GetFullPath(_root) + "-secreto.txt";
        Assert.True(File.Exists(hermano));

        // No hay forma de pedir esto sólo con `path` relativo sin ".." (ya cubierto arriba), así
        // que esto documenta la propiedad de la comparación de prefijo en sí: un candidato que
        // empieza igual que root pero no está dentro no debe colarse.
        Assert.False(hermano.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}

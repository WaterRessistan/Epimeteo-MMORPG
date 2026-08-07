using System.IO;
using Epimeteo.Shared.Data;
using Godot;

namespace Epimeteo.Client.World;

/// <summary>
/// Localiza <c>content/</c> desde el cliente. Los datos de juego viven fuera del proyecto de Godot
/// (CLAUDE.md §3: son la fuente de verdad compartida con el servidor, versionada en git), así que
/// hay que ir a buscarlos.
/// <para>
/// Se prueba primero <c>res://content/</c> —lo que existirá cuando la Fase 5 empaquete el juego— y
/// si no está, se sube desde la carpeta del proyecto hasta encontrar <c>Epimeteo.sln</c>, que es lo
/// que funciona con el editor abierto sobre el repositorio. Si el mapa que se carga aquí no
/// coincide con el del servidor, el <c>MapHash</c> de <c>WorldEnter</c> lo caza (FASE-04 §2 D4);
/// esto sólo tiene que encontrar el fichero.
/// </para>
/// </summary>
public static class ClientContent
{
    /// <summary>
    /// Raíz de <c>content/</c>, para catálogos que el cliente carga enteros (<c>SkillCatalog</c>,
    /// Fase 10 — la barra de habilidades necesita saber nivel/maná/cooldown de cada una). Mismo
    /// criterio de búsqueda que <see cref="FindMapPath"/>: primero empaquetado, si no, subiendo
    /// hasta <c>Epimeteo.sln</c>.
    /// </summary>
    public static string? ResolveContentRoot()
    {
        var packaged = ProjectSettings.GlobalizePath("res://content");
        return Directory.Exists(packaged) ? packaged : FindRepositoryContentRoot();
    }

    /// <summary>Ruta absoluta del JSON de un mapa, o <c>null</c> si no se encuentra.</summary>
    public static string? FindMapPath(string mapKey)
    {
        var packaged = ProjectSettings.GlobalizePath($"res://content/maps/{mapKey}.json");
        if (File.Exists(packaged))
        {
            return packaged;
        }

        var root = FindRepositoryContentRoot();
        if (root is null)
        {
            return null;
        }

        var path = Path.Combine(root, "maps", $"{mapKey}.json");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Carga y valida un mapa. Devuelve <c>null</c> y deja un error en el log si no se puede:
    /// quedarse sin mapa no es una excepción de programación, es contenido que falta.
    /// </summary>
    public static GameMap? LoadMap(string mapKey)
    {
        var path = FindMapPath(mapKey);
        if (path is null)
        {
            GD.PushError($"No se encontró el mapa '{mapKey}' ni en res://content/maps ni en el repositorio.");
            return null;
        }

        try
        {
            return MapLoader.Load(path);
        }
        catch (IOException ex)
        {
            GD.PushError($"No se pudo leer el mapa '{mapKey}' de {path}: {ex.Message}");
            return null;
        }
        catch (InvalidDataException ex)
        {
            GD.PushError($"El mapa '{mapKey}' de {path} no es válido: {ex.Message}");
            return null;
        }
    }

    private static string? FindRepositoryContentRoot()
    {
        var directory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Epimeteo.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        var content = Path.Combine(directory.FullName, "content");
        return Directory.Exists(content) ? content : null;
    }
}

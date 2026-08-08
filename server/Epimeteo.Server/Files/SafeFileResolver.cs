namespace Epimeteo.Server.Files;

/// <summary>
/// Resuelve la ruta que pide <c>/files/{**path}</c> contra el directorio de la build, rechazando
/// cualquier intento de salirse de él (FASE-15 §2 D3). <c>path</c> es entrada no confiable: viene
/// tal cual del segmento de URL, y el servidor la valida — no confía en que el cliente pida rutas
/// razonables (CLAUDE.md, reglas de seguridad no negociables).
/// </summary>
public static class SafeFileResolver
{
    /// <summary>La ruta absoluta del fichero si es válida y existe; si no, <c>null</c>.</summary>
    public static string? Resolve(string root, string? requestedPath)
    {
        if (string.IsNullOrEmpty(requestedPath))
        {
            return null;
        }

        // Primera capa: rechazar antes de combinar nada. ".." en cualquier segmento, o una ruta
        // absoluta —que haría que Path.Combine descartara `root` entero y devolviera el segundo
        // argumento tal cual, comportamiento documentado de .NET— son las dos formas de salirse
        // de root. Ninguna build legítima necesita ninguna.
        if (requestedPath.Contains("..", StringComparison.Ordinal)
            || requestedPath.Contains('\\')
            || Path.IsPathRooted(requestedPath))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, requestedPath));

        // Segunda capa, independiente de la primera: el resultado tiene que quedar estrictamente
        // dentro de root. El separador al final evita que "client-build-evil" cuele por empezar
        // igual que "client-build".
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, StringComparison.Ordinal) || !File.Exists(candidate))
        {
            return null;
        }

        return candidate;
    }
}

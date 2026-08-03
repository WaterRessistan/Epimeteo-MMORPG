using Microsoft.Extensions.Configuration;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Resuelve la cadena de conexión de test leyendo el mismo
/// <c>server/Epimeteo.Server/appsettings.Development.json</c> (fuera de git) que usa el servidor
/// en local — así basta con haber configurado Postgres una vez para que estos tests corran.
/// <see cref="EnvOverride"/> permite forzar otra cadena (por ejemplo en CI) sin tocar ficheros.
/// </summary>
internal static class TestDatabase
{
    private const string EnvOverride = "EPIMETEO_TEST_CONNECTION";

    public static string? ConnectionString { get; } = Resolve();

    private static string? Resolve()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvOverride);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            return null;
        }

        var appsettingsPath = Path.Combine(repoRoot, "server", "Epimeteo.Server", "appsettings.Development.json");
        if (!File.Exists(appsettingsPath))
        {
            return null;
        }

        var config = new ConfigurationBuilder().AddJsonFile(appsettingsPath).Build();
        var connectionString = config.GetConnectionString("Epimeteo");
        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Epimeteo.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName;
    }
}

/// <summary>
/// Como <see cref="FactAttribute"/>, pero se salta (no falla) si no hay Postgres configurado
/// para tests — ver <see cref="TestDatabase"/>.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
        {
            Skip = "Sin Postgres configurado para tests (appsettings.Development.json o " +
                   "EPIMETEO_TEST_CONNECTION) — ver docs/fases/FASE-02-persistencia.md §2.";
        }
    }
}

using DbUp;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence;

/// <summary>
/// Aplica <c>db/migrations/*.sql</c> (embebidos en este assembly) contra Postgres al arrancar
/// el servidor, antes de <c>app.Run()</c>. Si falla, el servidor no arranca: una migración a
/// medias es peor que un servidor que no contesta (CLAUDE.md §4, "falla ruidoso").
/// </summary>
public static class MigrationRunner
{
    public static void Run(string connectionString)
    {
        var log = Log.ForContext(typeof(MigrationRunner));

        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(MigrationRunner).Assembly, IsMigrationScript)
            .LogTo(new SerilogUpgradeLog(log))
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Fallo aplicando la migración '{result.ErrorScript?.Name}'", result.Error);
        }

        log.Information("{Count} migraciones aplicadas (o ya al día)", result.Scripts.Count());
    }

    private static bool IsMigrationScript(string resourceName) =>
        resourceName.StartsWith("Epimeteo.Server.Persistence.Migrations.", StringComparison.Ordinal)
        && resourceName.EndsWith(".sql", StringComparison.Ordinal);

    private sealed class SerilogUpgradeLog(ILogger log) : DbUp.Engine.Output.IUpgradeLog
    {
        public void LogTrace(string format, params object[] args) => log.Verbose(format, args);
        public void LogDebug(string format, params object[] args) => log.Debug(format, args);
        public void LogInformation(string format, params object[] args) => log.Information(format, args);
        public void LogWarning(string format, params object[] args) => log.Warning(format, args);
        public void LogError(string format, params object[] args) => log.Error(format, args);
        public void LogError(Exception ex, string format, params object[] args) => log.Error(ex, format, args);
    }
}

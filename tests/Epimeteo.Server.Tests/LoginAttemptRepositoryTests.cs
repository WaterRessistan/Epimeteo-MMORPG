using System.Net;
using Dapper;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Accounts;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Contra Postgres real: es la única forma honesta de probar la ventana de tiempo del rate
/// limit persistido (5/minuto por IP, docs/01-protocolo.md). Se salta si no hay Postgres
/// configurado para tests — ver <see cref="PostgresFactAttribute"/>.
/// </summary>
public sealed class LoginAttemptRepositoryTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);

    // TEST-NET-3 (RFC 5737): reservado para documentación, nunca aparece en tráfico real, así
    // que cada test usa un octeto distinto y no choca con otro test ni con 127.0.0.1 en dev.
    private static IPAddress UniqueTestIp() => IPAddress.Parse($"203.0.113.{Random.Shared.Next(2, 255)}");

    [PostgresFact]
    public async Task CountRecentAsync_SinIntentosPrevios_DevuelveCero()
    {
        var repo = new LoginAttemptRepository(_connections);
        var ip = UniqueTestIp();

        var count = await repo.CountRecentAsync(ip, TimeSpan.FromMinutes(1));

        Assert.Equal(0, count);
    }

    [PostgresFact]
    public async Task RecordAsync_LuegoCountRecentAsync_CuentaLosIntentosDentroDeLaVentana()
    {
        var repo = new LoginAttemptRepository(_connections);
        var ip = UniqueTestIp();

        try
        {
            for (var i = 0; i < 3; i++)
            {
                await repo.RecordAsync(ip, $"usuario{i}", success: false);
            }

            var countWithinWindow = await repo.CountRecentAsync(ip, TimeSpan.FromMinutes(1));
            var countOutsideWindow = await repo.CountRecentAsync(ip, TimeSpan.FromMilliseconds(1));

            Assert.Equal(3, countWithinWindow);
            Assert.True(countOutsideWindow <= countWithinWindow);
        }
        finally
        {
            await DeleteAttemptsAsync(ip);
        }
    }

    [PostgresFact]
    public async Task CountRecentAsync_NoMezclaIntentosDeOtraIp()
    {
        var repo = new LoginAttemptRepository(_connections);
        var ip = UniqueTestIp();
        var otherIp = UniqueTestIp();

        try
        {
            await repo.RecordAsync(otherIp, "usuario", success: false);

            var count = await repo.CountRecentAsync(ip, TimeSpan.FromMinutes(1));

            Assert.Equal(0, count);
        }
        finally
        {
            await DeleteAttemptsAsync(otherIp);
        }
    }

    private async Task DeleteAttemptsAsync(IPAddress ip)
    {
        await using var connection = await _connections.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM login_attempts WHERE ip = @ip::inet", new { ip = ip.ToString() });
    }
}

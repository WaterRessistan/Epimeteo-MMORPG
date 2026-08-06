using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Farm;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Contra Postgres real: la fila única de <c>farm_calendar</c> que sembró la migración
/// (FASE-08 §3). No se borra al final —es una fila singleton que siempre tiene que existir—,
/// sólo se deja como estaba.
/// </summary>
public sealed class FarmCalendarRepositoryTests
{
    private readonly NpgsqlConnectionFactory _connections = new(TestDatabase.ConnectionString ?? string.Empty);

    [PostgresFact]
    public async Task GetLastDayIndexAsync_DevuelveLoQueSembroLaMigracion()
    {
        var repo = new FarmCalendarRepository(_connections);

        var value = await repo.GetLastDayIndexAsync();

        Assert.True(value > 0);
    }

    [PostgresFact]
    public async Task SetLastDayIndexAsync_LuegoGetLastDayIndexAsync_DevuelveElValorNuevo()
    {
        var repo = new FarmCalendarRepository(_connections);
        var original = await repo.GetLastDayIndexAsync();

        try
        {
            await repo.SetLastDayIndexAsync(original + 5);

            Assert.Equal(original + 5, await repo.GetLastDayIndexAsync());
        }
        finally
        {
            await repo.SetLastDayIndexAsync(original);
        }
    }
}

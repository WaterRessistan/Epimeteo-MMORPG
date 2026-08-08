using System.Diagnostics;
using Epimeteo.Server.Observability;
using Npgsql;

namespace Epimeteo.Server.Persistence;

/// <summary>
/// Abre conexiones Npgsql a partir de la cadena de <c>ConnectionStrings:Epimeteo</c>. Los
/// repositorios piden una conexión abierta y la cierran con <c>await using</c>; no hay pool
/// propio porque Npgsql ya agrupa conexiones internamente.
/// <para>
/// Es además el único punto por el que pasan las ~11 clases de repositorio, así que es donde se
/// mide la latencia de Postgres (FASE-13 §2 D5): instrumentar aquí no obliga a tocar diez
/// ficheros ni a que un repositorio futuro se acuerde de nada. Lo que mide es <i>abrir</i>
/// conexión, que es lo primero que se degrada cuando la BD sufre o el pool se agota; el tiempo de
/// las consultas en sí queda para <c>pg_stat_statements</c>, que ya lo hace mejor.
/// </para>
/// </summary>
public sealed class NpgsqlConnectionFactory(string connectionString, ServerMetrics? metrics = null)
{
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        // Sin métricas cuando lo construye el arranque para leer stock/granja antes de que exista
        // el contenedor de dependencias (Program.cs), y en los tests de repositorio.
        if (metrics is null)
        {
            var plain = new NpgsqlConnection(connectionString);
            await plain.OpenAsync(ct).ConfigureAwait(false);
            return plain;
        }

        var start = Stopwatch.GetTimestamp();
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        metrics.DatabaseOpenMicros.Observe(
            Stopwatch.GetElapsedTime(start).TotalMicroseconds);

        return connection;
    }
}

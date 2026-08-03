using Npgsql;

namespace Epimeteo.Server.Persistence;

/// <summary>
/// Abre conexiones Npgsql a partir de la cadena de <c>ConnectionStrings:Epimeteo</c>. Los
/// repositorios piden una conexión abierta y la cierran con <c>await using</c>; no hay pool
/// propio porque Npgsql ya agrupa conexiones internamente.
/// </summary>
public sealed class NpgsqlConnectionFactory(string connectionString)
{
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}

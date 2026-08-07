using System.Threading.Channels;
using Epimeteo.Server.World;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence.Characters;

/// <summary>
/// La cola de guardado de personajes: el tick encola structs y este servicio los escribe en
/// Postgres desde su propia tarea. Es la aplicación literal de la regla "nada se escribe en BD
/// dentro del tick" (<c>docs/00 § Persistencia</c>).
/// <para>
/// La cola descarta el guardado <b>más antiguo</b> si se llena: cada elemento es una instantánea
/// completa del personaje, así que uno viejo no vale nada comparado con el siguiente, y bloquear
/// el tick para no perderlo sería mucho peor que perderlo.
/// </para>
/// </summary>
public sealed class CharacterSaver : ICharacterSink, IHostedService
{
    private readonly Channel<CharacterSave> _queue = Channel.CreateBounded<CharacterSave>(
        new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly CharacterRepository _characters;
    private readonly ILogger _log = Log.ForContext<CharacterSaver>();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    public CharacterSaver(CharacterRepository characters) => _characters = characters;

    /// <summary>Guardados pendientes de escribir. Aparece en <c>/status</c>.</summary>
    public int PendingCount => _queue.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(in CharacterSave save) => _queue.Writer.TryWrite(save);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _worker = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cerrar el escritor hace que el bucle termine de vaciar lo pendiente y salga solo: al
        // apagar sí interesa esperar, es la última oportunidad de no perder posiciones.
        _queue.Writer.TryComplete();

        if (_worker is not null)
        {
            await _worker.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var written = 0;

        try
        {
            await foreach (var save in _queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await _characters
                        .UpdateCharacterStateAsync(
                            save.CharacterId, save.MapKey, save.X, save.Y, (int)save.Facing,
                            save.Gold, save.Hp, save.Mp, save.Xp, save.Level,
                            save.StatStr, save.StatInt, save.StatVit, save.StatDex, save.StatPoints, token)
                        .ConfigureAwait(false);
                    written++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un fallo de BD no puede tumbar el guardado de los demás jugadores.
                    _log.Error(ex, "No se pudo guardar el personaje {CharacterId}", save.CharacterId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado.
        }

        _log.Information("Cola de personajes cerrada tras {Written} guardados", written);
    }
}

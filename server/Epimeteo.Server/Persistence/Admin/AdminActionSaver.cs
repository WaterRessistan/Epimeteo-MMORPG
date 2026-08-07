using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence.Admin;

/// <summary>
/// La cola de acciones de admin: el tick encola y este servicio escribe, mismo patrón que el
/// resto de colas desde la Fase 4. Un <c>Ban</c> hace además el <c>UPDATE accounts</c> real aquí
/// mismo — no hace falta un sink aparte sólo para esa mutación (FASE-11 §2 D7): ya está fuera del
/// tick, que es lo único que CLAUDE.md §4 exige.
/// </summary>
public sealed class AdminActionSaver : IAdminActionSink, IHostedService
{
    private readonly Channel<AdminActionSave> _queue = Channel.CreateBounded<AdminActionSave>(
        new BoundedChannelOptions(256)
        {
            // A diferencia del chat o el combate, esto es auditoría: perder una acción de admin
            // bajo presión no es aceptable, así que se espera en vez de descartar la más vieja.
            // El volumen de comandos de admin es minúsculo comparado con el resto de colas.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly AdminActionRepository _repository;
    private readonly ILogger _log = Log.ForContext<AdminActionSaver>();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    public AdminActionSaver(AdminActionRepository repository) => _repository = repository;

    /// <summary>Escrituras pendientes. Aparece en <c>/status</c>.</summary>
    public int PendingCount => _queue.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(in AdminActionSave save) => _queue.Writer.TryWrite(save);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _worker = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
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
                    await _repository.InsertAsync(save, token).ConfigureAwait(false);

                    if (save.Action == AdminAction.Ban && save.BanHours is { } hours)
                    {
                        await _repository
                            .BanAccountByCharacterAsync(save.TargetCharacterId, hours, save.Reason, token)
                            .ConfigureAwait(false);
                    }

                    written++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Error(ex, "No se pudo registrar la acción de admin {Action} de {AdminName} sobre {TargetName}",
                        save.Action, save.AdminName, save.TargetName);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado.
        }

        _log.Information("Cola de admin cerrada tras {Written} escrituras", written);
    }
}

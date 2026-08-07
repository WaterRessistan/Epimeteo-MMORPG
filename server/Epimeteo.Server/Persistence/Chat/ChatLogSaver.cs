using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence.Chat;

/// <summary>
/// La cola de chat: el tick encola y este servicio escribe, mismo patrón que <c>CombatLogSaver</c>
/// desde la Fase 9. Log append-only: <c>DropOldest</c> en el mismo criterio — perder una línea de
/// chat bajo presión extrema es mejor que bloquear el tick.
/// </summary>
public sealed class ChatLogSaver : IChatLogSink, IHostedService
{
    private readonly Channel<ChatLogSave> _queue = Channel.CreateBounded<ChatLogSave>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly ChatLogRepository _repository;
    private readonly ILogger _log = Log.ForContext<ChatLogSaver>();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    public ChatLogSaver(ChatLogRepository repository) => _repository = repository;

    /// <summary>Escrituras pendientes. Aparece en <c>/status</c>.</summary>
    public int PendingCount => _queue.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(in ChatLogSave save) => _queue.Writer.TryWrite(save);

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
                    written++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Error(ex, "No se pudo registrar la línea de chat del personaje {CharacterId}", save.CharacterId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado.
        }

        _log.Information("Cola de chat cerrada tras {Written} escrituras", written);
    }
}

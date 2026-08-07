using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence.Combat;

/// <summary>
/// La cola de muertes PvP: el tick encola y este servicio escribe, mismo patrón que el resto de
/// colas desde la Fase 4.
/// <para>
/// A diferencia de posición o inventario, cada elemento es una fila <b>independiente</b> de un log
/// append-only, no una instantánea que sustituya a la anterior — igual que <c>EconomySaver</c>.
/// Se mantiene <c>DropOldest</c> con el mismo riesgo residual ya aceptado allí, y aquí con mucho
/// más margen todavía: las muertes PvP son rarísimas comparadas con el guardado de posición.
/// </para>
/// </summary>
public sealed class CombatLogSaver : ICombatLogSink, IHostedService
{
    private readonly Channel<CombatLogSave> _queue = Channel.CreateBounded<CombatLogSave>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly CombatLogRepository _repository;
    private readonly ILogger _log = Log.ForContext<CombatLogSaver>();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    public CombatLogSaver(CombatLogRepository repository) => _repository = repository;

    /// <summary>Escrituras pendientes. Aparece en <c>/status</c>.</summary>
    public int PendingCount => _queue.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(in CombatLogSave save) => _queue.Writer.TryWrite(save);

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
                    _log.Error(ex, "No se pudo registrar la muerte del personaje {VictimId}", save.VictimId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado.
        }

        _log.Information("Cola de combate cerrada tras {Written} escrituras", written);
    }
}

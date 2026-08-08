using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence.Anomalies;

/// <summary>
/// La cola de anomalías: quien detecta encola y este servicio escribe, mismo patrón que las otras
/// seis colas desde la Fase 4.
/// <para>
/// <c>DropOldest</c> (FASE-13 §2 D7): bajo una inundación de anomalías —justo cuando alguien está
/// atacando— perder filas es preferible a que el hilo de red o el tick esperen a Postgres. Y si
/// hay inundación, el patrón ya quedó registrado en las primeras: el valor de esta tabla está en
/// saber <i>que</i> pasó y con qué cuenta, no en tenerlas todas.
/// </para>
/// </summary>
public sealed class AnomalySaver : IAnomalySink, IHostedService
{
    private readonly Channel<AnomalySave> _queue = Channel.CreateBounded<AnomalySave>(
        new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly AnomalyRepository _repository;
    private readonly ILogger _log = Log.ForContext<AnomalySaver>();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    public AnomalySaver(AnomalyRepository repository) => _repository = repository;

    /// <summary>Escrituras pendientes. Aparece en <c>/status</c>.</summary>
    public int PendingCount => _queue.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(in AnomalySave save) => _queue.Writer.TryWrite(save);

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
                    _log.Error(ex, "No se pudo registrar la anomalía {Kind} de la sesión {SessionId}",
                        save.Kind, save.SessionId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado.
        }

        _log.Information("Cola de anomalías cerrada tras {Written} escrituras", written);
    }
}

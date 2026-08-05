using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence.Economy;

/// <summary>
/// La cola de economía: el tick encola una fila por compra/venta/reparación/tirada, y este
/// servicio escribe el log y (si viene de una tienda) el stock, desde su propia tarea — mismo
/// patrón que <c>CharacterPositionSaver</c>/<c>InventorySaver</c> (FASE-07 §2 D1). A diferencia de
/// esas dos, cada elemento de esta cola es una fila **independiente**, no una instantánea que
/// sustituye a la anterior: <c>DropOldest</c> sigue siendo la política, con el mismo riesgo
/// residual ya aceptado en el resto de colas — y aquí con más margen, porque el volumen de
/// acciones económicas es bajísimo comparado con el de posición.
/// </summary>
public sealed class EconomySaver : IEconomySink, IHostedService
{
    private readonly Channel<EconomySave> _queue = Channel.CreateBounded<EconomySave>(
        new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly EconomyLogRepository _log;
    private readonly ShopStockRepository _shopStock;
    private readonly ILogger _logger = Log.ForContext<EconomySaver>();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    public EconomySaver(EconomyLogRepository log, ShopStockRepository shopStock)
    {
        _log = log;
        _shopStock = shopStock;
    }

    /// <summary>Escrituras pendientes. Aparece en <c>/status</c>.</summary>
    public int PendingCount => _queue.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(in EconomySave save) => _queue.Writer.TryWrite(save);

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
                    if (save.CharacterId is not null)
                    {
                        await _log.InsertAsync(save, token).ConfigureAwait(false);
                    }

                    if (save.ShopKey is { } shopKey && save.ShopStock is { } stock && save.ShopStockMax is { } stockMax)
                    {
                        await _shopStock
                            .UpsertAsync(shopKey, save.DefKey, stock, stockMax, save.ShopRestockAt ?? DateTimeOffset.UtcNow, token)
                            .ConfigureAwait(false);
                    }

                    written++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un fallo de BD no puede tumbar el guardado de los demás jugadores.
                    _logger.Error(ex, "No se pudo escribir una fila de economía ({Kind}, personaje {CharacterId})",
                        save.Kind, save.CharacterId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado.
        }

        _logger.Information("Cola de economía cerrada tras {Written} escrituras", written);
    }
}

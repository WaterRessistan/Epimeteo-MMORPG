using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence.Farm;

/// <summary>
/// La cola de guardado de granja: el tick encola una instantánea por tile que cambió (arar,
/// plantar, regar, cosechar o el barrido diario) y este servicio la escribe desde su propia
/// tarea — mismo patrón que <c>CharacterPositionSaver</c>/<c>InventorySaver</c>/<c>EconomySaver</c>
/// (FASE-08 §2 D1: exactamente el mismo escritor único que ya usa todo lo demás, en vez del
/// <c>UPDATE</c> SQL directo que describía <c>docs/00 §7</c>).
/// </summary>
public sealed class FarmTileSaver : IFarmSink, IHostedService
{
    private readonly Channel<FarmTileSave> _queue = Channel.CreateBounded<FarmTileSave>(
        new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly FarmTileRepository _tiles;
    private readonly FarmCalendarRepository _calendar;
    private readonly ILogger _log = Log.ForContext<FarmTileSaver>();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    public FarmTileSaver(FarmTileRepository tiles, FarmCalendarRepository calendar)
    {
        _tiles = tiles;
        _calendar = calendar;
    }

    /// <summary>Guardados pendientes de escribir. Aparece en <c>/status</c>.</summary>
    public int PendingCount => _queue.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(in FarmTileSave save) => _queue.Writer.TryWrite(save);

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
                    if (save.PlotId is not null)
                    {
                        await _tiles.UpsertAsync(save, token).ConfigureAwait(false);
                    }

                    if (save.CalendarDayIndex is { } dayIndex)
                    {
                        await _calendar.SetLastDayIndexAsync(dayIndex, token).ConfigureAwait(false);
                    }

                    written++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un fallo de BD no puede tumbar el guardado de los demás tiles.
                    _log.Error(ex, "No se pudo guardar el tile de granja ({TileX}, {TileY})", save.TileX, save.TileY);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado.
        }

        _log.Information("Cola de granja cerrada tras {Written} guardados", written);
    }
}

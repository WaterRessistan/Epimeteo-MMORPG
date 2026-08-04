using System.Threading.Channels;
using Epimeteo.Server.Inventory;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Persistence.Items;

/// <summary>
/// La cola de guardado de inventarios: el tick encola instantáneas completas y este servicio las
/// aplica a Postgres desde su propia tarea (mismo patrón que <c>CharacterPositionSaver</c>). Como
/// cada instantánea es el estado <b>completo</b> de los contenedores 0–3 de un personaje —no un
/// delta— <c>DropOldest</c> es tan seguro aquí como lo es para la posición: perder una vieja no
/// importa porque la más nueva ya la contiene entera (FASE-06 §2 D2).
/// </summary>
public sealed class InventorySaver : IInventorySink, IHostedService
{
    private readonly Channel<InventorySave> _queue = Channel.CreateBounded<InventorySave>(
        new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly ItemRepository _items;
    private readonly ILogger _log = Log.ForContext<InventorySaver>();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    public InventorySaver(ItemRepository items) => _items = items;

    /// <summary>Guardados pendientes de escribir. Aparece en <c>/status</c>.</summary>
    public int PendingCount => _queue.Reader.Count;

    /// <inheritdoc />
    public void Enqueue(in InventorySave save) => _queue.Writer.TryWrite(save);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _worker = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Igual que en el guardado de posiciones: al apagar sí interesa esperar a vaciar la cola,
        // es la última oportunidad de no perder inventario.
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
                    await _items.ReplaceInventoryAsync(save.CharacterId, save.Items, token).ConfigureAwait(false);
                    written++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un fallo de BD no puede tumbar el guardado de los demás jugadores.
                    _log.Error(ex, "No se pudo guardar el inventario del personaje {CharacterId}", save.CharacterId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado.
        }

        _log.Information("Cola de inventario cerrada tras {Written} guardados", written);
    }
}

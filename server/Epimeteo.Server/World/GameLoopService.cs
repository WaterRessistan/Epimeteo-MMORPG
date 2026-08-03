using Epimeteo.Server.Net;
using Epimeteo.Shared.Net;
using Microsoft.Extensions.Hosting;

namespace Epimeteo.Server.World;

/// <summary>
/// Ata el ciclo de vida del bucle de simulación al del host: arranca con el servidor y, al parar,
/// expulsa a todo el mundo con <see cref="KickReason.ServerShutdown"/> antes de detener el hilo.
/// </summary>
public sealed class GameLoopService : IHostedService
{
    private readonly GameLoop _loop;
    private readonly SessionManager _sessions;

    public GameLoopService(GameLoop loop, SessionManager sessions)
    {
        _loop = loop;
        _sessions = sessions;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loop.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessions.KickAll(KickReason.ServerShutdown);

        // Un respiro para que los frames de Kick salgan por el cable antes de cortar.
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        _loop.Stop();
    }
}

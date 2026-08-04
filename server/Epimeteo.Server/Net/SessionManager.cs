using System.Collections.Concurrent;
using System.Net.WebSockets;
using Epimeteo.Server.World;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Time;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Net;

/// <summary>
/// Registro de sesiones vivas. El barrido de timeouts lo dispara el bucle de tick una vez por
/// segundo: es trabajo periódico que ya tiene un hilo con cadencia fija, no hace falta otro.
/// </summary>
public sealed class SessionManager
{
    private readonly ConcurrentDictionary<int, Session> _sessions = new();
    private readonly ServerOptions _options;
    private readonly SessionMessageHandler _handler;
    private readonly IWorldInbox _worldInbox;
    private readonly ILogger _log = Log.ForContext<SessionManager>();
    private int _nextId;

    public SessionManager(ServerOptions options, SessionMessageHandler handler, IWorldInbox worldInbox)
    {
        _options = options;
        _handler = handler;
        _worldInbox = worldInbox;
    }

    /// <summary>Sesiones conectadas ahora mismo.</summary>
    public int Count => _sessions.Count;

    /// <summary>Crea y registra una sesión para un WebSocket recién aceptado.</summary>
    public Session Create(WebSocket socket, string remoteAddress)
    {
        var id = Interlocked.Increment(ref _nextId);
        var session = new Session(id, socket, remoteAddress, _handler, _options.OutboundQueueCapacity);
        _sessions[id] = session;
        _log.Debug("Sesión {SessionId} aceptada desde {Remote} ({Count} activas)", id, remoteAddress, _sessions.Count);
        return session;
    }

    /// <summary>Da de baja una sesión terminada.</summary>
    public void Remove(Session session)
    {
        if (_sessions.TryRemove(session.Id, out _))
        {
            if (session.JoinedWorld)
            {
                // El mundo saca la entidad y guarda la posición en su propio hilo; aquí no se
                // toca ni el mundo ni la BD.
                session.JoinedWorld = false;
                _worldInbox.PostControl(new PlayerLeaveCommand(session.Id));
            }

            _log.Debug("Sesión {SessionId} cerrada tras {DuracionMs} ms ({Count} activas)",
                session.Id, ServerClock.NowMs - session.ConnectedAtMs, _sessions.Count);
        }
    }

    /// <summary>
    /// Trabajo periódico del bucle de tick. Barre timeouts una vez por segundo; el resto de
    /// ticks retorna inmediatamente.
    /// </summary>
    public void OnTick(long tick)
    {
        if (tick % _options.TickRate != 0)
        {
            return;
        }

        var now = ServerClock.NowMs;

        foreach (var session in _sessions.Values)
        {
            if (session.IsClosing)
            {
                continue;
            }

            // Una conexión que no saluda a tiempo es un escáner de puertos o un cliente roto.
            if (session.State == SessionState.Connecting && now - session.ConnectedAtMs > _options.HelloTimeoutMs)
            {
                _log.Information("Sesión {SessionId} sin Hello en {Ms} ms", session.Id, _options.HelloTimeoutMs);
                session.Kick(KickReason.Timeout);
                continue;
            }

            if (now - session.LastInboundMs > _options.IdleTimeoutMs)
            {
                _log.Information("Sesión {SessionId} inactiva {Ms} ms", session.Id, now - session.LastInboundMs);
                session.Kick(KickReason.Timeout);
            }
        }
    }

    /// <summary>Expulsa a todos con el motivo dado. Se usa al apagar el servidor.</summary>
    public void KickAll(KickReason reason)
    {
        foreach (var session in _sessions.Values)
        {
            session.Kick(reason);
        }
    }
}

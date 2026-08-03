using Epimeteo.Server.Persistence.Accounts;
using Epimeteo.Server.World;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Time;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Net;

/// <summary>
/// Despacha los mensajes ya validados (opcode conocido, estado legal, dentro del rate limit).
/// <para>
/// Los mensajes de sesión —<c>Hello</c> y <c>Ping</c>— se resuelven aquí mismo, en el hilo de red:
/// no tocan estado de mundo, y hacerlos pasar por la cola del tick les añadiría hasta 50 ms que
/// falsearían el RTT. Todo lo que sí toque el mundo irá a <see cref="IWorldInbox"/>.
/// </para>
/// <para>
/// <c>Login</c> y <c>Register</c> también se resuelven aquí (tocan Postgres, no el tick), pero
/// awaited: la sesión no procesa el siguiente frame hasta que la BD responde. No bloquea otras
/// sesiones, que tienen su propio bucle de lectura.
/// </para>
/// </summary>
public sealed class SessionMessageHandler
{
    private readonly ServerOptions _options;
    private readonly IWorldInbox _worldInbox;
    private readonly AuthService _auth;
    private readonly ILogger _log = Log.ForContext<SessionMessageHandler>();

    public SessionMessageHandler(ServerOptions options, IWorldInbox worldInbox, AuthService auth)
    {
        _options = options;
        _worldInbox = worldInbox;
        _auth = auth;
    }

    /// <summary>Procesa un frame entrante. Llamado sólo desde el bucle de lectura de la sesión.</summary>
    public Task HandleAsync(Session session, Opcode opcode, ReadOnlyMemory<byte> frame)
    {
        switch (opcode)
        {
            case Opcode.Hello:
                HandleHello(session, frame);
                return Task.CompletedTask;

            case Opcode.Ping:
                HandlePing(session, frame);
                return Task.CompletedTask;

            case Opcode.Login:
                return HandleLoginAsync(session, frame);

            case Opcode.Register:
                return HandleRegisterAsync(session, frame);

            default:
                HandleUnrouted(session, opcode, frame);
                return Task.CompletedTask;
        }
    }

    private void HandleUnrouted(Session session, Opcode opcode, ReadOnlyMemory<byte> frame)
    {
        // Los opcodes que tocan el mundo cruzan a la cola del tick. Hoy no hay nadie al otro
        // lado (ni forma legal de llegar aquí: todos exigen InWorld), pero el enrutado ya es
        // el definitivo y la Fase 4 sólo tiene que poner el consumidor.
        if (OpcodeTable.TryGet(opcode, out var spec) && IsWorldFamily(spec.Family))
        {
            _worldInbox.Post(session.Id, opcode, FrameCodec.PayloadOf(frame).Span);
            return;
        }

        // El resto —login, registro, personajes— llega en su fase. Hasta entonces es un error
        // de protocolo, no un mensaje que se ignora en silencio.
        _log.Warning("Opcode {Opcode} aún no implementado (sesión {SessionId})", opcode, session.Id);
        session.Kick(KickReason.ProtocolError);
    }

    private static bool IsWorldFamily(OpcodeFamily family) => family
        is OpcodeFamily.Movement
        or OpcodeFamily.Inventory
        or OpcodeFamily.Shop
        or OpcodeFamily.Farm
        or OpcodeFamily.Combat
        or OpcodeFamily.Chat;

    private void HandleHello(Session session, ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecodePayload<C2SHello>(frame, out var hello) || hello is null)
        {
            _log.Warning("Hello ilegible en la sesión {SessionId}", session.Id);
            session.Kick(KickReason.ProtocolError);
            return;
        }

        if (hello.ProtocolVersion != ProtocolVersion.Current)
        {
            _log.Information("Sesión {SessionId} con protocolo {ClientVersion}, esperado {ServerVersion}",
                session.Id, hello.ProtocolVersion, ProtocolVersion.Current);
            session.Kick(KickReason.VersionMismatch, ResultCode.VersionMismatch);
            return;
        }

        session.ClientBuild = Sanitize(hello.ClientBuild);
        session.State = SessionState.Greeted;

        session.Send(Opcode.HelloAck, new S2CHelloAck
        {
            ServerProtocolVersion = ProtocolVersion.Current,
            TickRate = _options.TickRate,
            SnapshotRate = _options.SnapshotRate,
            ServerTimeMs = ServerClock.NowMs,
        });

        _log.Information("Sesión {SessionId} saludada desde {Remote} (build {ClientBuild})",
            session.Id, session.RemoteAddress, session.ClientBuild);
    }

    private static void HandlePing(Session session, ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecodePayload<C2SPing>(frame, out var ping) || ping is null)
        {
            session.Kick(KickReason.ProtocolError);
            return;
        }

        session.Send(Opcode.Pong, new S2CPong
        {
            ClientTimeMs = ping.ClientTimeMs,
            ServerTimeMs = ServerClock.NowMs,
        });
    }

    private async Task HandleLoginAsync(Session session, ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecodePayload<C2SLogin>(frame, out var login) || login is null)
        {
            _log.Warning("Login ilegible en la sesión {SessionId}", session.Id);
            session.Kick(KickReason.ProtocolError);
            return;
        }

        var outcome = await _auth
            .LoginAsync(session.RemoteAddress, login.Username, login.Password)
            .ConfigureAwait(false);

        SendAuthResult(session, outcome);
    }

    private async Task HandleRegisterAsync(Session session, ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecodePayload<C2SRegister>(frame, out var register) || register is null)
        {
            _log.Warning("Register ilegible en la sesión {SessionId}", session.Id);
            session.Kick(KickReason.ProtocolError);
            return;
        }

        var outcome = await _auth
            .RegisterAsync(session.RemoteAddress, register.Username, register.Email, register.Password)
            .ConfigureAwait(false);

        SendAuthResult(session, outcome);
    }

    private void SendAuthResult(Session session, AuthOutcome outcome)
    {
        if (outcome.Ok)
        {
            session.AccountId = outcome.AccountId;
            session.State = SessionState.Authenticated;
            _log.Information("Sesión {SessionId} autenticada como cuenta {AccountId}", session.Id, outcome.AccountId);
        }

        session.Send(Opcode.AuthResult, new S2CAuthResult
        {
            Ok = outcome.Ok,
            Code = outcome.Code,
            AccountId = outcome.AccountId,
            SessionToken = outcome.SessionToken,
        });
    }

    /// <summary>
    /// La build del cliente acaba en los logs: se recorta y se le quitan los caracteres de
    /// control para que nadie pueda inyectar saltos de línea en el fichero de log.
    /// </summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.Length > 32 ? value[..32] : value;
        return string.Create(trimmed.Length, trimmed, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsControl(source[i]) ? '?' : source[i];
            }
        });
    }
}

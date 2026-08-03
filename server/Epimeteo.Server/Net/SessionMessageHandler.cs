using Epimeteo.Server.Persistence.Accounts;
using Epimeteo.Server.Persistence.Characters;
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
/// <c>Login</c>, <c>Register</c> y los cinco opcodes de personaje también se resuelven aquí
/// (tocan Postgres, no el tick), pero awaited: la sesión no procesa el siguiente frame hasta que
/// la BD responde. No bloquea otras sesiones, que tienen su propio bucle de lectura.
/// </para>
/// </summary>
public sealed class SessionMessageHandler
{
    private readonly ServerOptions _options;
    private readonly IWorldInbox _worldInbox;
    private readonly AuthService _auth;
    private readonly CharacterService _characters;
    private readonly ILogger _log = Log.ForContext<SessionMessageHandler>();

    public SessionMessageHandler(ServerOptions options, IWorldInbox worldInbox, AuthService auth, CharacterService characters)
    {
        _options = options;
        _worldInbox = worldInbox;
        _auth = auth;
        _characters = characters;
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

            case Opcode.CharListRequest:
                return HandleCharListRequestAsync(session);

            case Opcode.CharCreate:
                return HandleCharCreateAsync(session, frame);

            case Opcode.CharDelete:
                return HandleCharDeleteAsync(session, frame);

            case Opcode.CharSelect:
                return HandleCharSelectAsync(session, frame);

            case Opcode.WorldReady:
                HandleWorldReady(session, frame);
                return Task.CompletedTask;

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

    private async Task HandleCharListRequestAsync(Session session)
    {
        var list = await _characters.ListAsync(session.AccountId).ConfigureAwait(false);
        session.Send(Opcode.CharList, new S2CCharList { Characters = list });
    }

    private async Task HandleCharCreateAsync(Session session, ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecodePayload<C2SCharCreate>(frame, out var create) || create is null)
        {
            _log.Warning("CharCreate ilegible en la sesión {SessionId}", session.Id);
            session.Kick(KickReason.ProtocolError);
            return;
        }

        var outcome = await _characters
            .CreateAsync(session.AccountId, create.Name, create.ClassKey, create.Slot, create.PaletteIndex)
            .ConfigureAwait(false);

        session.Send(Opcode.CharCreateResult, new S2CCharCreateResult
        {
            Ok = outcome.Ok,
            Code = outcome.Code,
            Character = outcome.Summary,
        });
    }

    private async Task HandleCharDeleteAsync(Session session, ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecodePayload<C2SCharDelete>(frame, out var delete) || delete is null)
        {
            _log.Warning("CharDelete ilegible en la sesión {SessionId}", session.Id);
            session.Kick(KickReason.ProtocolError);
            return;
        }

        var outcome = await _characters
            .DeleteAsync(session.AccountId, delete.CharacterId, delete.Confirm)
            .ConfigureAwait(false);

        session.Send(Opcode.CharDeleteResult, new S2CCharDeleteResult
        {
            Ok = outcome.Ok,
            Code = outcome.Code,
            CharacterId = delete.CharacterId,
        });
    }

    /// <summary>
    /// A diferencia de <c>CharCreate</c>/<c>CharDelete</c>, <c>CharSelect</c> no tiene un opcode
    /// de fallo dedicado (docs/01-protocolo.md): el diseño da por hecho que el cliente sólo
    /// selecciona personajes que ya le llegaron en su propio <c>CharList</c>. Un fallo aquí sólo
    /// lo produce una condición de carrera (borrado desde otra sesión) o un cliente manipulado;
    /// se trata igual que cualquier otro dato imposible con un cliente honesto — Kick, no un
    /// mensaje en banda nuevo que el protocolo cerrado no declaró.
    /// </summary>
    private async Task HandleCharSelectAsync(Session session, ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecodePayload<C2SCharSelect>(frame, out var select) || select is null)
        {
            _log.Warning("CharSelect ilegible en la sesión {SessionId}", session.Id);
            session.Kick(KickReason.ProtocolError);
            return;
        }

        var outcome = await _characters.SelectAsync(session.AccountId, select.CharacterId).ConfigureAwait(false);
        if (!outcome.Ok || outcome.Character is null)
        {
            _log.Warning("CharSelect a personaje {CharacterId} inexistente/ajeno en la sesión {SessionId}",
                select.CharacterId, session.Id);
            session.Kick(KickReason.ProtocolError, outcome.Code);
            return;
        }

        var character = outcome.Character;
        session.CharacterId = character.Id;
        session.State = SessionState.Loading;

        session.Send(Opcode.WorldEnter, new S2CWorldEnter
        {
            MapKey = character.MapKey,
            SpawnX = character.PosX,
            SpawnY = character.PosY,
            Facing = character.Facing,
            MyEntityId = character.Id,
            Stats = new CharacterStats
            {
                Level = character.Level,
                Xp = character.Xp,
                Str = character.StatStr,
                Int = character.StatInt,
                Vit = character.StatVit,
                Dex = character.StatDex,
                StatPoints = character.StatPoints,
                Hp = character.Hp,
                Mp = character.Mp,
                Gold = character.Gold,
            },
            ServerTimeMs = ServerClock.NowMs,
        });

        _log.Information("Sesión {SessionId} entra con el personaje {CharacterId} ({Name}) en {MapKey}",
            session.Id, character.Id, character.Name, character.MapKey);
    }

    private void HandleWorldReady(Session session, ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecodePayload<C2SWorldReady>(frame, out _))
        {
            _log.Warning("WorldReady ilegible en la sesión {SessionId}", session.Id);
            session.Kick(KickReason.ProtocolError);
            return;
        }

        session.State = SessionState.InWorld;
        _log.Information("Sesión {SessionId} en el mundo con el personaje {CharacterId}", session.Id, session.CharacterId);
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

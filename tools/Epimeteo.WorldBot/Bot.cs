using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.WorldBot;

/// <summary>
/// Un cliente de verdad, sin Godot: habla el protocolo real y ejecuta <b>el mismo</b>
/// <see cref="ClientPrediction"/> y el mismo <see cref="MovementSystem"/> que el cliente del juego.
/// Por eso lo que verifica el bot vale para el juego (FASE-04 §10).
/// </summary>
internal sealed class Bot : IAsyncDisposable
{
    /// <summary>Veces que se reintenta la autenticación antes de rendirse.</summary>
    private const int AuthAttempts = 4;

    /// <summary>Espera entre reintentos: la ventana del cupo es de un minuto, más un margen.</summary>
    private const int AuthRetryWaitMs = 65_000;

    private readonly ConcurrentQueue<byte[]> _received = new();
    private readonly Channel<byte[]> _outbound = Channel.CreateUnbounded<byte[]>();
    private readonly LagPipe<byte[]> _inLag;
    private readonly LagPipe<byte[]> _outLag;
    private readonly GameMap _map;
    private readonly string _password;

    private ClientWebSocket _socket = new();
    private Task? _receiveLoop;
    private Task? _sendLoop;
    private uint _seq;
    private bool _closed;

    public Bot(string url, string username, string characterName, GameMap map, int lagMs)
    {
        Url = url;
        Username = username;
        CharacterName = characterName;
        _map = map;
        _password = "bot-de-prueba-12345";
        _inLag = new LagPipe<byte[]>(lagMs);
        _outLag = new LagPipe<byte[]>(lagMs);
    }

    public string Url { get; }

    public string Username { get; }

    public string CharacterName { get; }

    /// <summary>Patrón de movimiento activo. El driver lo cambia por fases.</summary>
    public MovementPattern Pattern { get; set; } = MovementPattern.Quieto;

    /// <summary>Inputs que manda por tick. &gt; 1 es un cliente parcheado para ir más rápido.</summary>
    public int InputsPerTick { get; set; } = 1;

    /// <summary>Instante en que empezó el patrón actual, para los patrones con forma.</summary>
    public long PatternStartMs { get; set; }

    public ClientPrediction? Prediction { get; private set; }

    public int MyEntityId { get; private set; }

    public S2CWorldEnter? WorldEnter { get; private set; }

    // ── Métricas de la corrida ────────────────────────────────────────────
    public int Snapshots { get; private set; }

    public uint LastAckedSeq { get; private set; }

    public Vec2 ServerPos { get; private set; }

    public int SolidViolations { get; private set; }

    public List<int> Spawned { get; } = [];

    public List<(int Id, DespawnReason Reason)> Despawned { get; } = [];

    public List<(string Region, ZoneFlags Flags)> ZoneUpdates { get; } = [];

    public KickReason? Kicked { get; private set; }

    public int Corrections => Prediction?.Corrections ?? 0;

    public float MaxErrorTiles => Prediction?.MaxErrorTiles ?? 0f;

    /// <summary>Distancia recorrida por la posición <b>autoritativa</b> desde el último reinicio.</summary>
    public float ServerDistance { get; private set; }

    /// <summary>Conecta, entra al mundo y deja al bot listo para recibir ticks.</summary>
    public async Task ConnectAsync(bool register)
    {
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(Url), CancellationToken.None);
        _receiveLoop = Task.Run(ReceiveLoopAsync);
        _sendLoop = Task.Run(SendLoopAsync);

        await SendNowAsync(Opcode.Hello, new C2SHello { ProtocolVersion = ProtocolVersion.Current, ClientBuild = "worldbot" });
        await ExpectAsync<S2CHelloAck>(Opcode.HelloAck);

        await AuthenticateAsync(register);

        await SendNowAsync(Opcode.CharListRequest, new C2SCharListRequest());
        var list = await ExpectAsync<S2CCharList>(Opcode.CharList);

        long characterId;
        if (list is { Characters.Length: > 0 })
        {
            characterId = list.Characters[0].Id;
        }
        else
        {
            await SendNowAsync(Opcode.CharCreate, new C2SCharCreate
            {
                Name = CharacterName,
                ClassKey = "class.warrior",
                Slot = 0,
                PaletteIndex = 0,
            });

            var created = await ExpectAsync<S2CCharCreateResult>(Opcode.CharCreateResult);
            if (created is not { Ok: true, Character: not null })
            {
                throw new InvalidOperationException($"{Username}: no se pudo crear el personaje ({created?.Code}).");
            }

            characterId = created.Character.Id;
        }

        await SendNowAsync(Opcode.CharSelect, new C2SCharSelect { CharacterId = characterId });
        var enter = await ExpectAsync<S2CWorldEnter>(Opcode.WorldEnter)
            ?? throw new InvalidOperationException($"{Username}: sin WorldEnter.");

        if (enter.MapHash != _map.Hash)
        {
            throw new InvalidOperationException(
                $"{Username}: el mapa del servidor ({enter.MapHash:X8}) no es el de content/ ({_map.Hash:X8}).");
        }

        WorldEnter = enter;
        MyEntityId = enter.MyEntityId;
        ServerPos = new Vec2(enter.SpawnX, enter.SpawnY);
        Prediction = new ClientPrediction(_map.Collision, MoveState.AtRest(ServerPos, enter.Facing));

        await SendNowAsync(Opcode.WorldReady, new C2SWorldReady());
    }

    /// <summary>
    /// Se autentica, esperando a que se libere la ventana si topa con el cupo de intentos por IP.
    /// <para>
    /// Ese cupo (<c>Server:LoginAttemptsPerMinute</c>, 5 por defecto desde la Fase 2) es una
    /// política antifuerza bruta correcta, pero cuenta por IP y una flota de carga entera sale de
    /// la misma: sin esta espera, levantar 10 bots agota el cupo y los que sobran se caen a mitad
    /// de la corrida. Para las corridas largas conviene subir la opción en el servidor de pruebas
    /// en vez de esperar aquí.
    /// </para>
    /// </summary>
    private async Task AuthenticateAsync(bool register)
    {
        for (var intento = 1; ; intento++)
        {
            if (register)
            {
                await SendNowAsync(Opcode.Register, new C2SRegister { Username = Username, Email = null, Password = _password });
            }
            else
            {
                await SendNowAsync(Opcode.Login, new C2SLogin { Username = Username, Password = _password });
            }

            var auth = await ExpectAsync<S2CAuthResult>(Opcode.AuthResult);
            if (auth is { Ok: true })
            {
                return;
            }

            if (auth?.Code != ResultCode.RateLimited || intento == AuthAttempts)
            {
                throw new InvalidOperationException($"{Username}: autenticación rechazada ({auth?.Code}).");
            }

            Console.WriteLine(
                $"  … {Username}: cupo de intentos por IP agotado; espera {AuthRetryWaitMs / 1000} s " +
                $"(intento {intento} de {AuthAttempts})");

            await KeepAliveAsync(AuthRetryWaitMs);
        }
    }

    /// <summary>
    /// Espera mandando <c>Ping</c> de vez en cuando. Callado más de <c>Server:IdleTimeoutMs</c>
    /// (30 s) el servidor cierra la sesión, así que una espera larga hay que sostenerla.
    /// </summary>
    private async Task KeepAliveAsync(int totalMs)
    {
        const int PingEveryMs = 10_000;

        for (var esperado = 0; esperado < totalMs; esperado += PingEveryMs)
        {
            await Task.Delay(Math.Min(PingEveryMs, totalMs - esperado));
            await SendNowAsync(Opcode.Ping, new C2SPing { ClientTimeMs = Environment.TickCount64 });
        }
    }

    /// <summary>Cierra la conexión sin avisar, como quien cierra el juego de golpe.</summary>
    public async Task DisconnectAsync()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _outbound.Writer.TryComplete();

        try
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // Ya estaba cerrada.
        }

        _socket.Dispose();
    }

    /// <summary>Un tick de cliente: procesar lo recibido, predecir y mandar el input.</summary>
    public void Tick(long nowMs)
    {
        while (_received.TryDequeue(out var frame))
        {
            _inLag.Push(frame, nowMs);
        }

        while (_inLag.TryPop(nowMs, out var frame))
        {
            Handle(frame);
        }

        for (var i = 0; i < InputsPerTick; i++)
        {
            var (dirX, dirY) = Direction(nowMs);
            var input = new MoveInput(++_seq, dirX, dirY, FacingOf(dirX, dirY));
            Prediction?.ApplyInput(input);

            _outLag.Push(
                FrameCodec.Encode(Opcode.InputState, new C2SInputState
                {
                    Seq = input.Seq,
                    DirX = input.DirX,
                    DirY = input.DirY,
                    Facing = input.Facing,
                    Flags = 0,
                    DtMs = (ushort)SimulationConstants.TickDtMs,
                }),
                nowMs);
        }

        while (_outLag.TryPop(nowMs, out var bytes))
        {
            _outbound.Writer.TryWrite(bytes);
        }
    }

    /// <summary>Pone a cero los contadores de una fase para medir sólo la siguiente.</summary>
    public void ResetPhase()
    {
        Spawned.Clear();
        Despawned.Clear();
        ZoneUpdates.Clear();
        ServerDistance = 0f;
        Snapshots = 0;
    }

    private void Handle(byte[] frame)
    {
        if (!FrameCodec.TryReadOpcode(frame, out var opcode))
        {
            return;
        }

        switch (opcode)
        {
            case Opcode.Snapshot:
                HandleSnapshot(frame);
                break;

            case Opcode.EntitySpawn when FrameCodec.TryDecodePayload<S2CEntitySpawn>(frame, out var spawn) && spawn is not null:
                Spawned.AddRange(spawn.Entities.Select(entity => entity.Id));
                break;

            case Opcode.EntityDespawn when FrameCodec.TryDecodePayload<S2CEntityDespawn>(frame, out var despawn) && despawn is not null:
                Despawned.AddRange(despawn.Entities.Select(entity => (entity.Id, entity.Reason)));
                break;

            case Opcode.ZoneFlagsUpdate when FrameCodec.TryDecodePayload<S2CZoneFlagsUpdate>(frame, out var zone) && zone is not null:
                ZoneUpdates.Add((zone.RegionName, zone.Flags));
                break;

            case Opcode.Kick when FrameCodec.TryDecodePayload<S2CKick>(frame, out var kick) && kick is not null:
                Kicked = kick.Reason;
                break;

            default:
                break;
        }
    }

    private void HandleSnapshot(byte[] frame)
    {
        if (!FrameCodec.TryDecodePayload<S2CSnapshot>(frame, out var snapshot) || snapshot is null)
        {
            return;
        }

        Snapshots++;
        LastAckedSeq = snapshot.LastAckedInputSeq;

        var mine = snapshot.Entities.FirstOrDefault(entity => entity.Id == MyEntityId);
        if (mine is null || Prediction is null)
        {
            return;
        }

        var position = new Vec2(mine.X, mine.Y);
        ServerDistance += MathF.Sqrt(Vec2.DistanceSquared(ServerPos, position));
        ServerPos = position;

        if (_map.Collision.IsBlocked(position, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight))
        {
            SolidViolations++;
        }

        Prediction.ApplyAuthoritative(
            new MoveState(position, new Vec2(mine.Vx, mine.Vy), mine.Facing, mine.Anim),
            snapshot.LastAckedInputSeq);
    }

    /// <summary>Dirección del patrón activo en este instante.</summary>
    private (sbyte X, sbyte Y) Direction(long nowMs)
    {
        var elapsed = nowMs - PatternStartMs;

        return Pattern switch
        {
            MovementPattern.Quieto => (0, 0),
            MovementPattern.Muro => (1, 0),
            MovementPattern.Muralla => (0, -1),
            MovementPattern.MurallaVuelta => (0, 1),
            MovementPattern.Encuentro => (0, 0),
            MovementPattern.Circulo => (elapsed / 3000 % 4) switch
            {
                0 => ((sbyte)1, (sbyte)0),
                1 => (0, 1),
                2 => (-1, 0),
                _ => (0, -1),
            },
            _ => (0, 0),
        };
    }

    private static Facing FacingOf(sbyte dirX, sbyte dirY)
    {
        if (dirY != 0)
        {
            return dirY < 0 ? Facing.North : Facing.South;
        }

        return dirX < 0 ? Facing.West : Facing.East;
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameBytes];

        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                _received.Enqueue(buffer[..result.Count]);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // La conexión se fue; el driver lo verá en las métricas.
        }
    }

    private async Task SendLoopAsync()
    {
        try
        {
            await foreach (var frame in _outbound.Reader.ReadAllAsync())
            {
                if (_socket.State != WebSocketState.Open)
                {
                    return;
                }

                await _socket.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Idem.
        }
    }

    /// <summary>Envío inmediato, sin pasar por el simulador de latencia: sólo para el handshake.</summary>
    private async Task SendNowAsync<T>(Opcode opcode, T payload) =>
        await _socket.SendAsync(FrameCodec.Encode(opcode, payload), WebSocketMessageType.Binary, true, CancellationToken.None);

    /// <summary>Espera un opcode concreto durante el handshake, descartando lo que no sea.</summary>
    private async Task<T?> ExpectAsync<T>(Opcode expected, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            if (_received.TryDequeue(out var frame) &&
                FrameCodec.TryReadOpcode(frame, out var opcode))
            {
                if (opcode == expected)
                {
                    return FrameCodec.TryDecodePayload<T>(frame, out var payload) ? payload : default;
                }

                if (opcode == Opcode.Kick && FrameCodec.TryDecodePayload<S2CKick>(frame, out var kick))
                {
                    throw new InvalidOperationException($"{Username}: expulsado durante el handshake ({kick?.Reason}).");
                }
            }

            await Task.Delay(5);
        }

        throw new TimeoutException($"{Username}: no llegó {expected}.");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();

        if (_receiveLoop is not null)
        {
            await _receiveLoop.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        if (_sendLoop is not null)
        {
            await _sendLoop.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}

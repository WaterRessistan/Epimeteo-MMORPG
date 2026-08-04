using System;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using Epimeteo.Shared.Time;
using Godot;

namespace Epimeteo.Client.Net;

/// <summary>
/// Cliente de red: mantiene el WebSocket, hace el handshake y mide el RTT.
/// No sabe nada de interfaz; comunica lo que pasa con eventos de C#.
/// <para>
/// Todo ocurre en el hilo principal de Godot dentro de <see cref="_Process"/>: el
/// <see cref="WebSocketPeer"/> es de sondeo, no de callbacks, y así no hay concurrencia
/// que gestionar en el cliente.
/// </para>
/// </summary>
public partial class NetClient : Node
{
    /// <summary>Cadencia de <c>Ping</c>, en segundos (docs/01-protocolo.md § Ritmos).</summary>
    private const double PingIntervalSec = 1.0;

    /// <summary>Peso de la última medida en la media móvil del RTT.</summary>
    private const double RttSmoothing = 0.25;

    private readonly WebSocketPeer _peer = new();
    private NetLagSimulator _inboundLag = new(0);
    private NetLagSimulator _outboundLag = new(0);
    private SessionState _state = SessionState.None;
    private double _pingTimer;
    private bool _connectRequested;

    /// <summary>Se dispara cuando cambia el estado de la conexión.</summary>
    public event Action<ConnectionStatus>? StatusChanged;

    /// <summary>Se dispara al recibir un <c>Pong</c>, con el RTT de esa medida en ms.</summary>
    public event Action<long>? RttMeasured;

    /// <summary>Se dispara cuando el servidor expulsa al cliente.</summary>
    public event Action<KickReason, ResultCode, int>? Kicked;

    /// <summary>Se dispara al recibir la respuesta de <c>Login</c> o <c>Register</c>.</summary>
    public event Action<S2CAuthResult>? AuthResultReceived;

    /// <summary>Se dispara al recibir la lista de personajes de la cuenta.</summary>
    public event Action<S2CCharList>? CharListReceived;

    /// <summary>Se dispara al recibir la respuesta de <c>CharCreate</c>.</summary>
    public event Action<S2CCharCreateResult>? CharCreateResultReceived;

    /// <summary>Se dispara al recibir la respuesta de <c>CharDelete</c>.</summary>
    public event Action<S2CCharDeleteResult>? CharDeleteResultReceived;

    /// <summary>Se dispara al recibir <c>WorldEnter</c> tras un <c>CharSelect</c> con éxito.</summary>
    public event Action<S2CWorldEnter>? WorldEnterReceived;

    /// <summary>Se dispara con cada <c>Snapshot</c>: el estado autoritativo de lo que se ve.</summary>
    public event Action<S2CSnapshot>? SnapshotReceived;

    /// <summary>Se dispara cuando entran entidades en el área de interés.</summary>
    public event Action<S2CEntitySpawn>? EntitySpawnReceived;

    /// <summary>Se dispara cuando salen entidades del área de interés (o mueren, o se van).</summary>
    public event Action<S2CEntityDespawn>? EntityDespawnReceived;

    /// <summary>Se dispara al cruzar de región, con los flags que decide el servidor.</summary>
    public event Action<S2CZoneFlagsUpdate>? ZoneFlagsUpdateReceived;

    /// <summary>Se dispara una vez al entrar al mundo, con los contenedores 0/1/2 completos.</summary>
    public event Action<S2CInventoryFull>? InventoryFullReceived;

    /// <summary>Se dispara tras cada mutación de inventario con éxito.</summary>
    public event Action<S2CInventoryDelta>? InventoryDeltaReceived;

    /// <summary>Se dispara al entrar al mundo y tras cada <c>Equip</c>/<c>Unequip</c> con éxito.</summary>
    public event Action<S2CEquipmentUpdate>? EquipmentUpdateReceived;

    /// <summary>Se dispara con avisos sin opcode dedicado propio (FASE-06 §5): p. ej. un <c>Equip</c> rechazado.</summary>
    public event Action<S2CSystemMessage>? SystemMessageReceived;

    /// <summary>Estado actual de la conexión.</summary>
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    /// <summary>Último RTT medido, en ms. -1 si aún no hay medida.</summary>
    public long LastRttMs { get; private set; } = -1;

    /// <summary>Media móvil del RTT, en ms. -1 si aún no hay medida.</summary>
    public double AverageRttMs { get; private set; } = -1;

    /// <summary>Ritmos que anunció el servidor en su <c>HelloAck</c>.</summary>
    public S2CHelloAck? ServerInfo { get; private set; }

    /// <summary>Cuenta autenticada. 0 hasta que <c>Login</c>/<c>Register</c> tienen éxito.</summary>
    public long AccountId { get; private set; }

    /// <summary>Token de sesión en claro recibido en el último <c>AuthResult</c> con éxito.</summary>
    public string? SessionToken { get; private set; }

    /// <summary>
    /// Último <c>WorldEnter</c> recibido. <see cref="Godot.SceneTree.ChangeSceneToFile"/> no pasa
    /// datos entre escenas; la pantalla siguiente lo lee aquí en su <c>_Ready</c> porque
    /// <see cref="NetClient"/> es autoload y sobrevive al cambio.
    /// </summary>
    public S2CWorldEnter? LastWorldEnter { get; private set; }

    /// <summary>
    /// Personaje con el que se entró al mundo. <c>WorldEnter</c> no trae el nombre ni la paleta
    /// —el servidor da por hecho que el cliente ya los tiene de la lista de personajes— así que se
    /// guardan aquí al elegirlo, para que la escena de mundo pueda dibujarse a sí misma.
    /// </summary>
    public CharacterSummary? SelectedCharacter { get; private set; }

    /// <summary>Latencia simulada por sentido, en ms. 0 si no se pidió ninguna.</summary>
    public int SimulatedLagMs => _inboundLag.LagMs;

    /// <inheritdoc />
    public override void _Ready()
    {
        var lagMs = NetLagSimulator.ReadConfiguredLagMs();
        _inboundLag = new NetLagSimulator(lagMs);
        _outboundLag = new NetLagSimulator(lagMs);

        if (lagMs > 0)
        {
            GD.Print($"Simulador de latencia activo: {lagMs} ms por sentido ({lagMs * 2} ms de RTT).");
        }
    }

    /// <summary>Abre la conexión. Si ya había una, se descarta.</summary>
    public void ConnectTo(string url)
    {
        Close();

        var error = _peer.ConnectToUrl(url);
        if (error != Error.Ok)
        {
            GD.PushError($"No se pudo iniciar la conexión a {url}: {error}");
            SetStatus(ConnectionStatus.Failed);
            return;
        }

        _connectRequested = true;
        _state = SessionState.Connecting;
        _pingTimer = 0;
        LastRttMs = -1;
        AverageRttMs = -1;
        ServerInfo = null;
        SetStatus(ConnectionStatus.Connecting);
    }

    /// <summary>Cierra la conexión si estaba abierta.</summary>
    public void Close()
    {
        if (_peer.GetReadyState() != WebSocketPeer.State.Closed)
        {
            _peer.Close();
        }

        _inboundLag.Clear();
        _outboundLag.Clear();
        _connectRequested = false;
        _state = SessionState.None;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_connectRequested)
        {
            return;
        }

        _peer.Poll();

        switch (_peer.GetReadyState())
        {
            case WebSocketPeer.State.Open:
                if (_state == SessionState.Connecting && Status == ConnectionStatus.Connecting)
                {
                    SendHello();
                }

                PumpIncoming();
                TickPing(delta);
                FlushOutbound();
                break;

            case WebSocketPeer.State.Closed:
                OnClosed();
                break;
        }
    }

    /// <summary>Manda <c>Login</c>. Sólo válido con la sesión en estado <c>Greeted</c>.</summary>
    public void Login(string username, string password) =>
        Send(Opcode.Login, new C2SLogin { Username = username, Password = password });

    /// <summary>Manda <c>Register</c>. Sólo válido con la sesión en estado <c>Greeted</c>.</summary>
    public void Register(string username, string? email, string password) =>
        Send(Opcode.Register, new C2SRegister { Username = username, Email = email, Password = password });

    /// <summary>Pide la lista de personajes de la cuenta. Sólo válido en <c>Authenticated</c>.</summary>
    public void RequestCharList() => Send(Opcode.CharListRequest, new C2SCharListRequest());

    /// <summary>Manda <c>CharCreate</c>. Sólo válido en <c>Authenticated</c>.</summary>
    public void CreateCharacter(string name, string classKey, int slot, byte paletteIndex) => Send(
        Opcode.CharCreate,
        new C2SCharCreate { Name = name, ClassKey = classKey, Slot = slot, PaletteIndex = paletteIndex });

    /// <summary>Manda <c>CharDelete</c>. Sólo válido en <c>Authenticated</c>.</summary>
    public void DeleteCharacter(long characterId, bool confirm) =>
        Send(Opcode.CharDelete, new C2SCharDelete { CharacterId = characterId, Confirm = confirm });

    /// <summary>Manda <c>CharSelect</c>. Sólo válido en <c>Authenticated</c>.</summary>
    /// <param name="character">El personaje elegido, tal como venía en la lista.</param>
    public void SelectCharacter(CharacterSummary character)
    {
        SelectedCharacter = character;
        Send(Opcode.CharSelect, new C2SCharSelect { CharacterId = character.Id });
    }

    /// <summary>
    /// Manda <c>WorldReady</c> y adelanta el estado local a <see cref="SessionState.InWorld"/>:
    /// no hay confirmación del servidor para este mensaje (docs/01-protocolo.md), así que el
    /// cliente no tiene nada que esperar antes de considerarse dentro del mundo.
    /// </summary>
    public void SendWorldReady()
    {
        Send(Opcode.WorldReady, new C2SWorldReady());
        _state = SessionState.InWorld;
    }

    private void SendHello()
    {
        Send(Opcode.Hello, new C2SHello
        {
            ProtocolVersion = ProtocolVersion.Current,
            ClientBuild = ClientBuildInfo.Build,
        });

        SetStatus(ConnectionStatus.Greeting);
    }

    private void TickPing(double delta)
    {
        // Ping es legal en cualquier estado a partir de Greeted (docs/01-protocolo.md); antes de
        // eso el servidor aún no ha visto el Hello y lo rechazaría.
        if (_state is SessionState.None or SessionState.Connecting)
        {
            return;
        }

        _pingTimer -= delta;
        if (_pingTimer > 0)
        {
            return;
        }

        _pingTimer = PingIntervalSec;
        Send(Opcode.Ping, new C2SPing { ClientTimeMs = ServerClock.NowMs });
    }

    /// <summary>
    /// Manda la intención de movimiento de un tick. Es lo <b>único</b> que el cliente dice sobre
    /// su posición: nunca manda dónde está, sólo hacia dónde quiere ir (CLAUDE.md §4).
    /// </summary>
    public void SendInput(in MoveInput input) => Send(Opcode.InputState, new C2SInputState
    {
        Seq = input.Seq,
        DirX = input.DirX,
        DirY = input.DirY,
        Facing = input.Facing,
        Flags = 0,

        // El servidor no lo integra desde la Fase 4 (paso fijo, FASE-04 §2 D1); viaja sólo para
        // que pueda diagnosticar el jitter del cliente.
        DtMs = SimulationConstants.TickDtMs,
    });

    /// <summary>Mover (o apilar, o dividir) un ítem entre dos huecos del inventario propio.</summary>
    public void SendInvMove(ContainerId fromContainer, byte fromSlot, ContainerId toContainer, byte toSlot, int quantity) =>
        Send(Opcode.InvMove, new C2SInvMove
        {
            FromContainer = fromContainer,
            FromSlot = fromSlot,
            ToContainer = toContainer,
            ToSlot = toSlot,
            Quantity = quantity,
        });

    /// <summary>Usar un ítem (por ahora sólo consumibles de curación).</summary>
    public void SendInvUse(ContainerId container, byte slot) =>
        Send(Opcode.InvUse, new C2SInvUse { Container = container, Slot = slot });

    /// <summary>Tirar (destruir) parte o todo un stack. Sin saco de loot: no queda nada en el mundo.</summary>
    public void SendInvDrop(ContainerId container, byte slot, int quantity) =>
        Send(Opcode.InvDrop, new C2SInvDrop { Container = container, Slot = slot, Quantity = quantity });

    /// <summary>Equipar el ítem de <c>(container, slot)</c> en un hueco de equipo concreto.</summary>
    public void SendEquip(ContainerId container, byte slot, EquipSlot equipSlot) =>
        Send(Opcode.Equip, new C2SEquip { Container = container, Slot = slot, EquipSlot = equipSlot });

    /// <summary>Desequipar, de vuelta a la bolsa que le toque por su tipo.</summary>
    public void SendUnequip(EquipSlot equipSlot) => Send(Opcode.Unequip, new C2SUnequip { EquipSlot = equipSlot });

    private void PumpIncoming()
    {
        var now = ServerClock.NowMs;

        while (_peer.GetAvailablePacketCount() > 0)
        {
            _inboundLag.Push(_peer.GetPacket(), now);
        }

        while (_inboundLag.TryPop(now, out var frame))
        {
            if (!FrameCodec.TryReadOpcode(frame, out var opcode))
            {
                GD.PushWarning($"Frame de {frame.Length} B sin cabecera; descartado.");
                continue;
            }

            Dispatch(opcode, frame);
        }
    }

    /// <summary>Suelta los frames que ya han cumplido su retardo de salida.</summary>
    private void FlushOutbound()
    {
        var now = ServerClock.NowMs;

        while (_outboundLag.TryPop(now, out var frame))
        {
            var error = _peer.Send(frame, WebSocketPeer.WriteMode.Binary);
            if (error != Error.Ok)
            {
                GD.PushError($"Error al enviar un frame de {frame.Length} B: {error}");
            }
        }
    }

    private void Dispatch(Opcode opcode, byte[] frame)
    {
        switch (opcode)
        {
            case Opcode.HelloAck:
                if (FrameCodec.TryDecodePayload<S2CHelloAck>(frame, out var ack) && ack is not null)
                {
                    ServerInfo = ack;
                    _state = SessionState.Greeted;
                    _pingTimer = 0;
                    SetStatus(ConnectionStatus.Connected);
                }

                break;

            case Opcode.Pong:
                if (FrameCodec.TryDecodePayload<S2CPong>(frame, out var pong) && pong is not null)
                {
                    OnPong(pong);
                }

                break;

            case Opcode.AuthResult:
                if (FrameCodec.TryDecodePayload<S2CAuthResult>(frame, out var auth) && auth is not null)
                {
                    if (auth.Ok)
                    {
                        AccountId = auth.AccountId;
                        SessionToken = auth.SessionToken;
                        _state = SessionState.Authenticated;
                    }

                    AuthResultReceived?.Invoke(auth);
                }

                break;

            case Opcode.CharList:
                if (FrameCodec.TryDecodePayload<S2CCharList>(frame, out var charList) && charList is not null)
                {
                    CharListReceived?.Invoke(charList);
                }

                break;

            case Opcode.CharCreateResult:
                if (FrameCodec.TryDecodePayload<S2CCharCreateResult>(frame, out var createResult) && createResult is not null)
                {
                    CharCreateResultReceived?.Invoke(createResult);
                }

                break;

            case Opcode.CharDeleteResult:
                if (FrameCodec.TryDecodePayload<S2CCharDeleteResult>(frame, out var deleteResult) && deleteResult is not null)
                {
                    CharDeleteResultReceived?.Invoke(deleteResult);
                }

                break;

            case Opcode.WorldEnter:
                if (FrameCodec.TryDecodePayload<S2CWorldEnter>(frame, out var worldEnter) && worldEnter is not null)
                {
                    _state = SessionState.Loading;
                    LastWorldEnter = worldEnter;
                    WorldEnterReceived?.Invoke(worldEnter);
                }

                break;

            case Opcode.Snapshot:
                if (FrameCodec.TryDecodePayload<S2CSnapshot>(frame, out var snapshot) && snapshot is not null)
                {
                    SnapshotReceived?.Invoke(snapshot);
                }

                break;

            case Opcode.EntitySpawn:
                if (FrameCodec.TryDecodePayload<S2CEntitySpawn>(frame, out var spawn) && spawn is not null)
                {
                    EntitySpawnReceived?.Invoke(spawn);
                }

                break;

            case Opcode.EntityDespawn:
                if (FrameCodec.TryDecodePayload<S2CEntityDespawn>(frame, out var despawn) && despawn is not null)
                {
                    EntityDespawnReceived?.Invoke(despawn);
                }

                break;

            case Opcode.ZoneFlagsUpdate:
                if (FrameCodec.TryDecodePayload<S2CZoneFlagsUpdate>(frame, out var zone) && zone is not null)
                {
                    ZoneFlagsUpdateReceived?.Invoke(zone);
                }

                break;

            case Opcode.InventoryFull:
                if (FrameCodec.TryDecodePayload<S2CInventoryFull>(frame, out var invFull) && invFull is not null)
                {
                    InventoryFullReceived?.Invoke(invFull);
                }

                break;

            case Opcode.InventoryDelta:
                if (FrameCodec.TryDecodePayload<S2CInventoryDelta>(frame, out var invDelta) && invDelta is not null)
                {
                    InventoryDeltaReceived?.Invoke(invDelta);
                }

                break;

            case Opcode.EquipmentUpdate:
                if (FrameCodec.TryDecodePayload<S2CEquipmentUpdate>(frame, out var equipUpdate) && equipUpdate is not null)
                {
                    EquipmentUpdateReceived?.Invoke(equipUpdate);
                }

                break;

            case Opcode.SystemMessage:
                if (FrameCodec.TryDecodePayload<S2CSystemMessage>(frame, out var sysMsg) && sysMsg is not null)
                {
                    SystemMessageReceived?.Invoke(sysMsg);
                }

                break;

            case Opcode.Kick:
                if (FrameCodec.TryDecodePayload<S2CKick>(frame, out var kick) && kick is not null)
                {
                    Kicked?.Invoke(kick.Reason, kick.Detail, kick.ServerProtocolVersion);
                    SetStatus(ConnectionStatus.Kicked);
                }

                break;

            default:
                // Mensaje de una fase que este cliente todavía no implementa: se ignora.
                // Al revés que en el servidor, aquí no se corta la conexión.
                GD.Print($"Opcode {opcode} sin manejador en el cliente; ignorado.");
                break;
        }
    }

    private void OnPong(S2CPong pong)
    {
        // Los dos sellos salen del mismo reloj monotónico local: el RTT no depende de que los
        // relojes de cliente y servidor estén sincronizados.
        LastRttMs = Math.Max(0, ServerClock.NowMs - pong.ClientTimeMs);
        AverageRttMs = AverageRttMs < 0
            ? LastRttMs
            : (AverageRttMs * (1 - RttSmoothing)) + (LastRttMs * RttSmoothing);

        RttMeasured?.Invoke(LastRttMs);
    }

    private void OnClosed()
    {
        var code = _peer.GetCloseCode();
        _connectRequested = false;
        _state = SessionState.None;

        if (Status != ConnectionStatus.Kicked)
        {
            GD.Print($"Conexión cerrada (código {code}).");
            SetStatus(Status == ConnectionStatus.Connected ? ConnectionStatus.Disconnected : ConnectionStatus.Failed);
        }
    }

    /// <summary>
    /// Encola un mensaje. Con el simulador de latencia apagado sale en esta misma llamada, porque
    /// el frame vence de inmediato; con él encendido espera su turno en <see cref="FlushOutbound"/>.
    /// </summary>
    private void Send<T>(Opcode opcode, T payload)
    {
        _outboundLag.Push(FrameCodec.Encode(opcode, payload), ServerClock.NowMs);
        FlushOutbound();
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(status);
    }
}

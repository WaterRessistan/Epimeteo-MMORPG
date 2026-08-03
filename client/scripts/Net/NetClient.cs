using System;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
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

    private void PumpIncoming()
    {
        while (_peer.GetAvailablePacketCount() > 0)
        {
            var frame = _peer.GetPacket();
            if (!FrameCodec.TryReadOpcode(frame, out var opcode))
            {
                GD.PushWarning($"Frame de {frame.Length} B sin cabecera; descartado.");
                continue;
            }

            Dispatch(opcode, frame);
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

    private void Send<T>(Opcode opcode, T payload)
    {
        var error = _peer.Send(FrameCodec.Encode(opcode, payload), WebSocketPeer.WriteMode.Binary);
        if (error != Error.Ok)
        {
            GD.PushError($"Error al enviar {opcode}: {error}");
        }
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

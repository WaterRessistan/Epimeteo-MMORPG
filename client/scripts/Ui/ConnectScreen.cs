using Epimeteo.Client.Net;
using Epimeteo.Shared.Net;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>
/// Pantalla de conexión de la Fase 1: conecta al arrancar, muestra el estado del handshake y el
/// RTT, y permite reintentar con Enter. Es la única interfaz del juego hasta la Fase 2.
/// </summary>
public partial class ConnectScreen : Control
{
    private NetClient _net = null!;
    private Label _statusLabel = null!;
    private Label _rttLabel = null!;
    private Label _serverLabel = null!;
    private Label _hintLabel = null!;
    private string _serverUrl = ClientBuildInfo.DefaultServerUrl;

    /// <inheritdoc />
    public override void _Ready()
    {
        _net = GetNode<NetClient>("NetClient");
        _statusLabel = GetNode<Label>("Layout/Status");
        _rttLabel = GetNode<Label>("Layout/Rtt");
        _serverLabel = GetNode<Label>("Layout/Server");
        _hintLabel = GetNode<Label>("Layout/Hint");

        _serverUrl = ClientBuildInfo.ResolveServerUrl();
        _serverLabel.Text = _serverUrl;

        _net.StatusChanged += OnStatusChanged;
        _net.RttMeasured += OnRttMeasured;
        _net.Kicked += OnKicked;

        _net.ConnectTo(_serverUrl);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _net.StatusChanged -= OnStatusChanged;
        _net.RttMeasured -= OnRttMeasured;
        _net.Kicked -= OnKicked;
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_accept") && _net.Status is ConnectionStatus.Disconnected
            or ConnectionStatus.Failed or ConnectionStatus.Kicked)
        {
            _rttLabel.Text = "RTT --";
            _net.ConnectTo(_serverUrl);
        }
    }

    private void OnStatusChanged(ConnectionStatus status)
    {
        _statusLabel.Text = status switch
        {
            ConnectionStatus.Connecting => "Conectando...",
            ConnectionStatus.Greeting => "Saludando...",
            ConnectionStatus.Connected => "Conectado",
            ConnectionStatus.Kicked => "Rechazado por el servidor",
            ConnectionStatus.Failed => "No se pudo conectar",
            _ => "Desconectado",
        };

        _statusLabel.Modulate = status switch
        {
            ConnectionStatus.Connected => Colors.PaleGreen,
            ConnectionStatus.Kicked or ConnectionStatus.Failed => Colors.Salmon,
            _ => Colors.LightGoldenrod,
        };

        _hintLabel.Visible = status is ConnectionStatus.Disconnected or ConnectionStatus.Failed
            or ConnectionStatus.Kicked;

        if (status == ConnectionStatus.Connected && _net.ServerInfo is { } info)
        {
            _serverLabel.Text = $"{_serverUrl}  ·  v{info.ServerProtocolVersion}  ·  {info.TickRate}/{info.SnapshotRate} Hz";
        }
    }

    private void OnRttMeasured(long rttMs)
        => _rttLabel.Text = $"RTT {rttMs} ms  (media {_net.AverageRttMs:F0} ms)";

    private void OnKicked(KickReason reason, ResultCode detail, int serverProtocolVersion)
    {
        _rttLabel.Text = reason switch
        {
            KickReason.VersionMismatch =>
                $"Actualiza el juego: el servidor usa el protocolo v{serverProtocolVersion}, tú la v{ProtocolVersion.Current}",
            KickReason.RateLimited => "Demasiados mensajes enviados",
            KickReason.Timeout => "Sin respuesta del cliente",
            KickReason.ServerShutdown => "El servidor se está apagando",
            KickReason.Banned => "Cuenta expulsada",
            KickReason.LoggedInElsewhere => "Sesión abierta desde otro sitio",
            KickReason.InvalidState or KickReason.ProtocolError => $"Error de protocolo ({detail})",
            _ => $"Desconectado por el servidor ({reason})",
        };
    }
}

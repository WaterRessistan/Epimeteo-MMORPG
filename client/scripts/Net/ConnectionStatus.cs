namespace Epimeteo.Client.Net;

/// <summary>Estado de la conexión tal como lo ve la interfaz del cliente.</summary>
public enum ConnectionStatus
{
    /// <summary>Sin conexión y sin intentarlo.</summary>
    Disconnected,

    /// <summary>Abriendo el WebSocket.</summary>
    Connecting,

    /// <summary>WebSocket abierto, <c>Hello</c> enviado, esperando <c>HelloAck</c>.</summary>
    Greeting,

    /// <summary>Handshake completo: el servidor respondió.</summary>
    Connected,

    /// <summary>El servidor cerró la sesión con un <c>Kick</c>.</summary>
    Kicked,

    /// <summary>No se pudo conectar o se cortó antes de completar el handshake.</summary>
    Failed,
}

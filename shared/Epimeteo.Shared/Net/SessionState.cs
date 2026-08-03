namespace Epimeteo.Shared.Net;

/// <summary>
/// Estado de la máquina de sesión:
/// <code>
/// Connecting ──Hello──▶ Greeted ──Login/Register──▶ Authenticated
///     ──CharSelect──▶ Loading ──WorldReady──▶ InWorld
/// </code>
/// Es un enum de flags porque el mismo tipo sirve para el estado actual (siempre un único bit)
/// y para la máscara de estados legales de cada opcode en <see cref="OpcodeTable"/>.
/// </summary>
[Flags]
public enum SessionState : byte
{
    None = 0,

    /// <summary>WebSocket abierto, todavía sin <c>Hello</c>.</summary>
    Connecting = 1 << 0,

    /// <summary>Versión de protocolo validada; falta autenticar.</summary>
    Greeted = 1 << 1,

    /// <summary>Cuenta autenticada; el jugador está en la selección de personaje.</summary>
    Authenticated = 1 << 2,

    /// <summary>Personaje elegido; el cliente está cargando el mapa.</summary>
    Loading = 1 << 3,

    /// <summary>Jugando.</summary>
    InWorld = 1 << 4,

    /// <summary>Cierre en curso: no se acepta ni se procesa nada más.</summary>
    Closing = 1 << 5,

    /// <summary>Máscara: cualquier estado en el que la sesión sigue viva.</summary>
    Any = Connecting | Greeted | Authenticated | Loading | InWorld,
}

namespace Epimeteo.Shared.Net;

/// <summary>
/// Motivo por el que el servidor cierra una sesión. Se manda en <see cref="Messages.S2CKick"/>
/// justo antes de cerrar el WebSocket. Igual que <see cref="ResultCode"/>, no lleva texto.
/// </summary>
public enum KickReason : ushort
{
    Unknown = 0,

    /// <summary>La versión de protocolo del cliente no coincide con la del servidor.</summary>
    VersionMismatch = 1,

    /// <summary>Opcode desconocido, payload ilegible, frame de texto o frame demasiado grande.</summary>
    ProtocolError = 2,

    /// <summary>Mensaje legal pero recibido en un estado de sesión que no lo permite.</summary>
    InvalidState = 3,

    /// <summary>Superó el rate limit repetidamente.</summary>
    RateLimited = 4,

    /// <summary>30 s sin tráfico entrante, o <c>Hello</c> que no llegó a tiempo.</summary>
    Timeout = 5,

    /// <summary>La misma cuenta se conectó desde otro sitio.</summary>
    LoggedInElsewhere = 6,

    /// <summary>Expulsión o baneo por un administrador.</summary>
    Banned = 7,

    /// <summary>El servidor se está apagando.</summary>
    ServerShutdown = 8,

    /// <summary>Fallo interno del servidor al procesar la sesión.</summary>
    InternalError = 9,
}

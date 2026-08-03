namespace Epimeteo.Shared.Net;

/// <summary>
/// Agrupación de opcodes usada por el rate limiter: cada familia tiene su propio token bucket
/// por sesión. Ver <c>docs/01-protocolo.md § Rate limiting</c>.
/// </summary>
public enum OpcodeFamily : byte
{
    /// <summary>Handshake y keep-alive: Hello, Ping.</summary>
    Session = 0,

    /// <summary>Login y registro. Su límite real es por IP, no por sesión.</summary>
    Auth = 1,

    /// <summary>Listar, crear, borrar y seleccionar personaje.</summary>
    Character = 2,

    /// <summary>InputState e interacción con el mundo.</summary>
    Movement = 3,

    /// <summary>Inventario y equipamiento.</summary>
    Inventory = 4,

    /// <summary>Tiendas y armero.</summary>
    Shop = 5,

    /// <summary>Granja y cultivos.</summary>
    Farm = 6,

    /// <summary>Ataques y habilidades.</summary>
    Combat = 7,

    /// <summary>Chat.</summary>
    Chat = 8,

    /// <summary>Mensajes que sólo emite el servidor; no se aplica rate limit de entrada.</summary>
    ServerOnly = 9,
}

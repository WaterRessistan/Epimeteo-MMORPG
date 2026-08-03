namespace Epimeteo.Shared.Net;

/// <summary>
/// Versión del protocolo de red. Se incrementa **a mano** cada vez que cambia la forma de un
/// mensaje existente (añadir un opcode nuevo no la cambia: un cliente viejo simplemente no lo usa).
/// Cliente y servidor la comparan en el handshake; si no coincide, el servidor manda
/// <see cref="Messages.S2CKick"/> con <see cref="KickReason.VersionMismatch"/>.
/// </summary>
public static class ProtocolVersion
{
    /// <summary>Versión actual del protocolo.</summary>
    public const int Current = 1;
}

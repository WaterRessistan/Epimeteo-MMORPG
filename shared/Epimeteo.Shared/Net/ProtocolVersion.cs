namespace Epimeteo.Shared.Net;

/// <summary>
/// Versión del protocolo de red. Se incrementa **a mano** cada vez que cambia la forma de un
/// mensaje existente (añadir un opcode nuevo no la cambia: un cliente viejo simplemente no lo usa).
/// Cliente y servidor la comparan en el handshake; si no coincide, el servidor manda
/// <see cref="Messages.S2CKick"/> con <see cref="KickReason.VersionMismatch"/>.
/// </summary>
public static class ProtocolVersion
{
    /// <summary>
    /// Versión actual del protocolo.
    /// <list type="bullet">
    /// <item>1 — Fases 1–3: handshake, autenticación y personajes.</item>
    /// <item>2 — Fase 4: <c>WorldEnter</c> gana <c>MapHash</c> y su <c>MyEntityId</c> pasa a ser
    /// el id de entidad del mundo (era <c>CharacterId</c> provisional).</item>
    /// </list>
    /// </summary>
    public const int Current = 2;
}

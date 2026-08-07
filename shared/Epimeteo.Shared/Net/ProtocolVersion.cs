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
    /// <item>3 — Fase 9: <c>C2SPing</c> gana <c>LastServerTimeMs</c>, para que el <b>servidor</b>
    /// pueda medir el RTT él mismo en vez de creerse el del cliente — la compensación de latencia
    /// del PvP decide a quién alcanza un golpe, así que no puede depender de un número que manda
    /// el cliente (FASE-09 §2 D1). Las Fases 6, 7 y 8 no subieron la versión porque sólo añadieron
    /// opcodes y mensajes nuevos; ésta sí cambia la forma de uno que ya existía.</item>
    /// </list>
    /// </summary>
    public const int Current = 3;
}

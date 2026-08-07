using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Keep-alive a 1 Hz (opcode 0x0004, legal en cualquier estado). También sirve para medir el RTT
/// y para renovar el timeout de inactividad de la sesión.
/// </summary>
[MessagePackObject]
public sealed record C2SPing
{
    /// <summary>
    /// Sello del reloj monotónico del cliente. El servidor lo devuelve tal cual en
    /// <see cref="S2CPong"/>; el cliente calcula el RTT restando, sin sincronizar relojes.
    /// </summary>
    [Key(0)]
    public long ClientTimeMs { get; init; }

    /// <summary>
    /// Eco del último <see cref="S2CPong.ServerTimeMs"/> que vio el cliente, o <c>0</c> si todavía
    /// no ha recibido ninguno. Es lo que permite al <b>servidor</b> medir el RTT sin creerse un
    /// número calculado por el cliente (FASE-09 §2 D1): el sello lo originó el propio servidor y
    /// sólo tiene que ver cuánto tarda en volver.
    /// <para>
    /// Un cliente parcheado puede devolver un sello viejo para inflar su RTT. No se le cree sin
    /// más: el rebobinado se clampa a <c>CombatConstants.MaxRewindMs</c>, así que mentir no da
    /// nada que no tenga ya cualquiera con mala conexión.
    /// </para>
    /// </summary>
    [Key(1)]
    public long LastServerTimeMs { get; init; }
}

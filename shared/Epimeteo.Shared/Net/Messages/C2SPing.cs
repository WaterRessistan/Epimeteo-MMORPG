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
}

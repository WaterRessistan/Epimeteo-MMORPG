using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Primer mensaje de toda conexión (opcode 0x0001, estado <see cref="SessionState.Connecting"/>).
/// Si no llega en los primeros segundos, el servidor cierra la sesión.
/// </summary>
[MessagePackObject]
public sealed record C2SHello
{
    /// <summary>Versión de protocolo del cliente. Debe coincidir exactamente con la del servidor.</summary>
    [Key(0)]
    public int ProtocolVersion { get; init; }

    /// <summary>
    /// Identificador de build del cliente, sólo informativo (logs y diagnóstico).
    /// Nullable a propósito: un frame puede omitir el campo, y un inicializador por defecto
    /// aquí sería mentira (MessagePack lo pisaría con null al deserializar).
    /// </summary>
    [Key(1)]
    public string? ClientBuild { get; init; }
}

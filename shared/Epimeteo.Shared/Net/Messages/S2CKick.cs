using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Último mensaje de una sesión (opcode 0x8005): el servidor explica por qué cierra y acto seguido
/// cierra el WebSocket. Nunca lleva texto; el cliente traduce el motivo.
/// </summary>
[MessagePackObject]
public sealed record S2CKick
{
    /// <summary>Motivo de la desconexión.</summary>
    [Key(0)]
    public KickReason Reason { get; init; }

    /// <summary>Detalle opcional cuando el motivo admite matices. <c>Ok</c> si no aplica.</summary>
    [Key(1)]
    public ResultCode Detail { get; init; }

    /// <summary>
    /// Versión de protocolo que espera el servidor. Sólo tiene sentido con
    /// <see cref="KickReason.VersionMismatch"/>, para que el cliente pueda decir
    /// "actualiza el juego" con datos concretos.
    /// </summary>
    [Key(2)]
    public int ServerProtocolVersion { get; init; }
}

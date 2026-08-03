using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Respuesta al <see cref="C2SHello"/> (opcode 0x8001). Lleva los ritmos del servidor para que
/// el cliente configure su buffer de interpolación y su cadencia de envío de input.
/// </summary>
[MessagePackObject]
public sealed record S2CHelloAck
{
    /// <summary>Versión de protocolo del servidor.</summary>
    [Key(0)]
    public int ServerProtocolVersion { get; init; }

    /// <summary>Ticks de simulación por segundo (20).</summary>
    [Key(1)]
    public int TickRate { get; init; }

    /// <summary>Snapshots enviados por segundo (10).</summary>
    [Key(2)]
    public int SnapshotRate { get; init; }

    /// <summary>Reloj monotónico del servidor en el momento de responder, en ms.</summary>
    [Key(3)]
    public long ServerTimeMs { get; init; }
}

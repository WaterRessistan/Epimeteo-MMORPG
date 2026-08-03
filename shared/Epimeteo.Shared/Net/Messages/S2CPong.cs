using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Respuesta a <see cref="C2SPing"/> (opcode 0x8004).</summary>
[MessagePackObject]
public sealed record S2CPong
{
    /// <summary>Eco exacto del sello que mandó el cliente.</summary>
    [Key(0)]
    public long ClientTimeMs { get; init; }

    /// <summary>Reloj monotónico del servidor al responder, en ms.</summary>
    [Key(1)]
    public long ServerTimeMs { get; init; }
}

using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Respuesta a <see cref="C2SCharListRequest"/> (opcode 0x8010).</summary>
[MessagePackObject]
public sealed record S2CCharList
{
    [Key(0)]
    public required CharacterSummary[] Characters { get; init; }
}

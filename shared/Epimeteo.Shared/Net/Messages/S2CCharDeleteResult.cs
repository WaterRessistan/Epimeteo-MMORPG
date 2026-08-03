using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Respuesta a <see cref="C2SCharDelete"/> (opcode 0x8012).</summary>
[MessagePackObject]
public sealed record S2CCharDeleteResult
{
    [Key(0)]
    public bool Ok { get; init; }

    [Key(1)]
    public ResultCode Code { get; init; }

    [Key(2)]
    public long CharacterId { get; init; }
}

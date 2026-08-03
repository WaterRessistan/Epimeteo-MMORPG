using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Elige un personaje propio para entrar al mundo (opcode 0x0013, estado <see cref="SessionState.Authenticated"/>).</summary>
[MessagePackObject]
public sealed record C2SCharSelect
{
    [Key(0)]
    public required long CharacterId { get; init; }
}

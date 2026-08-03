using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Pide la lista de personajes de la cuenta (opcode 0x0010, estado <see cref="SessionState.Authenticated"/>).</summary>
[MessagePackObject]
public sealed record C2SCharListRequest;

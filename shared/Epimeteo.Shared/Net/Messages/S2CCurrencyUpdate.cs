using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Oro actual, valor absoluto — nunca un delta (<c>docs/01</c>). Primera vez que se tipa: el
/// opcode está reservado desde la Fase 1 sin usarse hasta que hubo algo que gastar (FASE-07).
/// </summary>
[MessagePackObject]
public sealed record S2CCurrencyUpdate
{
    [Key(0)]
    public required long Gold { get; init; }
}

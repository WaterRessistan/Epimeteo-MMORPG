using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Una entidad murió (opcode 0x8061). El <c>EntityDespawn</c> llega aparte, cuando toque.</summary>
[MessagePackObject]
public sealed record S2CEntityDeath
{
    [Key(0)]
    public required int Id { get; init; }

    /// <summary>Quién la mató, o <c>0</c> si no fue nadie (todavía no hay muertes por entorno).</summary>
    [Key(1)]
    public required int KillerId { get; init; }
}

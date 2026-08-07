using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Atacar a una entidad. El cliente manda <b>a quién</b> quiere pegar y nada más: ni daño, ni si
/// acierta, ni si está en alcance (CLAUDE.md §4 — sólo intenciones). Todo eso lo decide el
/// servidor, que valida zona, alcance, cooldown y línea de visión (FASE-09 §2 D3).
/// </summary>
[MessagePackObject]
public sealed record C2SAttack
{
    [Key(0)]
    public required int TargetEntityId { get; init; }
}

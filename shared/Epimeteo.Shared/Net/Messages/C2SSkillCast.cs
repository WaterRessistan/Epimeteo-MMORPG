using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Lanzar una habilidad. Igual que <c>Attack</c> (Fase 9), el cliente sólo manda a quién apunta y
/// con qué habilidad — maná, cooldown, alcance y si la desbloqueó los valida el servidor
/// (CLAUDE.md §4). En una curación el objetivo se ignora: siempre cura a quien la lanza
/// (FASE-10 §2 D9).
/// </summary>
[MessagePackObject]
public sealed record C2SSkillCast
{
    [Key(0)]
    public required string SkillKey { get; init; }

    [Key(1)]
    public required int TargetEntityId { get; init; }
}

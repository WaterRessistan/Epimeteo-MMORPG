using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Un golpe resuelto, tal como lo cuenta el servidor (opcode 0x8060). Se manda a todo el que tenga
/// a la víctima en su área de interés, no sólo a los dos implicados: el resto necesita ver los
/// números para entender qué está pasando.
/// </summary>
[MessagePackObject]
public sealed record S2CCombatEvent
{
    [Key(0)]
    public required int AttackerId { get; init; }

    [Key(1)]
    public required int TargetId { get; init; }

    [Key(2)]
    public required CombatEventKind Kind { get; init; }

    [Key(3)]
    public required int Amount { get; init; }

    [Key(4)]
    public required CombatEventFlags Flags { get; init; }

    /// <summary>Clave de habilidad, o <c>null</c> si fue un ataque básico. Las habilidades son la Fase 10.</summary>
    [Key(5)]
    public string? SkillKey { get; init; }
}

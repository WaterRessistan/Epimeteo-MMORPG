using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Vida y maná de una entidad (opcode 0x8023, reservado desde la Fase 1 y tipado por fin aquí:
/// hasta la Fase 9 nada cambiaba la vida de nadie). Se manda a quien la tenga en su área de
/// interés cada vez que cambia.
/// <para>
/// Los buffs que menciona <c>docs/01</c> no viajan todavía: no existe ninguno (serían de la
/// Fase 10). Mismo criterio que el resto de campos reservados sin usar.
/// </para>
/// </summary>
[MessagePackObject]
public sealed record S2CEntityStats
{
    [Key(0)]
    public required int Id { get; init; }

    [Key(1)]
    public required int Hp { get; init; }

    [Key(2)]
    public required int HpMax { get; init; }

    [Key(3)]
    public required int Mp { get; init; }

    [Key(4)]
    public required int MpMax { get; init; }

    [Key(5)]
    public required int Level { get; init; }
}

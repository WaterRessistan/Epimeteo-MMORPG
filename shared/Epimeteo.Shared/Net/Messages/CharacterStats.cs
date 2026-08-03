using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Stats completos de un personaje, tal como se guardan en la fila (sin derivados de equipo:
/// eso llega en la Fase 6). Tipo compartido, viaja dentro de <see cref="S2CWorldEnter"/>.
/// </summary>
[MessagePackObject]
public sealed record CharacterStats
{
    [Key(0)]
    public required int Level { get; init; }

    [Key(1)]
    public required long Xp { get; init; }

    [Key(2)]
    public required int Str { get; init; }

    [Key(3)]
    public required int Int { get; init; }

    [Key(4)]
    public required int Vit { get; init; }

    [Key(5)]
    public required int Dex { get; init; }

    [Key(6)]
    public required int StatPoints { get; init; }

    [Key(7)]
    public required int Hp { get; init; }

    [Key(8)]
    public required int Mp { get; init; }

    [Key(9)]
    public required long Gold { get; init; }
}

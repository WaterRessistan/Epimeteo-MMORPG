using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// XP tras matar algo o tras morir en PvP (opcode 0x8063). En la Fase 9 la XP se mueve pero el
/// nivel no sube: la curva de progresión es la Fase 10, así que <see cref="XpToNextLevel"/> viaja
/// a 0 y <see cref="LeveledUp"/> siempre falso hasta entonces.
/// </summary>
[MessagePackObject]
public sealed record S2CXpUpdate
{
    [Key(0)]
    public required long Xp { get; init; }

    [Key(1)]
    public required long XpToNextLevel { get; init; }

    [Key(2)]
    public required int Level { get; init; }

    [Key(3)]
    public required bool LeveledUp { get; init; }
}

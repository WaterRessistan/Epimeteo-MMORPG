using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Flag de combate PvP (opcode 0x8025, reservado desde la Fase 1). Mientras esté puesto, salir del
/// juego no saca al personaje del mundo: se queda vivo y atacable hasta que expire
/// (<c>docs/00 §6.2</c>, FASE-09 §2 D11). El cliente lo usa sólo para avisar; quien lo aplica es
/// el servidor.
/// </summary>
[MessagePackObject]
public sealed record S2CCombatFlagUpdate
{
    [Key(0)]
    public required bool InCombat { get; init; }

    [Key(1)]
    public required int MsRemaining { get; init; }
}

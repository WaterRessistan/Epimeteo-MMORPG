namespace Epimeteo.Server.Persistence.Combat;

/// <summary>
/// Una muerte en PvP, camino de <c>combat_log</c>. Sólo PvP: <c>docs/02</c> es explícito en que
/// las muertes contra monstruos no se guardan ("demasiado volumen, poco valor").
/// </summary>
public readonly record struct CombatLogSave(
    long VictimId,
    long? KillerId,
    string MapKey,
    string? Region,
    int VictimLevel,
    int KillerLevel,
    long XpLost);

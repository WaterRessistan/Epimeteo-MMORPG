namespace Epimeteo.Server.Persistence.Admin;

/// <summary>
/// Una acción de administrador, camino de <c>admin_action_log</c>. Todas las de esta fase exigen
/// el objetivo conectado (FASE-11 §2 D5), así que siempre hay <see cref="TargetCharacterId"/>.
/// <see cref="BanHours"/> sólo se usa con <see cref="AdminAction.Ban"/> — es lo que
/// <c>AdminActionSaver</c> necesita para el <c>UPDATE accounts</c> real (FASE-11 §2 D7).
/// <see cref="DefKey"/>/<see cref="Quantity"/> sólo con <see cref="AdminAction.Give"/>.
/// </summary>
public readonly record struct AdminActionSave(
    long AdminCharacterId,
    string AdminName,
    long TargetCharacterId,
    string TargetName,
    AdminAction Action,
    string Reason,
    int? BanHours = null,
    string? DefKey = null,
    int? Quantity = null);

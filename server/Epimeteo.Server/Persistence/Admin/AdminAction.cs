namespace Epimeteo.Server.Persistence.Admin;

/// <summary>Qué hizo un administrador, para <c>admin_action_log</c> (FASE-11 §2 D7).</summary>
public enum AdminAction : byte
{
    Kick = 0,
    Ban = 1,
    Teleport = 2,
    Give = 3,
}

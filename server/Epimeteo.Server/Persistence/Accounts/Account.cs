namespace Epimeteo.Server.Persistence.Accounts;

/// <summary>Proyección mínima de la fila de <c>accounts</c> que necesita el flujo de login.</summary>
public sealed record Account
{
    public required long Id { get; init; }
    public required string Username { get; init; }
    public required string PasswordHash { get; init; }

    /// <summary>0 activa, 1 suspendida, 2 baneada, 3 borrada — ver <c>docs/02-esquema-bd.md</c>.</summary>
    public required short Status { get; init; }

    public DateTimeOffset? BannedUntil { get; init; }
}

/// <summary>Valores de <c>accounts.status</c> — ver <c>docs/02-esquema-bd.md</c>.</summary>
internal static class AccountStatus
{
    public const short Active = 0;
    public const short Suspended = 1;
    public const short Banned = 2;
    public const short Deleted = 3;
}

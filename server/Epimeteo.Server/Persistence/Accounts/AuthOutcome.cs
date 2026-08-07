using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Persistence.Accounts;

/// <summary>Resultado de <see cref="AuthService.LoginAsync"/> / <see cref="AuthService.RegisterAsync"/>.</summary>
public sealed record AuthOutcome(bool Ok, ResultCode Code, long AccountId = 0, string? SessionToken = null, bool IsAdmin = false)
{
    public static AuthOutcome Success(long accountId, string token, bool isAdmin = false) =>
        new(true, ResultCode.Ok, accountId, token, isAdmin);

    public static AuthOutcome Fail(ResultCode code) => new(false, code);
}

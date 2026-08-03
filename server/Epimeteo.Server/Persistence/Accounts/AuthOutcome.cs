using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Persistence.Accounts;

/// <summary>Resultado de <see cref="AuthService.LoginAsync"/> / <see cref="AuthService.RegisterAsync"/>.</summary>
public sealed record AuthOutcome(bool Ok, ResultCode Code, long AccountId = 0, string? SessionToken = null)
{
    public static AuthOutcome Success(long accountId, string token) => new(true, ResultCode.Ok, accountId, token);

    public static AuthOutcome Fail(ResultCode code) => new(false, code);
}

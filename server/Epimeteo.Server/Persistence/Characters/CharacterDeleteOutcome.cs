using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Persistence.Characters;

/// <summary>Resultado de <see cref="CharacterService.DeleteAsync"/>.</summary>
public sealed record CharacterDeleteOutcome(bool Ok, ResultCode Code)
{
    public static readonly CharacterDeleteOutcome Success = new(true, ResultCode.Ok);

    public static CharacterDeleteOutcome Fail(ResultCode code) => new(false, code);
}

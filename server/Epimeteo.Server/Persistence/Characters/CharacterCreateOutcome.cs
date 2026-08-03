using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;

namespace Epimeteo.Server.Persistence.Characters;

/// <summary>Resultado de <see cref="CharacterService.CreateAsync"/>.</summary>
public sealed record CharacterCreateOutcome(bool Ok, ResultCode Code, CharacterSummary? Summary = null)
{
    public static CharacterCreateOutcome Success(CharacterSummary summary) => new(true, ResultCode.Ok, summary);

    public static CharacterCreateOutcome Fail(ResultCode code) => new(false, code);
}

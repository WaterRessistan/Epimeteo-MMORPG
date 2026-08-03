using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Persistence.Characters;

/// <summary>Resultado de <see cref="CharacterService.SelectAsync"/>.</summary>
public sealed record CharacterSelectOutcome(bool Ok, ResultCode Code, Character? Character = null)
{
    public static CharacterSelectOutcome Success(Character character) => new(true, ResultCode.Ok, character);

    public static CharacterSelectOutcome Fail(ResultCode code) => new(false, code);
}

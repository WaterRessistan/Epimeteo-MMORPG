using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Respuesta a <see cref="C2SLogin"/> o <see cref="C2SRegister"/> (opcode 0x8002). El servidor
/// nunca manda texto de error: <see cref="Code"/> es el único dato que explica un fallo.
/// </summary>
[MessagePackObject]
public sealed record S2CAuthResult
{
    [Key(0)]
    public bool Ok { get; init; }

    [Key(1)]
    public ResultCode Code { get; init; }

    /// <summary>0 si <see cref="Ok"/> es falso.</summary>
    [Key(2)]
    public long AccountId { get; init; }

    /// <summary>
    /// Token de sesión en claro, sólo cuando <see cref="Ok"/> es verdadero. El servidor guarda
    /// únicamente su SHA-256; este es el único momento en que el token viaja legible.
    /// </summary>
    [Key(3)]
    public string? SessionToken { get; init; }
}

using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Respuesta a <see cref="C2SCharCreate"/> (opcode 0x8011). Incluye el resumen ya creado para
/// que el cliente actualice la lista sin un segundo <see cref="C2SCharListRequest"/>.
/// </summary>
[MessagePackObject]
public sealed record S2CCharCreateResult
{
    [Key(0)]
    public bool Ok { get; init; }

    [Key(1)]
    public ResultCode Code { get; init; }

    /// <summary><c>null</c> si <see cref="Ok"/> es falso.</summary>
    [Key(2)]
    public CharacterSummary? Character { get; init; }
}

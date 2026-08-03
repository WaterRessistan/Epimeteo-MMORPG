using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Borra (lógicamente) un personaje propio (opcode 0x0012, estado <see cref="SessionState.Authenticated"/>).
/// <see cref="Confirm"/> es la confirmación de verdad en Godot (un diálogo), no un segundo
/// secreto: el servidor la exige como dato pero la propiedad ya la valida por <c>AccountId</c>.
/// </summary>
[MessagePackObject]
public sealed record C2SCharDelete
{
    [Key(0)]
    public required long CharacterId { get; init; }

    [Key(1)]
    public required bool Confirm { get; init; }
}

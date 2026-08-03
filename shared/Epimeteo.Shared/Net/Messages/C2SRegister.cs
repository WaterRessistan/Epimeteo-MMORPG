using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Petición de registro (opcode 0x0003, estado <see cref="SessionState.Greeted"/>).
/// Mismas reglas que <see cref="C2SLogin"/> sobre <see cref="Password"/>: en claro, nunca
/// hasheada en cliente.
/// </summary>
[MessagePackObject]
public sealed record C2SRegister
{
    [Key(0)]
    public required string Username { get; init; }

    /// <summary>Opcional: un frame puede omitirlo, y un valor por defecto aquí sería mentira.</summary>
    [Key(1)]
    public string? Email { get; init; }

    [Key(2)]
    public required string Password { get; init; }
}

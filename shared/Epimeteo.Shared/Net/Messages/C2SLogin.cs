using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Petición de login (opcode 0x0002, estado <see cref="SessionState.Greeted"/>).
/// <see cref="Password"/> viaja en claro dentro del WebSocket cifrado: nunca se hashea en el
/// cliente, porque hashear en cliente convertiría ese hash en la contraseña real de cara al
/// servidor y fijaría para siempre los parámetros de Argon2 en el protocolo.
/// </summary>
[MessagePackObject]
public sealed record C2SLogin
{
    [Key(0)]
    public required string Username { get; init; }

    [Key(1)]
    public required string Password { get; init; }
}

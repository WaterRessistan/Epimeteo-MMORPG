using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Crea un personaje nuevo (opcode 0x0011, estado <see cref="SessionState.Authenticated"/>).
/// <see cref="PaletteIndex"/> es la única "apariencia" hasta que haya assets reales
/// (docs/fases/FASE-03-personajes.md §4): un índice 0-3 que el cliente pinta como un color
/// placeholder, no un sprite.
/// </summary>
[MessagePackObject]
public sealed record C2SCharCreate
{
    [Key(0)]
    public required string Name { get; init; }

    [Key(1)]
    public required string ClassKey { get; init; }

    [Key(2)]
    public required int Slot { get; init; }

    [Key(3)]
    public required byte PaletteIndex { get; init; }
}

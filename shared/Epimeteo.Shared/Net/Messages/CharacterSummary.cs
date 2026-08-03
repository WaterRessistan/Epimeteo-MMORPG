using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Resumen de un personaje para la pantalla de selección. Tipo compartido, no un mensaje: viaja
/// dentro de <see cref="S2CCharList"/> y <see cref="S2CCharCreateResult"/>.
/// </summary>
[MessagePackObject]
public sealed record CharacterSummary
{
    [Key(0)]
    public required long Id { get; init; }

    [Key(1)]
    public required int Slot { get; init; }

    [Key(2)]
    public required string Name { get; init; }

    [Key(3)]
    public required string ClassKey { get; init; }

    [Key(4)]
    public required int Level { get; init; }

    [Key(5)]
    public required string MapKey { get; init; }

    [Key(6)]
    public required byte PaletteIndex { get; init; }
}

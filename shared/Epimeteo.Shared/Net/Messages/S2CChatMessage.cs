using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Un mensaje de chat retransmitido. A diferencia de <see cref="S2CSystemMessage"/>, <see cref="Text"/>
/// es texto literal de un jugador, no una clave i18n — no hay nada que traducir en lo que alguien
/// escribió (FASE-11 §2 D1).
/// </summary>
[MessagePackObject]
public sealed record S2CChatMessage
{
    [Key(0)]
    public required ChatChannel Channel { get; init; }

    [Key(1)]
    public required string SenderName { get; init; }

    [Key(2)]
    public required string Text { get; init; }

    [Key(3)]
    public required long ServerTimeMs { get; init; }
}

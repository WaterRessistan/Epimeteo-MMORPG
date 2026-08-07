using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Un mensaje de chat, o un comando de barra si <see cref="Text"/> empieza por <c>/</c>
/// (FASE-11 §2 D1/D3). El servidor decide qué es; el cliente sólo manda texto y el canal elegido
/// para un mensaje normal — <see cref="Channel"/> se ignora en cuanto el texto es un comando.
/// </summary>
[MessagePackObject]
public sealed record C2SChatSend
{
    [Key(0)]
    public required ChatChannel Channel { get; init; }

    [Key(1)]
    public required string Text { get; init; }
}

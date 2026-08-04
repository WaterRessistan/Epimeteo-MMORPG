using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Aviso sin opcode dedicado propio. Primera vez que se tipa (opcode reservado desde la Fase 1,
/// sin uso hasta esta fase): una mutación de inventario inválida no tiene un <c>InvResult</c>
/// —a diferencia de <c>ShopResult</c>— porque un cliente honesto no debería intentarlo nunca (ya
/// valida con el mismo <c>ItemCatalog</c>); esto es UX, no protocolo de confirmación.
/// </summary>
/// <param name="Severity">0 info, 1 aviso, 2 error. El cliente decide cómo pintarlo.</param>
/// <param name="Key">Clave i18n; el servidor nunca manda texto de usuario final (<c>docs/01</c>).</param>
/// <param name="Args">Argumentos posicionales para la traducción, si la clave los necesita.</param>
[MessagePackObject]
public sealed record S2CSystemMessage
{
    [Key(0)]
    public required byte Severity { get; init; }

    [Key(1)]
    public required string Key { get; init; }

    [Key(2)]
    public required string[] Args { get; init; }
}

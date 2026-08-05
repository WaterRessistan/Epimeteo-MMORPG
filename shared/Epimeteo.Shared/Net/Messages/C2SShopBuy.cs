using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Comprar. <see cref="ShopSlot"/> es el índice dentro de <c>ShopDefinition.Items</c>
/// (FASE-07 §2 D4). <see cref="ExpectedPrice"/> no es opcional: si no coincide con el precio real
/// del servidor, se rechaza sin más (<c>docs/01</c>).
/// </summary>
[MessagePackObject]
public sealed record C2SShopBuy
{
    [Key(0)]
    public required byte ShopSlot { get; init; }

    [Key(1)]
    public required int Quantity { get; init; }

    /// <summary>Coste total esperado (precio unitario × cantidad), no el precio unitario.</summary>
    [Key(2)]
    public required long ExpectedPrice { get; init; }
}

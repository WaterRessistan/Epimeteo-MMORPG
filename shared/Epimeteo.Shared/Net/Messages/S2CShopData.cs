using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Respuesta a <c>ShopOpen</c> con éxito: la tienda entera, lista para pintar.</summary>
[MessagePackObject]
public sealed record S2CShopData
{
    [Key(0)]
    public required string ShopKey { get; init; }

    [Key(1)]
    public required string DisplayName { get; init; }

    [Key(2)]
    public required bool CanRepair { get; init; }

    /// <summary>El índice de cada entrada es el <c>shopSlot</c> que espera <c>ShopBuy</c>.</summary>
    [Key(3)]
    public required ShopSlotInfo[] Slots { get; init; }
}

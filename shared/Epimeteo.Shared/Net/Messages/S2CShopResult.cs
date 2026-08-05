using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Fallo de <c>ShopOpen</c>/<c>ShopBuy</c>/<c>ShopSell</c>/<c>ShopRepair</c>. A diferencia del
/// inventario (Fase 6, que reutiliza <c>SystemMessage</c>), la tienda sí tiene un opcode de
/// resultado dedicado desde la Fase 1 — se usa tal cual. El éxito no manda esto: se ve en
/// <c>ShopData</c>/<c>InventoryDelta</c>/<c>CurrencyUpdate</c>, según la acción.
/// </summary>
[MessagePackObject]
public sealed record S2CShopResult
{
    [Key(0)]
    public required bool Ok { get; init; }

    [Key(1)]
    public required ResultCode Code { get; init; }
}

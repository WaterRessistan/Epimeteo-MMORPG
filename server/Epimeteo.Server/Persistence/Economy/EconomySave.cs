namespace Epimeteo.Server.Persistence.Economy;

/// <summary>
/// Una fila de <c>economy_log</c> lista para escribirse, con el stock de tienda que le
/// corresponde si la origina una compra/venta (FASE-07 §2 D9). A diferencia de
/// <c>InventorySave</c>/<c>PositionSave</c> (instantáneas que se sustituyen entre sí), cada
/// <c>EconomySave</c> es una fila independiente: perder una no la sustituye la siguiente, es
/// simplemente una fila de auditoría que ya no está. Se acepta el mismo riesgo residual que ya
/// asume el resto de colas de esta cola (<c>EconomySaver</c>) con una capacidad generosa: el
/// volumen de acciones económicas es bajísimo comparado con el de posición (20 Hz), así que en
/// la práctica no se llega ni de lejos a llenarla.
/// </summary>
/// <param name="Kind">Qué tipo de movimiento es. Sin sentido (no se lee) si <paramref name="CharacterId"/> es <c>null</c>.</param>
/// <param name="CharacterId">
/// Quién lo hizo, o <c>null</c> si no lo hizo nadie (la reposición automática de una tienda,
/// FASE-07 §2 D8): entonces no se escribe fila de <c>economy_log</c>, sólo se actualiza el stock.
/// </param>
/// <param name="DefKey">Ítem implicado.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="GoldDelta">Cambio de oro (positivo en venta, negativo en compra/reparación).</param>
/// <param name="GoldAfter">Oro tras el movimiento.</param>
/// <param name="ShopKey">Tienda de origen, o <c>null</c> si no viene de una tienda (p. ej. tirar).</param>
/// <param name="ShopStock">Stock de la tienda tras el movimiento, o <c>null</c> si no aplica (infinito o sin tienda).</param>
/// <param name="ShopStockMax">Stock máximo de esa entrada, para el <c>UPSERT</c> de <c>shop_stock</c>.</param>
/// <param name="ShopRestockAt">Próxima reposición de esa tienda.</param>
public readonly record struct EconomySave(
    EconomyLogKind Kind,
    long? CharacterId,
    string DefKey,
    int Quantity,
    long GoldDelta,
    long GoldAfter,
    string? ShopKey,
    int? ShopStock,
    int? ShopStockMax,
    DateTimeOffset? ShopRestockAt)
{
    /// <summary>Sólo actualiza <c>shop_stock</c>; no hay jugador que loguear (D8: reposición automática).</summary>
    public static EconomySave Restock(string shopKey, string defKey, int stock, int stockMax, DateTimeOffset restockAt) =>
        new(default, null, defKey, 0, 0, 0, shopKey, stock, stockMax, restockAt);
}

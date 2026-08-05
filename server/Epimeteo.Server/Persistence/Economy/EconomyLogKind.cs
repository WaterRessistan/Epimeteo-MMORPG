namespace Epimeteo.Server.Persistence.Economy;

/// <summary>
/// Valores de <c>economy_log.kind</c>, tal como los fijó <c>docs/02</c> (1–9). La columna es un
/// <c>smallint</c> sin CHECK, así que extender la lista no pide migración — mismo criterio que el
/// hueco de <c>ShopRepair</c> en el protocolo (FASE-07-tiendas.md §2 D6): <c>docs/02</c> es de
/// antes de que existieran las tiendas y no contempló reparar, que no es ni compra ni acción de
/// admin.
/// </summary>
public enum EconomyLogKind : short
{
    Buy = 1,
    Sell = 2,
    Loot = 3,
    Drop = 4,
    Harvest = 5,
    Quest = 6,
    Admin = 7,
    Destroy = 8,
    Trade = 9,
    Repair = 10,
}

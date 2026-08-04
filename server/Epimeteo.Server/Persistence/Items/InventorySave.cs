namespace Epimeteo.Server.Persistence.Items;

/// <summary>
/// El inventario completo (contenedores 0–3) de un personaje, listo para volcarse a Postgres.
/// A diferencia de <c>PositionSave</c> (un valor escalar que se sobrescribe), esto es una
/// <b>instantánea completa</b> de una colección: quien la aplica reemplaza el conjunto entero,
/// no aplica un delta (FASE-06 §2 D2). Por eso es seguro perder una instantánea vieja si llega
/// una más nueva: la más nueva ya contiene el resultado de todo lo que pasó antes.
/// </summary>
/// <param name="CharacterId">Fila de <c>characters</c>.</param>
/// <param name="Items">Todo lo que debe quedar en <c>item_instances</c> para este personaje (containers 0–3).</param>
public readonly record struct InventorySave(long CharacterId, IReadOnlyList<ItemStackSnapshot> Items);

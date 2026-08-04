namespace Epimeteo.Shared.Data;

/// <summary>
/// Contenedores de <c>item_instances.container</c> (<c>docs/02 § Ítems</c>). Sólo 0–3 tienen
/// lógica en esta fase; 4–7 están declarados para que su número no cambie cuando lleguen sus
/// fases (banco, tienda, correo, saco de loot — FASE-06 §1 "Fuera de alcance").
/// </summary>
public enum ContainerId : byte
{
    /// <summary>Bolsa general: consumibles, materiales, semillas.</summary>
    General = 0,

    /// <summary>Bolsa de armas, incluidos escudos.</summary>
    WeaponBag = 1,

    /// <summary>Bolsa de armaduras.</summary>
    ArmorBag = 2,

    /// <summary>Equipado. <c>slot</c> es un <see cref="EquipSlot"/>, no una posición de bolsa.</summary>
    Equipped = 3,

    /// <summary>Banco compartido de la cuenta (fase futura).</summary>
    Bank = 4,

    /// <summary>Stock de una tienda (Fase 7).</summary>
    ShopStock = 5,

    /// <summary>Buzón/correo (fase futura).</summary>
    Mailbox = 6,

    /// <summary>Saco de loot en el suelo (Fase 9).</summary>
    LootBag = 7,
}

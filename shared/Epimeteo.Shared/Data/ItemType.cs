namespace Epimeteo.Shared.Data;

/// <summary>
/// Qué clase de ítem es. Decide en qué contenedor no-equipado vive (<c>docs/02 § Ítems</c>,
/// FASE-06 §2 D3): cada valor tiene <b>un</b> contenedor válido fuera de estar equipado.
/// </summary>
public enum ItemType : byte
{
    /// <summary>Va en la bolsa de armas (<see cref="ContainerId.WeaponBag"/>). Incluye escudos.</summary>
    Weapon = 0,

    /// <summary>Va en la bolsa de armaduras (<see cref="ContainerId.ArmorBag"/>).</summary>
    Armor = 1,

    /// <summary>Va en el general (<see cref="ContainerId.General"/>). Se puede usar (<c>InvUse</c>).</summary>
    Consumable = 2,

    /// <summary>Va en el general. Sin uso propio: crafteo/venta (fases futuras).</summary>
    Material = 3,

    /// <summary>Va en el general. Sin lógica de siembra todavía (eso es la Fase 8).</summary>
    Seed = 4,
}

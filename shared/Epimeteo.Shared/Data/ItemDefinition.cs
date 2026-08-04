namespace Epimeteo.Shared.Data;

/// <summary>
/// Forma de <c>content/items/*.json</c> (CLAUDE.md §3: definiciones de contenido en JSON
/// versionado, no en la BD). Vive en <c>Shared</c> —a diferencia de <c>ClassDefinition</c>, que
/// es sólo del servidor— porque el cliente la necesita para tooltips y para saber a qué
/// contenedor le toca un ítem al dibujar el drag & drop (FASE-06 §4). La validación de verdad
/// —qué se puede hacer con un ítem— la hace igual el servidor; esto es sólo para que la UI no
/// tenga que preguntar cada vez.
/// </summary>
public sealed record ItemDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required ItemType Type { get; init; }

    /// <summary>Tamaño máximo de pila en un slot. <c>1</c> = no apilable.</summary>
    public required int MaxStack { get; init; }

    /// <summary>
    /// Sólo presente (no <c>null</c>) si y sólo si <see cref="Type"/> es <see cref="ItemType.Weapon"/>
    /// o <see cref="ItemType.Armor"/>: es lo que valida <see cref="ItemCatalog"/> al cargar.
    /// </summary>
    public EquipCategory? EquipCategory { get; init; }

    public int BonusStr { get; init; }

    public int BonusInt { get; init; }

    public int BonusVit { get; init; }

    public int BonusDex { get; init; }

    public int BonusHp { get; init; }

    public int BonusMp { get; init; }

    /// <summary>Vida que restaura al usarse con <c>InvUse</c>. <c>0</c> = no es consumible de curación.</summary>
    public int HealAmount { get; init; }
}

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

    /// <summary>
    /// Clave que resuelve el <c>AtlasRegistry</c> del cliente para el sprite (FASE-12 §2 D2). Por
    /// defecto es <see cref="Key"/> — la mayoría de ítems no necesitan compartir dibujo con otro—,
    /// pero puede apuntar a uno distinto cuando sí lo necesiten (p. ej. dos espadas provisionales
    /// con el mismo placeholder hasta que haya arte real de cada una).
    /// </summary>
    public required string VisualKey { get; init; }

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

    /// <summary>
    /// Durabilidad máxima de fábrica, o <c>null</c> si el ítem no se desgasta
    /// (<c>item_instances.durability_max</c>, FASE-07 §4). Un stack nuevo de este ítem nace con
    /// <c>Durability = DurabilityMax</c>. Nada reduce durabilidad todavía (sin combate ni
    /// herramientas de granja) — sólo el armero de la Fase 7 la restaura, y sólo se puede probar
    /// manipulando un ítem a mano hasta que exista una fuente real de desgaste.
    /// </summary>
    public int? DurabilityMax { get; init; }

    /// <summary>
    /// Sólo presente si <see cref="EquipCategory"/> es <see cref="Data.EquipCategory.Tool"/>: qué
    /// acción de granja habilita (FASE-08 §2 D4). Con un único hueco de herramienta, sin esto no
    /// hay forma de exigir "la herramienta correcta" para arar frente a regar.
    /// </summary>
    public FarmToolAction? FarmToolAction { get; init; }
}

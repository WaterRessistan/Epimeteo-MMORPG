namespace Epimeteo.Shared.Net;

/// <summary>
/// Identificador de mensaje en el envelope binario.
/// Rango 0x0000–0x7FFF: cliente → servidor. Rango 0x8000–0xFFFF: servidor → cliente.
/// Los valores NO se reutilizan jamás: un opcode retirado se marca [Obsolete] y su número muere.
/// El catálogo completo está declarado desde la Fase 1 aunque la mayoría no tenga aún
/// mensaje ni manejador; así ningún número se asigna dos veces por descuido.
/// </summary>
public enum Opcode : ushort
{
    None = 0x0000,

    // ── Cliente → servidor ───────────────────────────────────────────────
    Hello = 0x0001,
    Login = 0x0002,
    Register = 0x0003,
    Ping = 0x0004,

    CharListRequest = 0x0010,
    CharCreate = 0x0011,
    CharDelete = 0x0012,
    CharSelect = 0x0013,
    WorldReady = 0x0014,

    InputState = 0x0020,
    Interact = 0x0021,

    InvMove = 0x0030,
    InvUse = 0x0031,
    InvDrop = 0x0032,
    Equip = 0x0033,
    Unequip = 0x0034,

    ShopOpen = 0x0040,
    ShopBuy = 0x0041,
    ShopSell = 0x0042,
    ShopClose = 0x0043,

    /// <summary>
    /// Añadido en la Fase 7: el catálogo original de la Fase 1 reservó Open/Buy/Sell/Close pero
    /// no reparar (hueco real, FASE-07-tiendas.md §2 D6). No sube <c>ProtocolVersion</c> — la
    /// regla de <c>docs/01</c> liga la versión a cambiar la forma de un mensaje ya existente, no
    /// a añadir uno nuevo (mismo criterio que los opcodes de inventario de la Fase 6).
    /// </summary>
    ShopRepair = 0x0044,

    FarmPlant = 0x0050,
    FarmWater = 0x0051,
    FarmHarvest = 0x0052,
    FarmTill = 0x0053,

    Attack = 0x0060,
    SkillCast = 0x0061,

    /// <summary>
    /// Añadido en la Fase 9: el catálogo original reservó <c>LootDrop</c> (S2C) y
    /// <c>ContainerId.LootBag</c>, pero ningún C2S para coger nada del saco (hueco real,
    /// FASE-09-combate-pvp.md §2 D9). <c>InvMove</c> no vale: opera entre contenedores del propio
    /// personaje, y un saco es una entidad del mundo que pueden ver varios. Mismo criterio que
    /// <see cref="ShopRepair"/> en la Fase 7.
    /// </summary>
    LootTake = 0x0062,

    /// <summary>
    /// Añadido en la Fase 10: el catálogo original no reservó nada para gastar puntos de stat
    /// (hueco real, FASE-10-progresion.md §2 D5). Familia <see cref="OpcodeFamily.Character"/> —
    /// mismo cupo que crear/borrar personaje, una acción de menú, no de combate— pero estado legal
    /// <c>InWorld</c>: hace falta el personaje cargado para saber su clase y sus stats.
    /// </summary>
    AllocateStatPoint = 0x0063,

    ChatSend = 0x0070,

    // ── Servidor → cliente ───────────────────────────────────────────────
    HelloAck = 0x8001,
    AuthResult = 0x8002,
    Pong = 0x8004,
    Kick = 0x8005,

    CharList = 0x8010,
    CharCreateResult = 0x8011,
    CharDeleteResult = 0x8012,
    WorldEnter = 0x8013,

    EntitySpawn = 0x8020,
    EntityDespawn = 0x8021,
    Snapshot = 0x8022,
    EntityStats = 0x8023,
    ZoneFlagsUpdate = 0x8024,
    CombatFlagUpdate = 0x8025,

    InventoryFull = 0x8030,
    InventoryDelta = 0x8031,
    EquipmentUpdate = 0x8032,
    CurrencyUpdate = 0x8033,

    ShopData = 0x8040,
    ShopResult = 0x8041,

    FarmTileUpdate = 0x8050,

    CombatEvent = 0x8060,
    EntityDeath = 0x8061,
    LootDrop = 0x8062,
    XpUpdate = 0x8063,

    ChatMessage = 0x8070,
    SystemMessage = 0x8071,
}

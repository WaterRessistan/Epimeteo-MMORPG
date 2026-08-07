using Epimeteo.Server.Combat;
using Epimeteo.Server.Inventory;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>
/// Un jugador dentro del mundo: la entidad, su sesión, su cola de inputs, su inventario y lo que
/// sabe ver. Vive sólo en el hilo del tick — nadie más toca estos campos.
/// </summary>
public sealed class PlayerEntity : WorldEntity
{
    public PlayerEntity(
        int id,
        IWorldPeer peer,
        long characterId,
        string classKey,
        string name,
        MoveState state,
        long nowMs,
        IReadOnlyList<ItemStack> items)
        : base(id, EntityType.Player, classKey, name, state)
    {
        Peer = peer;
        CharacterId = characterId;
        Inputs = new InputQueue(nowMs);
        Inventory = new PlayerInventory(items);
    }

    /// <summary>Sesión de red del jugador.</summary>
    public IWorldPeer Peer { get; }

    /// <summary>Fila de <c>characters</c> a la que se guarda la posición.</summary>
    public long CharacterId { get; }

    /// <summary>Cola de inputs pendientes de simular.</summary>
    public InputQueue Inputs { get; }

    /// <summary>Contenedores 0–3, autoritativo mientras dura la sesión (FASE-06 §2 D1).</summary>
    public PlayerInventory Inventory { get; }

    /// <summary>Maná actual.</summary>
    public int Mp { get; set; }

    /// <summary>Maná máximo, con equipo (FASE-06 §2 D5). Se recalcula al equipar/desequipar.</summary>
    public int MpMax { get; set; }

    /// <summary>Fuerza base (sin equipo).</summary>
    public int Str { get; set; }

    /// <summary>Inteligencia base (sin equipo).</summary>
    public int IntStat { get; set; }

    /// <summary>Vitalidad base (sin equipo).</summary>
    public int Vit { get; set; }

    /// <summary>Destreza base (sin equipo).</summary>
    public int Dex { get; set; }

    /// <summary>Entidades que este jugador tiene "spawneadas" ahora mismo en su cliente.</summary>
    public HashSet<int> Known { get; } = [];

    /// <summary>Tick del último snapshot que se le mandó; marca qué cambios ya conoce.</summary>
    public long LastSnapshotTick { get; set; }

    /// <summary>Región en la que estaba en el tick anterior, para detectar el cruce.</summary>
    public string CurrentRegion { get; set; } = string.Empty;

    /// <summary>Verdadero si la posición cambió desde el último guardado.</summary>
    public bool PositionDirty { get; set; }

    /// <summary>Oro actual (FASE-07 §2 D2).</summary>
    public long Gold { get; set; }

    /// <summary>Verdadero si el oro cambió desde el último guardado. Se persiste junto a la posición.</summary>
    public bool GoldDirty { get; set; }

    /// <summary>
    /// Id de entidad del NPC cuya tienda tiene abierta, o <c>null</c> si no tiene ninguna
    /// (FASE-07 §2 D1). Se guarda el id, no la clave de tienda: revalidar la distancia en cada
    /// acción (D7) necesita la posición del NPC, y de un id se llega a las dos cosas — de una
    /// clave de tienda sola no se llega a dónde está parado.
    /// </summary>
    public int? OpenShopNpcEntityId { get; set; }

    /// <summary>Strikes de anticheat acumulados (inputs por encima del presupuesto).</summary>
    public int CheatStrikes { get; set; }

    /// <summary>Nivel. No sube en la Fase 9: la curva es la Fase 10.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Experiencia acumulada.</summary>
    public long Xp { get; set; }

    /// <summary>Puntos de stat sin gastar (Fase 10).</summary>
    public int StatPoints { get; set; }

    /// <summary>
    /// Cooldown por habilidad: instante (<c>ServerClock.NowMs</c>) en el que cada
    /// <c>skillKey</c> vuelve a estar lista. Aparte del cooldown de <c>Attack</c> —lanzar una
    /// habilidad no bloquea las demás ni el ataque básico (FASE-10 §2 D7).
    /// </summary>
    public Dictionary<string, long> SkillCooldowns { get; } = [];

    /// <summary>
    /// Verdadero si vida, maná o XP cambiaron desde el último guardado. Se persisten junto a la
    /// posición y el oro, en el mismo <c>UPDATE</c> (FASE-09 §2 D12).
    /// </summary>
    public bool VitalsDirty { get; set; }

    /// <summary>Verdadero mientras el personaje esté muerto, esperando a reaparecer.</summary>
    public bool IsDead { get; set; }

    /// <summary>
    /// Instante (<c>ServerClock.NowMs</c>) en el que expira el flag de combate PvP, o <c>0</c> si
    /// no está en combate (<c>docs/00 §6.2</c>, FASE-09 §2 D11).
    /// </summary>
    public long CombatFlagUntilMs { get; set; }

    /// <summary>
    /// Instante en el que hay que sacar del mundo a un jugador que pidió salir estando en combate.
    /// <c>null</c> si no hay salida pendiente. Es lo que impide que "me van a matar" se resuelva
    /// con Alt+F4: la entidad se queda viva y atacable hasta que expire el flag.
    /// </summary>
    public long? PendingLeaveAtMs { get; set; }

    /// <summary>Último instante en el que atacó, para el cooldown.</summary>
    public long LastAttackMs { get; set; }

    /// <summary>Verdadero si el flag de combate sigue vigente en <paramref name="nowMs"/>.</summary>
    public bool IsInCombat(long nowMs) => CombatFlagUntilMs > nowMs;

    /// <summary>Poder de ataque efectivo, recalculado con cada <c>EquipmentUpdate</c> (Fase 9).</summary>
    public int AttackPower { get; set; }

    /// <summary>Defensa efectiva, recalculada con cada <c>EquipmentUpdate</c>.</summary>
    public int Defense { get; set; }

    /// <summary>Destreza efectiva (con equipo), la que entra en la probabilidad de crítico.</summary>
    public int DexEffective { get; set; }

    /// <summary>Historial de posiciones para la compensación de latencia (FASE-09 §2 D1).</summary>
    public PositionHistory History { get; } = new();

    /// <inheritdoc />
    public override bool IsAttackable => !IsDead;

    /// <inheritdoc />
    public override CombatantStats CombatStats => new(AttackPower, Defense, DexEffective);

    /// <summary>Aplica el resultado de un paso de simulación.</summary>
    public void Advance(in MoveState state, long tick)
    {
        var before = State.Pos;
        SetState(state, tick);

        if (State.Pos != before)
        {
            PositionDirty = true;
        }
    }
}

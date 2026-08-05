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

using Epimeteo.Server.Combat;
using Epimeteo.Server.Content;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>En qué está el monstruo ahora mismo (FASE-09 §2 D8).</summary>
public enum MonsterState : byte
{
    /// <summary>Quieto en su sitio, mirando si pasa alguien.</summary>
    Idle = 0,

    /// <summary>Dando una vuelta dentro de su radio.</summary>
    Patrol = 1,

    /// <summary>Yendo a por quien más amenaza lleva.</summary>
    Chase = 2,

    /// <summary>Pegando: ya está en alcance.</summary>
    Attack = 3,

    /// <summary>Volviendo al punto de aparición; ignora daño y aggro (correa, D7).</summary>
    Returning = 4,
}

/// <summary>
/// Un monstruo: una <see cref="WorldEntity"/> con una máquina de estados y una tabla de amenaza.
/// No toca <c>AoiSystem</c> ni <c>SnapshotBuilder</c> — igual que los NPC de la Fase 7, la
/// generalidad que se diseñó en la Fase 4 vuelve a salir gratis.
/// </summary>
public sealed class MonsterEntity : WorldEntity
{
    public MonsterEntity(int id, MonsterDefinition definition, Vec2 home, float radius, int spawnPointIndex)
        : base(id, EntityType.Monster, definition.Key, definition.DisplayName, MoveState.AtRest(home, Facing.South))
    {
        Definition = definition;
        Home = home;
        Radius = radius;
        SpawnPointIndex = spawnPointIndex;
        Hp = definition.HpMax;
        HpMax = definition.HpMax;
    }

    public MonsterDefinition Definition { get; }

    /// <summary>Punto de aparición: a dónde vuelve cuando se rinde.</summary>
    public Vec2 Home { get; }

    /// <summary>Radio dentro del que patrulla.</summary>
    public float Radius { get; }

    /// <summary>Índice de su punto de aparición, para que el spawner sepa a quién reponer.</summary>
    public int SpawnPointIndex { get; }

    public MonsterState AiState { get; set; } = MonsterState.Idle;

    public AggroTable Aggro { get; } = new();

    /// <summary>Último instante en que pegó, para su cooldown.</summary>
    public long LastAttackMs { get; set; }

    /// <summary>Instante en el que puede volver a decidir hacia dónde patrulla.</summary>
    public long NextPatrolDecisionMs { get; set; }

    /// <summary>Destino de la patrulla actual.</summary>
    public Vec2 PatrolTarget { get; set; }

    /// <inheritdoc />
    public override bool IsAttackable => true;

    /// <inheritdoc />
    public override CombatantStats CombatStats =>
        new(Definition.Attack, Definition.Defense, Definition.Dex);

    /// <summary>Mueve al monstruo y marca el tick si cambió de sitio.</summary>
    public void MoveTo(in MoveState state, long tick) => SetState(state, tick);

    /// <summary>Lo devuelve a su punto de aparición con la vida llena: se rindió (D7).</summary>
    public void ResetToHome(long tick)
    {
        Aggro.Clear();
        Hp = HpMax;
        AiState = MonsterState.Idle;
        SetState(MoveState.AtRest(Home, State.Facing), tick);
    }
}

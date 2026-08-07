using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>
/// Cualquier cosa que ocupa un sitio en el mundo y que otros pueden ver. En la Fase 4 sólo hay
/// jugadores; monstruos, NPCs y sacos de loot heredarán de aquí sin tocar AOI ni snapshots.
/// </summary>
public class WorldEntity
{
    protected WorldEntity(int id, EntityType type, string defKey, string name, MoveState state)
    {
        Id = id;
        Type = type;
        DefKey = defKey;
        Name = name;
        State = state;
    }

    /// <summary>Id efímero dentro del mundo.</summary>
    public int Id { get; }

    /// <summary>Qué es, para que el cliente sepa dibujarla.</summary>
    public EntityType Type { get; }

    /// <summary>Clave de contenido (<c>class.warrior</c>, <c>monster.slime</c>...).</summary>
    public string DefKey { get; }

    /// <summary>Nombre visible.</summary>
    public string Name { get; }

    /// <summary>Estado cinemático autoritativo.</summary>
    public MoveState State { get; protected set; }

    /// <summary>Apariencia placeholder mientras no haya sprites.</summary>
    public byte PaletteIndex { get; init; }

    /// <summary>Vida actual.</summary>
    public int Hp { get; set; }

    /// <summary>Vida máxima.</summary>
    public int HpMax { get; set; }

    /// <summary>Celda de AOI en la que está ahora mismo.</summary>
    public int Cell { get; internal set; }

    /// <summary>
    /// Si se le puede pegar (Fase 9). Falso por defecto: un tendero o un saco de loot no son
    /// objetivos, y el fallo seguro es no dejar atacar. Lo sobrescriben jugadores y monstruos.
    /// </summary>
    public virtual bool IsAttackable => false;

    /// <summary>Verdadero mientras le quede vida.</summary>
    public bool IsAlive => Hp > 0;

    /// <summary>Stats con los que entra en <c>CombatFormulas</c>. Los resuelve cada subtipo.</summary>
    public virtual CombatantStats CombatStats => default;

    /// <summary>
    /// Último tick en el que <see cref="State"/> cambió. Es lo que permite que un snapshot no
    /// repita entidades quietas: se compara con el último tick que recibió cada observador.
    /// </summary>
    public long LastChangedTick { get; protected set; }

    /// <summary>Aplica un estado nuevo y marca el tick si de verdad cambió algo.</summary>
    public void SetState(in MoveState state, long tick)
    {
        if (state == State)
        {
            return;
        }

        State = state;
        LastChangedTick = tick;
    }

    /// <summary>Información de aparición para un cliente que acaba de verla.</summary>
    public EntitySpawnInfo ToSpawnInfo() => new()
    {
        Id = Id,
        Type = Type,
        DefKey = DefKey,
        X = State.Pos.X,
        Y = State.Pos.Y,
        Facing = State.Facing,
        PaletteIndex = PaletteIndex,
        Name = Name,
        Hp = Hp,
        HpMax = HpMax,
    };

    /// <summary>Estado para un snapshot.</summary>
    public EntityDelta ToDelta() => new()
    {
        Id = Id,
        X = State.Pos.X,
        Y = State.Pos.Y,
        Vx = State.Vel.X,
        Vy = State.Vel.Y,
        Facing = State.Facing,
        Anim = State.Anim,
        Flags = 0,
    };
}

using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>
/// Un jugador dentro del mundo: la entidad, su sesión, su cola de inputs y lo que sabe ver.
/// Vive sólo en el hilo del tick — nadie más toca estos campos.
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
        long nowMs)
        : base(id, EntityType.Player, classKey, name, state)
    {
        Peer = peer;
        CharacterId = characterId;
        Inputs = new InputQueue(nowMs);
    }

    /// <summary>Sesión de red del jugador.</summary>
    public IWorldPeer Peer { get; }

    /// <summary>Fila de <c>characters</c> a la que se guarda la posición.</summary>
    public long CharacterId { get; }

    /// <summary>Cola de inputs pendientes de simular.</summary>
    public InputQueue Inputs { get; }

    /// <summary>Entidades que este jugador tiene "spawneadas" ahora mismo en su cliente.</summary>
    public HashSet<int> Known { get; } = [];

    /// <summary>Tick del último snapshot que se le mandó; marca qué cambios ya conoce.</summary>
    public long LastSnapshotTick { get; set; }

    /// <summary>Región en la que estaba en el tick anterior, para detectar el cruce.</summary>
    public string CurrentRegion { get; set; } = string.Empty;

    /// <summary>Verdadero si la posición cambió desde el último guardado.</summary>
    public bool PositionDirty { get; set; }

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

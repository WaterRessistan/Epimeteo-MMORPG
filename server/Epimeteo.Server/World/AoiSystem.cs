using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>
/// Decide qué ve cada jugador y le manda <c>EntitySpawn</c> de lo que entra y
/// <c>EntityDespawn</c> de lo que sale. Un jugador está suscrito a su celda y a las 8 vecinas.
/// <para>
/// Recalcula el conjunto visible completo y lo compara con lo que el cliente ya tiene, en vez de
/// intentar deducir las diferencias a partir de quién se movió. Es un puñado de operaciones de
/// conjunto sobre 9 celdas y, a cambio, es <b>idempotente</b>: si un mensaje se pierde o un caso
/// raro deja el estado torcido, el tick siguiente lo arregla solo. Deducir diferencias es más
/// rápido y se desincroniza en cuanto aparece un caso que no se previó.
/// </para>
/// </summary>
public sealed class AoiSystem
{
    private readonly AoiGrid _grid;
    private readonly CellGrid _cells;
    private readonly IReadOnlyDictionary<int, WorldEntity> _entities;
    private readonly HashSet<int> _visible = [];
    private readonly List<EntitySpawnInfo> _spawns = [];
    private readonly List<EntityDespawnEntry> _despawns = [];

    public AoiSystem(AoiGrid grid, CellGrid cells, IReadOnlyDictionary<int, WorldEntity> entities)
    {
        _grid = grid;
        _cells = cells;
        _entities = entities;
    }

    /// <summary>
    /// Pone al día lo que ve un jugador. Sólo manda mensajes si de verdad ha cambiado algo, así
    /// que llamarlo cada tick para todo el mundo no cuesta ancho de banda.
    /// </summary>
    public void Refresh(PlayerEntity player)
    {
        _visible.Clear();
        _spawns.Clear();
        _despawns.Clear();

        Span<int> neighbourhood = stackalloc int[AoiGrid.MaxNeighborhood];
        var count = _grid.GetNeighborhood(player.Cell, neighbourhood);

        for (var i = 0; i < count; i++)
        {
            foreach (var id in _cells.Occupants(neighbourhood[i]))
            {
                // El jugador no se ve a sí mismo como una entidad más: la suya llega en
                // WorldEnter y viaja en todos los snapshots.
                if (id != player.Id)
                {
                    _visible.Add(id);
                }
            }
        }

        foreach (var id in _visible)
        {
            if (!player.Known.Contains(id) && _entities.TryGetValue(id, out var entity))
            {
                _spawns.Add(entity.ToSpawnInfo());
            }
        }

        foreach (var id in player.Known)
        {
            if (!_visible.Contains(id))
            {
                _despawns.Add(new EntityDespawnEntry { Id = id, Reason = DespawnReason.OutOfRange });
            }
        }

        if (_spawns.Count > 0)
        {
            player.Peer.Send(Opcode.EntitySpawn, new S2CEntitySpawn { Entities = [.. _spawns] });
        }

        if (_despawns.Count > 0)
        {
            player.Peer.Send(Opcode.EntityDespawn, new S2CEntityDespawn { Entities = [.. _despawns] });
        }

        player.Known.Clear();
        foreach (var id in _visible)
        {
            player.Known.Add(id);
        }
    }

    /// <summary>
    /// Avisa de que una entidad desaparece por un motivo que no es alejarse (logout, muerte). Sin
    /// esto, el cliente se quedaría con un muñeco quieto hasta que casualmente saliera de su AOI.
    /// </summary>
    public static void NotifyRemoval(int entityId, DespawnReason reason, IEnumerable<PlayerEntity> players)
    {
        var message = new S2CEntityDespawn
        {
            Entities = [new EntityDespawnEntry { Id = entityId, Reason = reason }],
        };

        foreach (var player in players)
        {
            if (player.Known.Remove(entityId))
            {
                player.Peer.Send(Opcode.EntityDespawn, message);
            }
        }
    }
}

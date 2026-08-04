using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.World;

/// <summary>
/// Un mapa simulado: sus entidades, su rejilla de AOI y su tick. Un solo hilo la toca —el del
/// bucle de simulación—, así que aquí dentro no hay ni un <c>lock</c> ni un tipo concurrente.
/// </summary>
public sealed class Zone
{
    /// <summary>Inputs rechazados por presupuesto que se toleran antes de echar a la sesión.</summary>
    private const int CheatStrikeLimit = 60;

    private readonly Dictionary<int, WorldEntity> _entities = [];
    private readonly Dictionary<int, PlayerEntity> _playersBySession = [];
    private readonly List<PlayerEntity> _players = [];
    private readonly AoiGrid _grid;
    private readonly CellGrid _cells;
    private readonly AoiSystem _aoi;
    private readonly SnapshotBuilder _snapshots;
    private readonly ILogger _log = Log.ForContext<Zone>();
    private bool _cellsDirty;

    public Zone(GameMap map)
    {
        Map = map;
        _grid = new AoiGrid(map.Width, map.Height);
        _cells = new CellGrid(_grid.CellCount);
        _aoi = new AoiSystem(_grid, _cells, _entities);
        _snapshots = new SnapshotBuilder(_entities);
    }

    /// <summary>Mapa que simula esta zona.</summary>
    public GameMap Map { get; }

    /// <summary>Jugadores dentro de la zona.</summary>
    public IReadOnlyList<PlayerEntity> Players => _players;

    /// <summary>Entidades vivas, jugadores incluidos.</summary>
    public IReadOnlyDictionary<int, WorldEntity> Entities => _entities;

    /// <summary>Busca al jugador de una sesión.</summary>
    public PlayerEntity? FindBySession(int sessionId) =>
        _playersBySession.TryGetValue(sessionId, out var player) ? player : null;

    /// <summary>Busca al jugador que está usando un personaje concreto.</summary>
    public PlayerEntity? FindByCharacter(long characterId)
    {
        foreach (var player in _players)
        {
            if (player.CharacterId == characterId)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>Mete a un jugador en el mundo y le manda lo que ya puede ver.</summary>
    public PlayerEntity Join(IWorldPeer peer, WorldJoinRequest request, long tick, long nowMs)
    {
        var position = ResolveSpawn(request);

        var player = new PlayerEntity(
            request.EntityId,
            peer,
            request.CharacterId,
            request.ClassKey,
            request.Name,
            MoveState.AtRest(position, request.Facing),
            nowMs)
        {
            PaletteIndex = request.PaletteIndex,
            Hp = request.Hp,
            HpMax = request.HpMax,
        };

        player.Cell = _grid.CellOf(position);

        _entities[player.Id] = player;
        _players.Add(player);
        _playersBySession[peer.Id] = player;
        _cells.Add(player.Id, player.Cell);
        _cellsDirty = true;

        _aoi.Refresh(player);
        SendZoneFlags(player);

        _log.Information(
            "Entidad {EntityId} ({Name}) entra en {MapKey} en ({X:F2}, {Y:F2}); {Count} jugadores en zona",
            player.Id, player.Name, Map.Key, position.X, position.Y, _players.Count);

        return player;
    }

    /// <summary>Saca a un jugador del mundo y avisa a quien lo estuviera viendo.</summary>
    public PlayerEntity? Leave(int sessionId)
    {
        if (!_playersBySession.Remove(sessionId, out var player))
        {
            return null;
        }

        _entities.Remove(player.Id);
        _players.Remove(player);
        _cells.Remove(player.Id, player.Cell);
        _cellsDirty = true;

        AoiSystem.NotifyRemoval(player.Id, DespawnReason.Logout, _players);

        _log.Information("Entidad {EntityId} ({Name}) sale de {MapKey}; quedan {Count}",
            player.Id, player.Name, Map.Key, _players.Count);

        return player;
    }

    /// <summary>
    /// Encola un input recién llegado. Devuelve falso si hay que cerrar la sesión por insistir en
    /// pasarse del presupuesto.
    /// </summary>
    public void EnqueueInput(int sessionId, in MoveInput input, long nowMs)
    {
        var player = FindBySession(sessionId);
        if (player is null)
        {
            // Carrera normal entre el último InputState en vuelo y el leave de la sesión.
            return;
        }

        var admission = player.Inputs.TryEnqueue(input, nowMs);

        switch (admission)
        {
            case InputAdmission.RejectedBudget:
                player.CheatStrikes++;
                if (player.CheatStrikes == CheatStrikeLimit)
                {
                    _log.Warning(
                        "Entidad {EntityId} ({Name}) lleva {Strikes} inputs por encima del presupuesto; se cierra",
                        player.Id, player.Name, player.CheatStrikes);
                    player.Peer.Kick(KickReason.RateLimited, ResultCode.RateLimited);
                }

                break;

            case InputAdmission.AcceptedDroppingOldest:
                _log.Debug("Cola de inputs desbordada en la entidad {EntityId}; se descarta el más antiguo", player.Id);
                break;

            case InputAdmission.RejectedStaleSeq:
                _log.Debug("Input con seq {Seq} repetido o atrasado en la entidad {EntityId}", input.Seq, player.Id);
                break;

            case InputAdmission.Accepted:
            default:
                break;
        }
    }

    /// <summary>Un tick completo de la zona, en el orden de <c>docs/00 §4</c>.</summary>
    public void Tick(long tick, long nowMs)
    {
        foreach (var player in _players)
        {
            Simulate(player, tick);
        }

        if (_cellsDirty)
        {
            foreach (var player in _players)
            {
                _aoi.Refresh(player);
            }

            _cellsDirty = false;
        }

        // Snapshots a 10 Hz: uno de cada dos ticks (docs/01 § Ritmos).
        if (tick % (SimulationConstants.TickRate / SimulationConstants.SnapshotRate) == 0)
        {
            foreach (var player in _players)
            {
                _snapshots.Send(player, tick);
            }
        }
    }

    private void Simulate(PlayerEntity player, long tick)
    {
        Span<MoveInput> inputs = stackalloc MoveInput[2];
        var count = player.Inputs.Dequeue(inputs);
        var state = player.State;

        if (count == 0)
        {
            // Cola vacía: se simula sin dirección. Repetir el último input haría que un jugador
            // que suelta el teclado y pierde un paquete siguiera andando en el servidor.
            state = MovementSystem.Step(state, MoveInput.Idle(0, state.Facing), Map.Collision);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                state = MovementSystem.Step(state, inputs[i], Map.Collision);
            }
        }

        player.Advance(state, tick);

        var cell = _grid.CellOf(player.State.Pos);
        if (cell != player.Cell)
        {
            _cells.Move(player.Id, player.Cell, cell);
            player.Cell = cell;
            _cellsDirty = true;
        }

        var region = Map.Regions.Resolve(player.State.Pos);
        if (!string.Equals(region.Name, player.CurrentRegion, StringComparison.Ordinal))
        {
            player.CurrentRegion = region.Name;
            player.Peer.Send(Opcode.ZoneFlagsUpdate, new S2CZoneFlagsUpdate
            {
                RegionName = region.Name,
                Flags = region.Flags,
            });
        }
    }

    private void SendZoneFlags(PlayerEntity player)
    {
        var region = Map.Regions.Resolve(player.State.Pos);
        player.CurrentRegion = region.Name;
        player.Peer.Send(Opcode.ZoneFlagsUpdate, new S2CZoneFlagsUpdate
        {
            RegionName = region.Name,
            Flags = region.Flags,
        });
    }

    /// <summary>
    /// Posición de entrada. Si la guardada ya no vale —el mapa se editó y ahora hay un muro ahí—
    /// se cae al spawn del mapa en vez de dejar al jugador atrapado dentro de la geometría.
    /// </summary>
    private Vec2 ResolveSpawn(WorldJoinRequest request)
    {
        var stored = request.Position;

        if (!Map.Collision.IsBlocked(stored, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight))
        {
            return stored;
        }

        _log.Warning(
            "El personaje {CharacterId} estaba en ({X:F2}, {Y:F2}) de {MapKey}, hoy bloqueado; entra por el spawn",
            request.CharacterId, stored.X, stored.Y, Map.Key);

        return Map.Spawn;
    }
}

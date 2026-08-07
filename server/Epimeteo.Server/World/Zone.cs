using Epimeteo.Server.Combat;
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
    private readonly List<MonsterEntity> _monsters = [];
    private readonly List<LootBagEntity> _lootBags = [];
    private readonly List<PendingMonsterAttack> _pendingMonsterAttacks = [];
    private readonly AoiGrid _grid;
    private readonly CellGrid _cells;
    private readonly AoiSystem _aoi;
    private readonly SnapshotBuilder _snapshots;
    private readonly ILogger _log = Log.ForContext<Zone>();
    private bool _cellsDirty;

    public Zone(GameMap map, IReadOnlyList<NpcEntity>? npcs = null, ulong rngSeed = 0)
    {
        Map = map;

        // Una secuencia por zona, con semilla de servidor (FASE-09 §2 D4). La semilla no sale de
        // aquí: el cliente no predice daño ni tiradas de loot.
        Rng = new DeterministicRng(rngSeed == 0 ? (ulong)DateTime.UtcNow.Ticks : rngSeed);
        _grid = new AoiGrid(map.Width, map.Height);
        _cells = new CellGrid(_grid.CellCount);
        _aoi = new AoiSystem(_grid, _cells, _entities);
        _snapshots = new SnapshotBuilder(_entities);

        // Los NPCs se registran una vez, al construir la zona (no por jugador, como los
        // PlayerEntity): son estáticos para siempre, así que basta con que existan en
        // _entities/_cells para que el AoiSystem de cualquier jugador cercano los descubra con
        // el EntitySpawn que ya existe desde la Fase 4 (FASE-07 §2 D3). Sin _aoi.Refresh —eso es
        // "quién ve a quién" desde la perspectiva de un jugador, y un NPC nunca mira alrededor—.
        foreach (var npc in npcs ?? [])
        {
            npc.Cell = _grid.CellOf(npc.State.Pos);
            _entities[npc.Id] = npc;
            _cells.Add(npc.Id, npc.Cell);
        }

        if (npcs is { Count: > 0 })
        {
            _cellsDirty = true;
        }
    }

    /// <summary>Mapa que simula esta zona.</summary>
    public GameMap Map { get; }

    /// <summary>Generador de la zona: daño, tiradas de loot y patrullas (FASE-09 §2 D4).</summary>
    public DeterministicRng Rng { get; }

    /// <summary>Jugadores dentro de la zona.</summary>
    public IReadOnlyList<PlayerEntity> Players => _players;

    /// <summary>Entidades vivas, jugadores incluidos.</summary>
    public IReadOnlyDictionary<int, WorldEntity> Entities => _entities;

    /// <summary>Monstruos de la zona (Fase 9).</summary>
    public IReadOnlyList<MonsterEntity> Monsters => _monsters;

    /// <summary>Sacos de loot en el suelo (Fase 9).</summary>
    public IReadOnlyList<LootBagEntity> LootBags => _lootBags;

    /// <summary>
    /// Ataques que los monstruos quieren lanzar este tick. Los resuelve <c>GameWorld</c>, que es
    /// quien tiene el RNG y los catálogos — la IA sólo decide, no pega (FASE-09 §2 D8).
    /// </summary>
    public IReadOnlyList<PendingMonsterAttack> PendingMonsterAttacks => _pendingMonsterAttacks;

    /// <summary>Mete una entidad que no es un jugador (monstruo, saco de loot) en el mundo.</summary>
    public void AddEntity(WorldEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.Cell = _grid.CellOf(entity.State.Pos);
        _entities[entity.Id] = entity;
        _cells.Add(entity.Id, entity.Cell);
        _cellsDirty = true;

        switch (entity)
        {
            case MonsterEntity monster:
                _monsters.Add(monster);
                break;

            case LootBagEntity bag:
                _lootBags.Add(bag);
                break;

            default:
                break;
        }
    }

    /// <summary>Saca del mundo una entidad que no es un jugador y avisa a quien la estuviera viendo.</summary>
    public void RemoveEntity(int entityId, DespawnReason reason)
    {
        if (!_entities.Remove(entityId, out var entity))
        {
            return;
        }

        _cells.Remove(entityId, entity.Cell);
        _cellsDirty = true;

        switch (entity)
        {
            case MonsterEntity monster:
                _monsters.Remove(monster);
                break;

            case LootBagEntity bag:
                _lootBags.Remove(bag);
                break;

            default:
                break;
        }

        AoiSystem.NotifyRemoval(entityId, reason, _players);
    }

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
            nowMs,
            request.Items)
        {
            PaletteIndex = request.PaletteIndex,
            Hp = request.Hp,
            HpMax = request.HpMax,
            Mp = request.Mp,
            MpMax = request.MpMax,
            Str = request.StatStr,
            IntStat = request.StatInt,
            Vit = request.StatVit,
            Dex = request.StatDex,
            Gold = request.Gold,
            Level = request.Level,
            Xp = request.Xp,
            StatPoints = request.StatPoints,
            AccountId = request.AccountId,
            IsAdmin = request.IsAdmin,
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

    /// <summary>
    /// Mueve a un jugador de golpe a otro punto de la misma zona (reaparición tras morir). Hay que
    /// pasar por aquí y no tocar <c>State</c> a pelo: cambia de celda de AOI, y el historial de
    /// posiciones tiene que olvidar el salto — si no, un ataque rebobinado podría alcanzarle en el
    /// sitio donde murió.
    /// </summary>
    public void Teleport(PlayerEntity player, Vec2 destination, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(player);

        player.SetState(MoveState.AtRest(destination, player.State.Facing), player.LastChangedTick + 1);

        var cell = _grid.CellOf(destination);
        if (cell != player.Cell)
        {
            _cells.Move(player.Id, player.Cell, cell);
            player.Cell = cell;
            _cellsDirty = true;
        }

        player.History.Reset(nowMs, destination);
        _aoi.Refresh(player);
        SendZoneFlags(player);
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
        _pendingMonsterAttacks.Clear();

        foreach (var player in _players)
        {
            Simulate(player, tick);

            // El historial se anota después de simular, con la posición ya autoritativa de este
            // tick: es contra esto contra lo que se rebobina un ataque (FASE-09 §2 D1).
            player.History.Record(nowMs, player.State.Pos);
        }

        TickMonsters(tick, nowMs);

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

    /// <summary>
    /// La IA de todos los monstruos de la zona. Sólo decide y mueve: los ataques que quiera lanzar
    /// quedan en <see cref="PendingMonsterAttacks"/> para que <c>GameWorld</c> los pase por
    /// <c>CombatSystem</c>, exactamente igual que el ataque de un jugador.
    /// </summary>
    private void TickMonsters(long tick, long nowMs)
    {
        foreach (var monster in _monsters)
        {
            var action = MonsterAi.Tick(monster, _players, Map, Rng, tick, nowMs);

            if (action.AttackTargetId is { } targetId)
            {
                _pendingMonsterAttacks.Add(new PendingMonsterAttack(monster, targetId));
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

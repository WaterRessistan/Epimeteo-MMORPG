using Epimeteo.Server.Content;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using Epimeteo.Shared.Time;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.World;

/// <summary>
/// El mundo entero: una zona por mapa, la frontera con el hilo de red y la cola de guardados.
/// Se llama <c>GameWorld</c> y no <c>World</c> para no chocar con el namespace del mismo nombre.
/// Lo llama el <see cref="GameLoop"/> una vez por tick y todo lo que hay debajo corre en ese
/// mismo hilo.
/// </summary>
public sealed class GameWorld
{
    private readonly Dictionary<string, Zone> _zones = new(StringComparer.Ordinal);
    private readonly WorldInbox _inbox;
    private readonly IPositionSink _positions;
    private readonly int _saveIntervalTicks;
    private readonly string _fallbackMapKey;
    private readonly ILogger _log = Log.ForContext<GameWorld>();

    public GameWorld(MapCatalog maps, WorldInbox inbox, IPositionSink positions, int saveIntervalSeconds = 30)
    {
        _inbox = inbox;
        _positions = positions;
        _saveIntervalTicks = saveIntervalSeconds * SimulationConstants.TickRate;

        foreach (var map in maps.All)
        {
            _zones[map.Key] = new Zone(map);
        }

        _fallbackMapKey = _zones.ContainsKey("map.village") ? "map.village" : _zones.Keys.First();
    }

    /// <summary>Zonas simuladas, una por mapa cargado.</summary>
    public IReadOnlyCollection<Zone> Zones => _zones.Values;

    /// <summary>Jugadores dentro del mundo, sumando todas las zonas.</summary>
    public int PlayerCount => _zones.Values.Sum(zone => zone.Players.Count);

    /// <summary>Entidades vivas, sumando todas las zonas.</summary>
    public int EntityCount => _zones.Values.Sum(zone => zone.Entities.Count);

    /// <summary>Un tick de mundo con el reloj del servidor.</summary>
    public void Tick(long tick) => Tick(tick, ServerClock.NowMs);

    /// <summary>
    /// Un tick de mundo: control, mensajes, simulación y guardados.
    /// <para>
    /// El instante entra por parámetro y no se lee aquí dentro porque el presupuesto de inputs
    /// depende de él: con el reloj real, un test que simula 10 ticks en un milisegundo parecería
    /// un cliente inundando de inputs.
    /// </para>
    /// </summary>
    public void Tick(long tick, long nowMs)
    {
        DrainControl(tick, nowMs);
        DrainMessages(nowMs);

        foreach (var zone in _zones.Values)
        {
            zone.Tick(tick, nowMs);
        }

        SweepSaves(tick);
    }

    /// <summary>
    /// Vuelca la posición de todos los jugadores. La llama el apagado: sin esto, un
    /// <c>systemctl restart</c> perdería hasta 30 s de movimiento de todo el servidor.
    /// </summary>
    public void FlushAllPositions()
    {
        foreach (var zone in _zones.Values)
        {
            foreach (var player in zone.Players)
            {
                Save(zone, player);
            }
        }
    }

    private void DrainControl(long tick, long nowMs)
    {
        while (_inbox.TryDequeueControl(out var command))
        {
            switch (command)
            {
                case PlayerJoinCommand join:
                    HandleJoin(join, tick, nowMs);
                    break;

                case PlayerLeaveCommand leave:
                    HandleLeave(leave.SessionId);
                    break;

                default:
                    _log.Warning("Comando de mundo desconocido: {Command}", command.GetType().Name);
                    break;
            }
        }
    }

    private void HandleJoin(PlayerJoinCommand join, long tick, long nowMs)
    {
        var request = join.Request;

        if (!_zones.TryGetValue(request.MapKey, out var zone))
        {
            _log.Warning("El personaje {CharacterId} estaba en el mapa desconocido {MapKey}; entra en {Fallback}",
                request.CharacterId, request.MapKey, _fallbackMapKey);
            zone = _zones[_fallbackMapKey];
            request = request with { MapKey = zone.Map.Key, Position = zone.Map.Spawn };
        }

        // Doble conexión con el mismo personaje: si no se echa al anterior, las dos sesiones se
        // pisan la posición al guardar y el jugador se teletransporta solo.
        foreach (var other in _zones.Values)
        {
            var duplicate = other.FindByCharacter(request.CharacterId);
            if (duplicate is not null)
            {
                _log.Information("El personaje {CharacterId} ya estaba en el mundo; se cierra la sesión {SessionId}",
                    request.CharacterId, duplicate.Peer.Id);
                duplicate.Peer.Kick(KickReason.LoggedInElsewhere);
                RemoveFrom(other, duplicate.Peer.Id);
            }
        }

        zone.Join(join.Peer, request, tick, nowMs);
    }

    private void HandleLeave(int sessionId)
    {
        foreach (var zone in _zones.Values)
        {
            if (RemoveFrom(zone, sessionId))
            {
                return;
            }
        }
    }

    private bool RemoveFrom(Zone zone, int sessionId)
    {
        var player = zone.Leave(sessionId);
        if (player is null)
        {
            return false;
        }

        // El guardado final no espera al barrido periódico: la sesión ya no existe.
        Save(zone, player, force: true);
        return true;
    }

    private void DrainMessages(long nowMs)
    {
        while (_inbox.TryDequeue(out var message))
        {
            switch (message.Opcode)
            {
                case Opcode.InputState:
                    HandleInput(message, nowMs);
                    break;

                default:
                    // La tabla de opcodes ya filtra estado y dirección; llegar aquí con otra cosa
                    // es un enrutado mal hecho en el servidor, no un cliente malicioso.
                    _log.Warning("Opcode {Opcode} sin sistema de mundo que lo atienda (sesión {SessionId})",
                        message.Opcode, message.SessionId);
                    break;
            }
        }
    }

    private void HandleInput(in WorldMessage message, long nowMs)
    {
        if (!FrameCodec.TryDecodeBody<C2SInputState>(message.Payload, out var input) || input is null)
        {
            _log.Warning("InputState ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var move = new MoveInput(input.Seq, input.DirX, input.DirY, input.Facing);

        if (!move.IsWellFormed())
        {
            // Un cliente honesto no manda una dirección que no sea -1, 0 o 1. No se clampa en
            // silencio: es una violación de protocolo (FASE-04 §8).
            _log.Warning("InputState con dirección ({DirX}, {DirY}) o facing {Facing} inválidos en la sesión {SessionId}",
                input.DirX, input.DirY, input.Facing, message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        foreach (var zone in _zones.Values)
        {
            if (zone.FindBySession(message.SessionId) is not null)
            {
                zone.EnqueueInput(message.SessionId, move, nowMs);
                return;
            }
        }
    }

    private void KickSession(int sessionId, KickReason reason)
    {
        foreach (var zone in _zones.Values)
        {
            zone.FindBySession(sessionId)?.Peer.Kick(reason);
        }
    }

    /// <summary>
    /// Guardado periódico, escalonado por id de entidad: sin el escalonado, todos los jugadores
    /// escribirían en el mismo tick cada 30 s y ese tick se saldría del presupuesto.
    /// </summary>
    private void SweepSaves(long tick)
    {
        foreach (var zone in _zones.Values)
        {
            foreach (var player in zone.Players)
            {
                if ((tick + player.Id) % _saveIntervalTicks == 0)
                {
                    Save(zone, player);
                }
            }
        }
    }

    private void Save(Zone zone, PlayerEntity player, bool force = false)
    {
        if (!player.PositionDirty && !force)
        {
            return;
        }

        _positions.Enqueue(new PositionSave(
            player.CharacterId,
            zone.Map.Key,
            player.State.Pos.X,
            player.State.Pos.Y,
            player.State.Facing));

        player.PositionDirty = false;
    }
}

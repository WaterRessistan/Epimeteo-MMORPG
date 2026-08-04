using Epimeteo.Server.Content;
using Epimeteo.Server.Inventory;
using Epimeteo.Server.Persistence.Items;
using Epimeteo.Shared.Data;
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
    private readonly ItemCatalog _items;
    private readonly ClassCatalog _classes;
    private readonly IInventorySink _inventorySink;
    private readonly int _saveIntervalTicks;
    private readonly string _fallbackMapKey;
    private readonly ILogger _log = Log.ForContext<GameWorld>();

    public GameWorld(
        MapCatalog maps,
        WorldInbox inbox,
        IPositionSink positions,
        ItemCatalog items,
        ClassCatalog classes,
        IInventorySink inventorySink,
        int saveIntervalSeconds = 30)
    {
        _inbox = inbox;
        _positions = positions;
        _items = items;
        _classes = classes;
        _inventorySink = inventorySink;
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
    /// Vuelca la posición y el inventario de todos los jugadores. La llama el apagado: sin esto,
    /// un <c>systemctl restart</c> perdería hasta 30 s de movimiento, más cualquier mutación de
    /// inventario que no hubiera llegado a encolarse todavía.
    /// </summary>
    public void FlushAllState()
    {
        foreach (var zone in _zones.Values)
        {
            foreach (var player in zone.Players)
            {
                Save(zone, player);
                SaveInventory(player);
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

        var player = zone.Join(join.Peer, request, tick, nowMs);

        // El inventario ya viaja cargado en el WorldJoinRequest (FASE-06 §2 D1); lo único que
        // falta es que el cliente lo vea. InventoryFull son las bolsas; EquipmentUpdate manda el
        // equipo puesto más los stats derivados, que también hace falta calcular por primera vez.
        player.Peer.Send(Opcode.InventoryFull, new S2CInventoryFull { Items = BagItems(player) });
        SendEquipmentUpdate(player);
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

        // El guardado final no espera al barrido periódico: la sesión ya no existe. El
        // inventario no tiene barrido periódico (se guarda tras cada mutación, FASE-06 §2 D2),
        // pero este guardado extra es la red de seguridad si alguna quedó sin encolar.
        Save(zone, player, force: true);
        SaveInventory(player);
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

                case Opcode.InvMove:
                    HandleInvMove(message);
                    break;

                case Opcode.InvUse:
                    HandleInvUse(message);
                    break;

                case Opcode.InvDrop:
                    HandleInvDrop(message);
                    break;

                case Opcode.Equip:
                    HandleEquip(message);
                    break;

                case Opcode.Unequip:
                    HandleUnequip(message);
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

    private PlayerEntity? FindPlayer(int sessionId)
    {
        foreach (var zone in _zones.Values)
        {
            var player = zone.FindBySession(sessionId);
            if (player is not null)
            {
                return player;
            }
        }

        return null;
    }

    private void HandleInvMove(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SInvMove>(message.Payload, out var move) || move is null)
        {
            _log.Warning("InvMove ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var player = FindPlayer(message.SessionId);
        if (player is null)
        {
            return; // Carrera con leave, no es un ataque (mismo criterio que InputState).
        }

        if (!InventoryConstants.IsWellFormedSlot(move.FromContainer, move.FromSlot) ||
            !InventoryConstants.IsWellFormedSlot(move.ToContainer, move.ToSlot) ||
            move.Quantity <= 0)
        {
            // Un cliente honesto conoce el tamaño de sus propias bolsas: esto es protocolo, no
            // una jugada legal que falla (FASE-06 §5, mismo criterio que InputState en Fase 4).
            _log.Warning("InvMove con hueco o cantidad imposibles en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var result = InventorySystem.TryMove(
            player.Inventory, _items, move.FromContainer, move.FromSlot, move.ToContainer, move.ToSlot, move.Quantity);

        ApplyResult(player, result);
    }

    private void HandleInvUse(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SInvUse>(message.Payload, out var use) || use is null)
        {
            _log.Warning("InvUse ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var player = FindPlayer(message.SessionId);
        if (player is null)
        {
            return;
        }

        if (!InventoryConstants.IsWellFormedSlot(use.Container, use.Slot))
        {
            _log.Warning("InvUse con hueco imposible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var result = InventorySystem.TryUse(player.Inventory, _items, use.Container, use.Slot);
        if (result.Ok)
        {
            // Sin EntityStats todavía (FASE-06 §1): el HP curado se aplica de verdad, pero el
            // cliente no tiene forma de verlo aún —no hay barra de vida en el HUD (Fase 4)—.
            // La prueba observable de que "usar" funcionó es que el consumible desapareció
            // (InventoryDelta más abajo), no el número de vida.
            player.Hp = Math.Min(player.HpMax, player.Hp + result.HealAmount);
        }

        ApplyResult(player, new InventoryOpResult(result.Ok, result.Code, result.Touched));
    }

    private void HandleInvDrop(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SInvDrop>(message.Payload, out var drop) || drop is null)
        {
            _log.Warning("InvDrop ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var player = FindPlayer(message.SessionId);
        if (player is null)
        {
            return;
        }

        if (!InventoryConstants.IsWellFormedSlot(drop.Container, drop.Slot) || drop.Quantity <= 0)
        {
            _log.Warning("InvDrop con hueco o cantidad imposibles en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var result = InventorySystem.TryDrop(player.Inventory, drop.Container, drop.Slot, drop.Quantity);
        ApplyResult(player, result);
    }

    private void HandleEquip(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SEquip>(message.Payload, out var equip) || equip is null)
        {
            _log.Warning("Equip ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var player = FindPlayer(message.SessionId);
        if (player is null)
        {
            return;
        }

        if (!InventoryConstants.IsWellFormedSlot(equip.Container, equip.Slot) || !Enum.IsDefined(equip.EquipSlot))
        {
            _log.Warning("Equip con hueco o EquipSlot imposibles en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var result = InventorySystem.TryEquip(player.Inventory, _items, equip.Container, equip.Slot, equip.EquipSlot);
        ApplyResult(player, result);
    }

    private void HandleUnequip(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SUnequip>(message.Payload, out var unequip) || unequip is null)
        {
            _log.Warning("Unequip ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var player = FindPlayer(message.SessionId);
        if (player is null)
        {
            return;
        }

        if (!Enum.IsDefined(unequip.EquipSlot))
        {
            _log.Warning("Unequip con EquipSlot imposible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var result = InventorySystem.TryUnequip(player.Inventory, _items, unequip.EquipSlot);
        ApplyResult(player, result);
    }

    /// <summary>
    /// Reparte el resultado de una mutación: fallo → <c>SystemMessage</c> y nada más (nada
    /// cambió); éxito sin huecos tocados → tampoco hay nada que mandar ni guardar (apilar contra
    /// un slot ya lleno, FASE-06 §5); éxito de verdad → <c>InventoryDelta</c> para las bolsas,
    /// <c>EquipmentUpdate</c> si tocó el equipo, y siempre el guardado.
    /// </summary>
    private void ApplyResult(PlayerEntity player, InventoryOpResult result)
    {
        if (!result.Ok)
        {
            SendInventoryFailure(player, result.Code);
            return;
        }

        if (result.Touched.Count == 0)
        {
            return;
        }

        var touchedEquip = false;
        var changes = new List<InventoryChangeEntry>(result.Touched.Count);

        foreach (var slot in result.Touched)
        {
            if (slot.Container == ContainerId.Equipped)
            {
                touchedEquip = true;
                continue;
            }

            var stack = player.Inventory.Find(slot.Container, slot.Slot);
            changes.Add(new InventoryChangeEntry
            {
                Container = slot.Container,
                Slot = slot.Slot,
                Item = stack is null ? null : ToInfo(stack),
            });
        }

        if (changes.Count > 0)
        {
            player.Peer.Send(Opcode.InventoryDelta, new S2CInventoryDelta { Changes = [.. changes] });
        }

        if (touchedEquip)
        {
            SendEquipmentUpdate(player);
        }

        SaveInventory(player);
    }

    private static void SendInventoryFailure(PlayerEntity player, ResultCode code) => player.Peer.Send(
        Opcode.SystemMessage,
        new S2CSystemMessage { Severity = 0, Key = $"inventory.{code}", Args = [] });

    /// <summary>
    /// Recalcula stats derivados (FASE-06 §2 D5), clampa <c>Hp</c>/<c>Mp</c> actuales a los
    /// nuevos máximos si hacía falta, y manda el equipo completo. Se llama tras un
    /// <c>Equip</c>/<c>Unequip</c> con éxito y una vez al entrar al mundo.
    /// </summary>
    private void SendEquipmentUpdate(PlayerEntity player)
    {
        if (!_classes.TryGet(player.DefKey, out var classDef))
        {
            _log.Error("Entidad {EntityId} con clase desconocida {ClassKey}; no se recalculan stats",
                player.Id, player.DefKey);
            return;
        }

        var stats = InventorySystem.ComputeDerivedStats(
            player.Inventory, _items, classDef, player.Str, player.IntStat, player.Vit, player.Dex);

        player.HpMax = stats.HpMax;
        player.MpMax = stats.MpMax;
        player.Hp = Math.Min(player.Hp, player.HpMax);
        player.Mp = Math.Min(player.Mp, player.MpMax);

        var equipped = player.Inventory.Stacks
            .Where(stack => stack.Container == ContainerId.Equipped)
            .Select(ToInfo)
            .ToArray();

        player.Peer.Send(Opcode.EquipmentUpdate, new S2CEquipmentUpdate
        {
            Equipped = equipped,
            HpMax = stats.HpMax,
            MpMax = stats.MpMax,
            StrEffective = stats.StrEffective,
            IntEffective = stats.IntEffective,
            VitEffective = stats.VitEffective,
            DexEffective = stats.DexEffective,
        });
    }

    private static ItemStackInfo[] BagItems(PlayerEntity player) =>
        [.. player.Inventory.Stacks.Where(stack => stack.Container != ContainerId.Equipped).Select(ToInfo)];

    private static ItemStackInfo ToInfo(ItemStack stack) => new()
    {
        DefKey = stack.DefKey,
        Container = stack.Container,
        Slot = stack.Slot,
        Quantity = stack.Quantity,
        Durability = stack.Durability,
        DurabilityMax = stack.DurabilityMax,
        Quality = stack.Quality,
        BoundTo = stack.BoundTo,
    };

    /// <summary>
    /// Instantánea completa del inventario del personaje (FASE-06 §2 D2): se manda entero, no un
    /// delta, así que perder una instantánea vieja en la cola nunca corrompe nada.
    /// </summary>
    private void SaveInventory(PlayerEntity player)
    {
        var snapshot = player.Inventory.Stacks
            .Select(stack => new ItemStackSnapshot(
                stack.DefKey, (byte)stack.Container, stack.Slot, stack.Quantity,
                stack.Durability, stack.DurabilityMax, stack.Quality, stack.BoundTo))
            .ToArray();

        _inventorySink.Enqueue(new InventorySave(player.CharacterId, snapshot));
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

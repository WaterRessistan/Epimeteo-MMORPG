using Epimeteo.Server.Content;
using Epimeteo.Server.Farm;
using Epimeteo.Server.Inventory;
using Epimeteo.Server.Persistence.Economy;
using Epimeteo.Server.Persistence.Farm;
using Epimeteo.Server.Persistence.Items;
using Epimeteo.Server.Shop;
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
    /// <summary>Radio de interacción con un NPC de tienda, en tiles (FASE-07 §2 D7).</summary>
    private const float ShopInteractionRangeTiles = 3f;

    /// <summary>
    /// Radio de interacción con un tile de granja, en tiles. CLAUDE.md §4 es explícito: toda
    /// petición se valida contra distancia real, y las cuatro acciones de granja no son la
    /// excepción — mismo criterio que <see cref="ShopInteractionRangeTiles"/>, aunque
    /// <c>FASE-08-granja-cultivos.md</c> no lo mencionara aparte (D6 sólo cubre "el tile existe",
    /// no "está cerca").
    /// </summary>
    private const float FarmInteractionRangeTiles = 2f;

    private readonly Dictionary<string, Zone> _zones = new(StringComparer.Ordinal);
    private readonly WorldInbox _inbox;
    private readonly IPositionSink _positions;
    private readonly ItemCatalog _items;
    private readonly ClassCatalog _classes;
    private readonly IInventorySink _inventorySink;
    private readonly ShopCatalog _shops;
    private readonly ShopRuntime _shopRuntime;
    private readonly IEconomySink _economySink;
    private readonly CropCatalog _crops;
    private readonly FarmRuntime _farmRuntime;
    private readonly IFarmSink _farmSink;
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
        ShopCatalog shops,
        ShopRuntime shopRuntime,
        IEconomySink economySink,
        CropCatalog crops,
        FarmRuntime farmRuntime,
        IFarmSink farmSink,
        EntityIdAllocator entityIds,
        int saveIntervalSeconds = 30)
    {
        _inbox = inbox;
        _positions = positions;
        _items = items;
        _classes = classes;
        _inventorySink = inventorySink;
        _shops = shops;
        _shopRuntime = shopRuntime;
        _economySink = economySink;
        _crops = crops;
        _farmRuntime = farmRuntime;
        _farmSink = farmSink;
        _saveIntervalTicks = saveIntervalSeconds * SimulationConstants.TickRate;

        var npcsByMap = new Dictionary<string, List<NpcEntity>>(StringComparer.Ordinal);
        foreach (var shop in shops.All)
        {
            var npc = new NpcEntity(
                entityIds.Next(), shop.Key, shop.Npc.Name, new Vec2(shop.Npc.X, shop.Npc.Y), shop.Npc.Facing);

            if (!npcsByMap.TryGetValue(shop.Npc.MapKey, out var list))
            {
                npcsByMap[shop.Npc.MapKey] = list = [];
            }

            list.Add(npc);
        }

        foreach (var map in maps.All)
        {
            _zones[map.Key] = new Zone(map, npcsByMap.GetValueOrDefault(map.Key));
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
        SweepRestock(tick);
        SweepFarmGrowth(tick);
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

        foreach (var plot in _farmRuntime.Plots)
        {
            if (plot.MapKey == zone.Map.Key)
            {
                player.Peer.Send(Opcode.FarmTileUpdate, BuildFarmTileUpdate(plot, DateTimeOffset.UtcNow));
            }
        }
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

                case Opcode.ShopOpen:
                    HandleShopOpen(message);
                    break;

                case Opcode.ShopBuy:
                    HandleShopBuy(message);
                    break;

                case Opcode.ShopSell:
                    HandleShopSell(message);
                    break;

                case Opcode.ShopClose:
                    HandleShopClose(message);
                    break;

                case Opcode.ShopRepair:
                    HandleShopRepair(message);
                    break;

                case Opcode.FarmTill:
                    HandleFarmTill(message);
                    break;

                case Opcode.FarmPlant:
                    HandleFarmPlant(message);
                    break;

                case Opcode.FarmWater:
                    HandleFarmWater(message);
                    break;

                case Opcode.FarmHarvest:
                    HandleFarmHarvest(message);
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

        // Se captura antes de mutar: TryDrop puede vaciar el hueco. economy_log quiere saber qué
        // se tiró (FASE-07 §2 D9 — retoma lo que la Fase 6 dejó pendiente por falta de tabla).
        var defKey = player.Inventory.Find(drop.Container, drop.Slot)?.DefKey;

        var result = InventorySystem.TryDrop(player.Inventory, drop.Container, drop.Slot, drop.Quantity);
        ApplyResult(player, result);

        if (result.Ok && result.Touched.Count > 0 && defKey is not null)
        {
            _economySink.Enqueue(new EconomySave(
                EconomyLogKind.Drop, player.CharacterId, defKey, drop.Quantity, 0, player.Gold, null, null, null, null));
        }
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

    // ── Tienda ───────────────────────────────────────────────────────────

    private void HandleShopOpen(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SShopOpen>(message.Payload, out var open) || open is null)
        {
            _log.Warning("ShopOpen ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;

        if (!zone.Entities.TryGetValue(open.NpcEntityId, out var entity) || entity is not NpcEntity npc)
        {
            SendShopFailure(player, ResultCode.TargetNotFound);
            return;
        }

        if (!IsWithinShopRange(player, npc))
        {
            SendShopFailure(player, ResultCode.TooFarAway);
            return;
        }

        if (!_shops.TryGet(npc.ShopKey, out var shop) || !_shopRuntime.TryGetShopStock(npc.ShopKey, out var stock))
        {
            _log.Error("NPC {EntityId} referencia la tienda desconocida {ShopKey}", npc.Id, npc.ShopKey);
            SendShopFailure(player, ResultCode.UnknownError);
            return;
        }

        player.OpenShopNpcEntityId = npc.Id;
        player.Peer.Send(Opcode.ShopData, BuildShopData(shop, stock));
    }

    private void HandleShopBuy(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SShopBuy>(message.Payload, out var buy) || buy is null)
        {
            _log.Warning("ShopBuy ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (buy.Quantity <= 0)
        {
            _log.Warning("ShopBuy con cantidad imposible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;
        if (ResolveOpenShop(zone, player) is not { } resolved)
        {
            return;
        }

        var (shop, stock, npc) = resolved;
        if (!IsWithinShopRange(player, npc))
        {
            SendShopFailure(player, ResultCode.TooFarAway);
            return;
        }

        var result = ShopSystem.TryBuy(player.Inventory, _items, shop, stock, player.Gold, buy.ShopSlot, buy.Quantity, buy.ExpectedPrice);
        if (!result.Ok)
        {
            SendShopFailure(player, result.Code);
            return;
        }

        var defKey = shop.Items[buy.ShopSlot].DefKey;
        ApplySuccessfulShopOp(player, result, EconomyLogKind.Buy, shop, stock, defKey, buy.Quantity);
    }

    private void HandleShopSell(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SShopSell>(message.Payload, out var sell) || sell is null)
        {
            _log.Warning("ShopSell ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (!InventoryConstants.IsWellFormedSlot(sell.Container, sell.Slot) || sell.Quantity <= 0)
        {
            _log.Warning("ShopSell con hueco o cantidad imposibles en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;
        if (ResolveOpenShop(zone, player) is not { } resolved)
        {
            return;
        }

        var (shop, stock, npc) = resolved;
        if (!IsWithinShopRange(player, npc))
        {
            SendShopFailure(player, ResultCode.TooFarAway);
            return;
        }

        // Se captura antes de mutar: TrySell puede vaciar el hueco.
        var defKey = player.Inventory.Find(sell.Container, sell.Slot)?.DefKey;

        var result = ShopSystem.TrySell(player.Inventory, shop, stock, player.Gold, sell.Container, sell.Slot, sell.Quantity, sell.ExpectedPrice);
        if (!result.Ok)
        {
            SendShopFailure(player, result.Code);
            return;
        }

        ApplySuccessfulShopOp(player, result, EconomyLogKind.Sell, shop, stock, defKey!, sell.Quantity);
    }

    private void HandleShopRepair(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SShopRepair>(message.Payload, out var repair) || repair is null)
        {
            _log.Warning("ShopRepair ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (!InventoryConstants.IsWellFormedSlot(repair.Container, repair.Slot))
        {
            _log.Warning("ShopRepair con hueco imposible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;
        if (ResolveOpenShop(zone, player) is not { } resolved)
        {
            return;
        }

        var (shop, stock, npc) = resolved;
        if (!IsWithinShopRange(player, npc))
        {
            SendShopFailure(player, ResultCode.TooFarAway);
            return;
        }

        var defKey = player.Inventory.Find(repair.Container, repair.Slot)?.DefKey;

        var result = ShopSystem.TryRepair(player.Inventory, _items, shop, player.Gold, repair.Container, repair.Slot);
        if (!result.Ok)
        {
            SendShopFailure(player, result.Code);
            return;
        }

        if (defKey is not null)
        {
            // Reparar no toca stock de tienda (no es comprar ni vender): 1 unidad, sin entrada de stock.
            ApplySuccessfulShopOp(player, result, EconomyLogKind.Repair, shop, stock, defKey, quantity: 1, touchesStock: false);
        }
    }

    private void HandleShopClose(in WorldMessage message)
    {
        var player = FindPlayer(message.SessionId);
        if (player is not null)
        {
            player.OpenShopNpcEntityId = null;
        }
    }

    /// <summary>Busca al jugador de una sesión junto con la zona en la que está (para mirar sus NPCs).</summary>
    private (Zone Zone, PlayerEntity Player)? FindPlayerZone(int sessionId)
    {
        foreach (var zone in _zones.Values)
        {
            var player = zone.FindBySession(sessionId);
            if (player is not null)
            {
                return (zone, player);
            }
        }

        return null;
    }

    /// <summary>
    /// La tienda que el jugador dice tener abierta, revalidada de verdad contra la zona actual —
    /// no basta con confiar en <c>OpenShopNpcEntityId</c>: el NPC podría no estar ya en esta zona
    /// si algo (fuera de alcance hoy) lo hubiera hecho desaparecer.
    /// </summary>
    private (ShopDefinition Shop, IReadOnlyDictionary<string, ShopStockState> Stock, NpcEntity Npc)? ResolveOpenShop(
        Zone zone, PlayerEntity player)
    {
        if (player.OpenShopNpcEntityId is not { } npcId ||
            !zone.Entities.TryGetValue(npcId, out var entity) ||
            entity is not NpcEntity npc)
        {
            SendShopFailure(player, ResultCode.ShopNotOpen);
            return null;
        }

        if (!_shops.TryGet(npc.ShopKey, out var shop) || !_shopRuntime.TryGetShopStock(npc.ShopKey, out var stock))
        {
            _log.Error("NPC {EntityId} referencia la tienda desconocida {ShopKey}", npc.Id, npc.ShopKey);
            SendShopFailure(player, ResultCode.UnknownError);
            return null;
        }

        return (shop, stock, npc);
    }

    private static bool IsWithinShopRange(PlayerEntity player, WorldEntity npc) =>
        Vec2.DistanceSquared(player.State.Pos, npc.State.Pos) <= ShopInteractionRangeTiles * ShopInteractionRangeTiles;

    private static bool IsWithinFarmRange(PlayerEntity player, FarmTileState tile)
    {
        var tileCenter = new Vec2(tile.TileX + 0.5f, tile.TileY + 0.5f);
        return Vec2.DistanceSquared(player.State.Pos, tileCenter) <= FarmInteractionRangeTiles * FarmInteractionRangeTiles;
    }

    private static void SendShopFailure(PlayerEntity player, ResultCode code) =>
        player.Peer.Send(Opcode.ShopResult, new S2CShopResult { Ok = false, Code = code });

    private static S2CShopData BuildShopData(ShopDefinition shop, IReadOnlyDictionary<string, ShopStockState> stock) => new()
    {
        ShopKey = shop.Key,
        DisplayName = shop.DisplayName,
        CanRepair = shop.CanRepair,
        Slots = [.. shop.Items.Select(item =>
        {
            var state = stock[item.DefKey];
            return new ShopSlotInfo
            {
                DefKey = item.DefKey,
                PriceBuy = state.PriceBuyOverride ?? item.PriceBuy,
                PriceSell = state.PriceSellOverride ?? item.PriceSell,
                Stock = state.Stock ?? -1,
            };
        })],
    };

    /// <summary>
    /// El desenlace de una compra/venta/reparación con éxito: aplica el oro y los huecos de
    /// inventario tocados (reutilizando <see cref="ApplyResult"/> de la Fase 6 tal cual — una
    /// reparación puede tocar un ítem equipado, así que también puede disparar
    /// <c>EquipmentUpdate</c> si la durabilidad formara parte de los stats derivados, cosa que hoy
    /// no hace, pero el camino ya está ahí sin más código) y encola el log + el stock nuevo.
    /// </summary>
    private void ApplySuccessfulShopOp(
        PlayerEntity player, ShopOpResult result, EconomyLogKind kind, ShopDefinition shop,
        IReadOnlyDictionary<string, ShopStockState> stock, string defKey, int quantity, bool touchesStock = true)
    {
        var goldDelta = result.NewGold - player.Gold;
        player.Gold = result.NewGold;

        if (goldDelta != 0)
        {
            player.GoldDirty = true;
            player.Peer.Send(Opcode.CurrencyUpdate, new S2CCurrencyUpdate { Gold = player.Gold });
        }

        if (result.InventoryTouched.Count > 0)
        {
            ApplyResult(player, new InventoryOpResult(true, ResultCode.Ok, result.InventoryTouched));
        }

        if (goldDelta == 0 && result.InventoryTouched.Count == 0)
        {
            // P. ej. reparar algo ya al máximo: éxito, pero no hay nada que loguear ni persistir.
            return;
        }

        var shopItem = touchesStock ? Array.Find(shop.Items, item => item.DefKey == defKey) : null;
        var state = touchesStock && stock.TryGetValue(defKey, out var found) ? found : null;

        _economySink.Enqueue(new EconomySave(
            kind, player.CharacterId, defKey, quantity, goldDelta, player.Gold,
            shop.Key, state?.Stock, shopItem?.StockMax, state?.RestockAt));
    }

    /// <summary>
    /// Repone lo que toque de cada tienda (FASE-07 §2 D8) una vez por segundo, no cada tick: el
    /// horario es real (minutos), comprobarlo a 20 Hz sería puro desperdicio. A quien tenga esa
    /// tienda abierta se le manda un <c>ShopData</c> fresco: si no, vería un stock repuesto que
    /// su pantalla no refleja hasta que la cierre y la vuelva a abrir.
    /// </summary>
    private void SweepRestock(long tick)
    {
        if (tick % SimulationConstants.TickRate != 0)
        {
            return;
        }

        var restocked = _shopRuntime.SweepRestock(_shops, DateTimeOffset.UtcNow);
        if (restocked.Count == 0)
        {
            return;
        }

        foreach (var shopKey in restocked)
        {
            if (!_shops.TryGet(shopKey, out var shop) || !_shopRuntime.TryGetShopStock(shopKey, out var stock))
            {
                continue;
            }

            foreach (var item in shop.Items)
            {
                var state = stock[item.DefKey];
                _economySink.Enqueue(EconomySave.Restock(shopKey, item.DefKey, state.Stock ?? 0, item.StockMax ?? 0, state.RestockAt));
            }

            foreach (var zone in _zones.Values)
            {
                foreach (var player in zone.Players)
                {
                    if (player.OpenShopNpcEntityId is { } npcId &&
                        zone.Entities.TryGetValue(npcId, out var entity) &&
                        entity is NpcEntity npc && npc.ShopKey == shopKey)
                    {
                        player.Peer.Send(Opcode.ShopData, BuildShopData(shop, stock));
                    }
                }
            }
        }
    }

    // ── Granja ───────────────────────────────────────────────────────────

    private void HandleFarmTill(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SFarmTill>(message.Payload, out var till) || till is null)
        {
            _log.Warning("FarmTill ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;
        if (ResolveFarmTile(zone, till.TileX, till.TileY, message.SessionId) is not { } resolved)
        {
            return;
        }

        var (plot, tile) = resolved;
        if (!IsWithinFarmRange(player, tile))
        {
            SendFarmFailure(player, ResultCode.TooFarAway);
            return;
        }

        var result = FarmSystem.TryTill(tile, player.Inventory, _items);
        if (!result.Ok)
        {
            SendFarmFailure(player, result.Code);
            return;
        }

        BroadcastFarmTileUpdate(plot);
        _farmSink.Enqueue(FarmTileSave.From(plot.PlotId, tile));
    }

    private void HandleFarmPlant(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SFarmPlant>(message.Payload, out var plant) || plant is null)
        {
            _log.Warning("FarmPlant ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (!InventoryConstants.IsWellFormedSlot(plant.Container, plant.Slot))
        {
            _log.Warning("FarmPlant con hueco imposible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;
        if (ResolveFarmTile(zone, plant.TileX, plant.TileY, message.SessionId) is not { } resolved)
        {
            return;
        }

        var (plot, tile) = resolved;
        if (!IsWithinFarmRange(player, tile))
        {
            SendFarmFailure(player, ResultCode.TooFarAway);
            return;
        }

        var result = FarmSystem.TryPlant(
            tile, player.Inventory, _items, _crops, plant.Container, plant.Slot, DateTimeOffset.UtcNow);
        if (!result.Ok)
        {
            SendFarmFailure(player, result.Code);
            return;
        }

        if (result.InventoryTouched.Count > 0)
        {
            ApplyResult(player, new InventoryOpResult(true, ResultCode.Ok, result.InventoryTouched));
        }

        BroadcastFarmTileUpdate(plot);
        _farmSink.Enqueue(FarmTileSave.From(plot.PlotId, tile));
    }

    private void HandleFarmWater(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SFarmWater>(message.Payload, out var water) || water is null)
        {
            _log.Warning("FarmWater ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;
        if (ResolveFarmTile(zone, water.TileX, water.TileY, message.SessionId) is not { } resolved)
        {
            return;
        }

        var (plot, tile) = resolved;
        if (!IsWithinFarmRange(player, tile))
        {
            SendFarmFailure(player, ResultCode.TooFarAway);
            return;
        }

        var result = FarmSystem.TryWater(tile, player.Inventory, _items, DateTimeOffset.UtcNow);
        if (!result.Ok)
        {
            SendFarmFailure(player, result.Code);
            return;
        }

        BroadcastFarmTileUpdate(plot);
        _farmSink.Enqueue(FarmTileSave.From(plot.PlotId, tile));
    }

    private void HandleFarmHarvest(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SFarmHarvest>(message.Payload, out var harvest) || harvest is null)
        {
            _log.Warning("FarmHarvest ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;
        if (ResolveFarmTile(zone, harvest.TileX, harvest.TileY, message.SessionId) is not { } resolved)
        {
            return;
        }

        var (plot, tile) = resolved;
        if (!IsWithinFarmRange(player, tile))
        {
            SendFarmFailure(player, ResultCode.TooFarAway);
            return;
        }

        var result = FarmSystem.TryHarvest(tile, player.Inventory, _items, _crops);
        if (!result.Ok)
        {
            SendFarmFailure(player, result.Code);
            return;
        }

        if (result.InventoryTouched.Count > 0)
        {
            ApplyResult(player, new InventoryOpResult(true, ResultCode.Ok, result.InventoryTouched));
        }

        BroadcastFarmTileUpdate(plot);
        _farmSink.Enqueue(FarmTileSave.From(plot.PlotId, tile));
    }

    /// <summary>
    /// El tile de una acción de granja, revalidado contra la parcela de verdad. Un cliente
    /// honesto sólo actúa sobre tiles que ha visto en un <c>FarmTileUpdate</c> — todos dentro de
    /// alguna parcela conocida (FASE-08 §2 D6): si no lo está, no es una jugada legal que falla,
    /// es un dato imposible con el protocolo cerrado, igual que un <c>InputState</c> con
    /// dirección fuera de rango (Fase 4).
    /// </summary>
    private (FarmPlotRuntime Plot, FarmTileState Tile)? ResolveFarmTile(Zone zone, int tileX, int tileY, int sessionId)
    {
        var plot = _farmRuntime.FindPlotContaining(zone.Map.Key, tileX, tileY);
        if (plot is null || !plot.Tiles.TryGetValue((tileX, tileY), out var tile))
        {
            _log.Warning("Acción de granja sobre el tile ({X}, {Y}) fuera de cualquier parcela en la sesión {SessionId}",
                tileX, tileY, sessionId);
            KickSession(sessionId, KickReason.ProtocolError);
            return null;
        }

        return (plot, tile);
    }

    private static void SendFarmFailure(PlayerEntity player, ResultCode code) => player.Peer.Send(
        Opcode.SystemMessage, new S2CSystemMessage { Severity = 0, Key = $"farm.{code}", Args = [] });

    private void BroadcastFarmTileUpdate(FarmPlotRuntime plot)
    {
        if (!_zones.TryGetValue(plot.MapKey, out var zone))
        {
            return;
        }

        var update = BuildFarmTileUpdate(plot, DateTimeOffset.UtcNow);
        foreach (var player in zone.Players)
        {
            player.Peer.Send(Opcode.FarmTileUpdate, update);
        }
    }

    private S2CFarmTileUpdate BuildFarmTileUpdate(FarmPlotRuntime plot, DateTimeOffset now) => new()
    {
        Tiles = [.. plot.Tiles.Values.Select(tile => new FarmTileInfo
        {
            TileX = tile.TileX,
            TileY = tile.TileY,
            State = tile.Status,
            CropKey = tile.CropKey,
            Stage = ComputeStage(tile),
            Watered = tile.WateredAt is not null,
            MsRemaining = tile.EtaAt is { } eta ? Math.Max(0, (long)(eta - now).TotalMilliseconds) : 0,
        })],
    };

    private byte ComputeStage(FarmTileState tile)
    {
        if (tile.CropKey is null || !_crops.TryGet(tile.CropKey, out var crop) || crop.Stages.Length == 0)
        {
            return 0;
        }

        var fraction = tile.GrowthNeeded <= 0 ? 1f : Math.Clamp(tile.GrowthDays / tile.GrowthNeeded, 0f, 1f);
        var index = (int)(fraction * crop.Stages.Length);
        return (byte)Math.Min(index, crop.Stages.Length - 1);
    }

    /// <summary>
    /// Cierra tantos días de granja como hayan pasado desde el último barrido (recuperación de
    /// días perdidos, FASE-08 §2 D1), una vez por segundo — el límite es de reloj de pared
    /// (05:00 UTC), comprobarlo a 20 Hz sería puro desperdicio, mismo criterio que
    /// <see cref="SweepRestock"/>.
    /// </summary>
    private void SweepFarmGrowth(long tick)
    {
        if (tick % SimulationConstants.TickRate != 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var currentDay = FarmCalendar.DayIndex(now);

        while (_farmRuntime.LastProcessedDayIndex < currentDay)
        {
            var dayBoundaryEnd = FarmCalendar.BoundaryOf(_farmRuntime.LastProcessedDayIndex + 1);
            var changed = _farmRuntime.ApplyDailyGrowth(dayBoundaryEnd);
            _farmRuntime.LastProcessedDayIndex++;

            foreach (var (plot, tile) in changed)
            {
                _farmSink.Enqueue(FarmTileSave.From(plot.PlotId, tile));
            }

            _farmSink.Enqueue(FarmTileSave.Calendar(_farmRuntime.LastProcessedDayIndex));

            foreach (var plot in changed.Select(c => c.Plot).Distinct())
            {
                BroadcastFarmTileUpdate(plot);
            }
        }
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
        if (!player.PositionDirty && !player.GoldDirty && !force)
        {
            return;
        }

        _positions.Enqueue(new PositionSave(
            player.CharacterId,
            zone.Map.Key,
            player.State.Pos.X,
            player.State.Pos.Y,
            player.State.Facing,
            player.Gold));

        player.PositionDirty = false;
        player.GoldDirty = false;
    }
}

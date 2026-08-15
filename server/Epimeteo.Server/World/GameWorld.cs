using Epimeteo.Server.Chat;
using Epimeteo.Server.Combat;
using Epimeteo.Server.Content;
using Epimeteo.Server.Farm;
using Epimeteo.Server.Inventory;
using Epimeteo.Server.Persistence.Admin;
using Epimeteo.Server.Persistence.Chat;
using Epimeteo.Server.Persistence.Economy;
using Epimeteo.Server.Persistence.Combat;
using Epimeteo.Server.Persistence.Farm;
using Epimeteo.Server.Persistence.Items;
using Epimeteo.Server.Security;
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

    /// <summary>Radio para coger de un saco de loot, en tiles. Mismo criterio que tiendas y granja.</summary>
    private const float LootRangeTiles = 2f;

    private readonly Dictionary<string, Zone> _zones = new(StringComparer.Ordinal);
    private readonly WorldInbox _inbox;
    private readonly ICharacterSink _characters;
    private readonly ItemCatalog _items;
    private readonly ClassCatalog _classes;
    private readonly IInventorySink _inventorySink;
    private readonly ShopCatalog _shops;
    private readonly ShopRuntime _shopRuntime;
    private readonly IEconomySink _economySink;
    private readonly CropCatalog _crops;
    private readonly FarmRuntime _farmRuntime;
    private readonly IFarmSink _farmSink;
    private readonly MonsterCatalog _monsters;
    private readonly SkillCatalog _skills;
    private readonly ICombatLogSink _combatLogSink;
    private readonly IChatLogSink _chatLogSink;
    private readonly IAdminActionSink _adminActionSink;
    private readonly EntityIdAllocator _entityIds;
    private readonly Dictionary<string, MonsterSpawner> _spawners = new(StringComparer.Ordinal);
    private readonly int _saveIntervalTicks;
    private readonly string _fallbackMapKey;
    private readonly ILogger _log = Log.ForContext<GameWorld>();

    public GameWorld(
        MapCatalog maps,
        WorldInbox inbox,
        ICharacterSink characters,
        ItemCatalog items,
        ClassCatalog classes,
        IInventorySink inventorySink,
        ShopCatalog shops,
        ShopRuntime shopRuntime,
        IEconomySink economySink,
        CropCatalog crops,
        FarmRuntime farmRuntime,
        IFarmSink farmSink,
        MonsterCatalog monsters,
        SkillCatalog skills,
        ICombatLogSink combatLogSink,
        IChatLogSink chatLogSink,
        IAdminActionSink adminActionSink,
        EntityIdAllocator entityIds,
        int saveIntervalSeconds = 30)
    {
        _inbox = inbox;
        _characters = characters;
        _items = items;
        _classes = classes;
        _inventorySink = inventorySink;
        _shops = shops;
        _shopRuntime = shopRuntime;
        _economySink = economySink;
        _crops = crops;
        _farmRuntime = farmRuntime;
        _farmSink = farmSink;
        _monsters = monsters;
        _skills = skills;
        _combatLogSink = combatLogSink;
        _chatLogSink = chatLogSink;
        _adminActionSink = adminActionSink;
        _entityIds = entityIds;
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
            _spawners[map.Key] = new MonsterSpawner(monsters, map);
        }

        _fallbackMapKey = _zones.ContainsKey("map.village") ? "map.village" : _zones.Keys.First();
    }

    /// <summary>Zonas simuladas, una por mapa cargado.</summary>
    public IReadOnlyCollection<Zone> Zones => _zones.Values;

    /// <summary>Jugadores dentro del mundo, sumando todas las zonas.</summary>
    public int PlayerCount => _zones.Values.Sum(zone => zone.Players.Count);

    /// <summary>Entidades vivas, sumando todas las zonas.</summary>
    public int EntityCount => _zones.Values.Sum(zone => zone.Entities.Count);

    /// <summary>Monstruos vivos, sumando todas las zonas. Aparece en <c>/status</c>.</summary>
    public int MonsterCount => _zones.Values.Sum(zone => zone.Monsters.Count);

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

        ResolveMonsterAttacks(nowMs);

        SweepSaves(tick);
        SweepRestock(tick);
        SweepFarmGrowth(tick);
        SweepCombat(tick, nowMs);
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

    /// <summary>
    /// Saca a un jugador del mundo, salvo que tenga puesto el flag de combate PvP: entonces la
    /// entidad se queda viva y atacable hasta que expire, y la saca <see cref="SweepCombat"/>
    /// (<c>docs/00 §6.2</c>, FASE-09 §2 D11). Sin esto, "me van a matar" se resuelve con Alt+F4.
    /// <para>
    /// El estado se guarda ya, en el momento de pedir la salida: si el proceso se cae durante esos
    /// 10 s, no se pierde nada.
    /// </para>
    /// </summary>
    private bool RemoveFrom(Zone zone, int sessionId, bool force = false)
    {
        if (!force && zone.FindBySession(sessionId) is { } pending && pending.IsInCombat(ServerClock.NowMs))
        {
            if (pending.PendingLeaveAtMs is null)
            {
                pending.PendingLeaveAtMs = pending.CombatFlagUntilMs;
                _log.Information(
                    "El personaje {CharacterId} pidió salir en combate; se queda {Ms} ms más en el mundo",
                    pending.CharacterId, pending.CombatFlagUntilMs - ServerClock.NowMs);

                Save(zone, pending, force: true);
                SaveInventory(pending);
            }

            return true;
        }

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

                case Opcode.Attack:
                    HandleAttack(message, nowMs);
                    break;

                case Opcode.LootTake:
                    HandleLootTake(message, nowMs);
                    break;

                case Opcode.SkillCast:
                    HandleSkillCast(message, nowMs);
                    break;

                case Opcode.AllocateStatPoint:
                    HandleAllocateStatPoint(message);
                    break;

                case Opcode.ChatSend:
                    HandleChatSend(message, nowMs);
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


    // ── Combate ──────────────────────────────────────────────────────────

    private void HandleAttack(in WorldMessage message, long nowMs)
    {
        if (!FrameCodec.TryDecodeBody<C2SAttack>(message.Payload, out var attack) || attack is null)
        {
            _log.Warning("Attack ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;

        if (player.IsDead)
        {
            SendCombatFailure(player, ResultCode.CannotAttackTarget);
            return;
        }

        if (!zone.Entities.TryGetValue(attack.TargetEntityId, out var target))
        {
            SendCombatFailure(player, ResultCode.TargetNotFound);
            return;
        }

        // Compensación de latencia: sólo para el alcance, y con el RTT que ha medido el propio
        // servidor (FASE-09 §2 D1 y D2). Los flags de zona los mira CombatSystem contra la
        // posición actual, no contra ésta.
        var rewindMs = PositionHistory.RewindFor(player.Peer.RttMs);
        var rangePos = target is PlayerEntity victim
            ? victim.History.PositionAt(nowMs, rewindMs, victim.State.Pos)
            : target.State.Pos;

        var isPvp = target is PlayerEntity;
        var cooldownReady = nowMs - player.LastAttackMs >= CombatConstants.AttackCooldownMs;

        var verdict = CombatSystem.ValidateAttack(
            player, target, rangePos, zone.Map, CombatConstants.MeleeRangeTiles, isPvp, cooldownReady);

        if (verdict != ResultCode.Ok)
        {
            SendCombatFailure(player, verdict);
            return;
        }

        player.LastAttackMs = nowMs;
        ResolveHit(zone, player, target, nowMs);
    }

    /// <summary>
    /// Los ataques que decidió la IA este tick. Pasan por exactamente la misma validación que los
    /// de un jugador — un monstruo tampoco pega a través de un muro ni fuera de alcance.
    /// </summary>
    private void ResolveMonsterAttacks(long nowMs)
    {
        foreach (var zone in _zones.Values)
        {
            foreach (var (monster, targetId) in zone.PendingMonsterAttacks)
            {
                if (!zone.Entities.TryGetValue(targetId, out var target))
                {
                    continue;
                }

                var verdict = CombatSystem.ValidateAttack(
                    monster, target, target.State.Pos, zone.Map,
                    monster.Definition.AttackRangeTiles, requirePvpZone: false, cooldownReady: true);

                if (verdict != ResultCode.Ok)
                {
                    continue;
                }

                monster.LastAttackMs = nowMs;
                ResolveHit(zone, monster, target, nowMs);
            }
        }
    }

    /// <summary>Aplica un golpe ya validado: daño, aviso a los testigos, amenaza y muerte si toca.</summary>
    private void ResolveHit(Zone zone, WorldEntity attacker, WorldEntity target, long nowMs, int powerBonus = 0, string? skillKey = null)
    {
        var hit = CombatSystem.ApplyHit(attacker, target, zone.Rng, powerBonus);

        BroadcastCombatEvent(zone, target, new S2CCombatEvent
        {
            AttackerId = attacker.Id,
            TargetId = target.Id,
            Kind = CombatEventKind.Damage,
            Amount = hit.Damage,
            Flags = hit.Critical ? CombatEventFlags.Critical : CombatEventFlags.None,
            SkillKey = skillKey,
        });

        if (target is MonsterEntity monster)
        {
            // La amenaza es el daño hecho: quien más pega, manda (FASE-09 §2 D6).
            monster.Aggro.Add(attacker.Id, hit.Damage);

            if (monster.AiState is MonsterState.Idle or MonsterState.Patrol)
            {
                monster.AiState = MonsterState.Chase;
            }
        }

        if (target is PlayerEntity victim)
        {
            victim.VitalsDirty = true;

            // Sólo el PvP pone el flag de combate: que te muerda un lobo no debe impedirte salir
            // del juego (docs/00 §6.2 habla de combate PvP).
            if (attacker is PlayerEntity aggressor)
            {
                FlagCombat(aggressor, nowMs);
                FlagCombat(victim, nowMs);
            }
        }

        BroadcastEntityStats(zone, target);

        if (!target.IsAlive)
        {
            HandleDeath(zone, attacker, target, nowMs);
        }
    }

    private void HandleDeath(Zone zone, WorldEntity killer, WorldEntity target, long nowMs)
    {
        BroadcastToZone(zone, Opcode.EntityDeath, new S2CEntityDeath { Id = target.Id, KillerId = killer.Id });

        switch (target)
        {
            case MonsterEntity monster:
                HandleMonsterDeath(zone, killer, monster, nowMs);
                break;

            case PlayerEntity victim:
                HandlePlayerDeath(zone, killer, victim, nowMs);
                break;

            default:
                break;
        }
    }

    private void HandleMonsterDeath(Zone zone, WorldEntity killer, MonsterEntity monster, long nowMs)
    {
        // La XP va a quien más amenaza acumuló, que con la amenaza-por-daño es quien más pegó.
        var topId = monster.Aggro.Top();
        var winner = topId is null ? null : zone.Entities.GetValueOrDefault(topId.Value) as PlayerEntity;
        winner ??= killer as PlayerEntity;

        if (winner is not null)
        {
            GrantXp(zone, winner, monster.Definition.XpReward);
        }

        SpawnLootBag(zone, monster, winner, nowMs);

        _spawners[zone.Map.Key].NotifyDeath(monster, nowMs);
        zone.RemoveEntity(monster.Id, DespawnReason.Death);
    }

    private void HandlePlayerDeath(Zone zone, WorldEntity killer, PlayerEntity victim, long nowMs)
    {
        victim.IsDead = true;
        victim.VitalsDirty = true;

        // Sin drop de inventario: el full-loot ahuyenta a los nuevos (docs/00 §6.3). La
        // penalización es XP, y sólo en PvP — morir contra un monstruo ya cuesta el viaje de vuelta.
        if (killer is PlayerEntity aggressor)
        {
            var lost = (long)(victim.Xp * CombatConstants.PvpXpLossFraction);
            victim.Xp = Math.Max(0, victim.Xp - lost);
            SendXpUpdate(victim, leveledUp: false);

            _combatLogSink.Enqueue(new CombatLogSave(
                victim.CharacterId,
                aggressor.CharacterId,
                zone.Map.Key,
                zone.Map.Regions.Resolve(victim.State.Pos).Name,
                victim.Level,
                aggressor.Level,
                lost));

            _log.Information("PvP: {KillerName} mató a {VictimName} en {Region}; {XpLost} XP perdidos",
                aggressor.Name, victim.Name, zone.Map.Regions.Resolve(victim.State.Pos).Name, lost);
        }

        Respawn(zone, victim, nowMs);
    }

    /// <summary>Reaparición en el pueblo con parte de la vida (<c>docs/00 §6.3</c>).</summary>
    private void Respawn(Zone zone, PlayerEntity victim, long nowMs)
    {
        victim.IsDead = false;
        victim.Hp = Math.Max(1, (int)(victim.HpMax * CombatConstants.RespawnHpFraction));
        victim.CombatFlagUntilMs = 0;
        victim.PositionDirty = true;
        victim.VitalsDirty = true;

        zone.Teleport(victim, zone.Map.Spawn, nowMs);

        BroadcastEntityStats(zone, victim);
        SendCombatFlag(victim, nowMs);
    }

    private void SpawnLootBag(Zone zone, MonsterEntity monster, PlayerEntity? owner, long nowMs)
    {
        var slots = new List<LootSlot>();

        foreach (var entry in monster.Definition.Loot)
        {
            if (!zone.Rng.NextChance(entry.Chance))
            {
                continue;
            }

            slots.Add(new LootSlot
            {
                DefKey = entry.DefKey,
                Quantity = zone.Rng.NextInt(entry.Min, entry.Max + 1),
            });
        }

        if (slots.Count == 0)
        {
            return;
        }

        var bag = new LootBagEntity(
            _entityIds.Next(),
            monster.State.Pos,
            slots,
            owner?.CharacterId ?? 0,
            nowMs + (CombatConstants.LootRightsSeconds * 1000L),
            nowMs + (CombatConstants.LootDespawnSeconds * 1000L));

        zone.AddEntity(bag);
        BroadcastToZone(zone, Opcode.LootDrop, bag.ToDropMessage());
    }

    private void HandleLootTake(in WorldMessage message, long nowMs)
    {
        if (!FrameCodec.TryDecodeBody<C2SLootTake>(message.Payload, out var take) || take is null)
        {
            _log.Warning("LootTake ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;

        if (!zone.Entities.TryGetValue(take.LootEntityId, out var entity) || entity is not LootBagEntity bag)
        {
            SendCombatFailure(player, ResultCode.TargetNotFound);
            return;
        }

        if (!CombatFormulas.IsWithinRange(player.State.Pos, bag.State.Pos, LootRangeTiles))
        {
            SendCombatFailure(player, ResultCode.TooFarAway);
            return;
        }

        // Derecho de saqueo: durante el periodo exclusivo sólo su dueño (FASE-09 §2 D9).
        if (!bag.CanTake(player.CharacterId, nowMs))
        {
            SendCombatFailure(player, ResultCode.CannotAttackTarget);
            return;
        }

        if (take.Slot >= bag.Slots.Count || bag.Slots[take.Slot].IsEmpty)
        {
            SendCombatFailure(player, ResultCode.ItemNotFound);
            return;
        }

        var slot = bag.Slots[take.Slot];
        var result = InventorySystem.TryAddNew(player.Inventory, _items, slot.DefKey, slot.Quantity);
        if (!result.Ok)
        {
            SendCombatFailure(player, result.Code);
            return;
        }

        _economySink.Enqueue(new EconomySave(
            EconomyLogKind.Loot, player.CharacterId, slot.DefKey, slot.Quantity, 0, player.Gold, null, null, null, null));

        slot.Quantity = 0;
        ApplyResult(player, result);

        if (bag.IsEmpty)
        {
            zone.RemoveEntity(bag.Id, DespawnReason.OutOfRange);
        }
        else
        {
            BroadcastToZone(zone, Opcode.LootDrop, bag.ToDropMessage());
        }
    }

    private void HandleSkillCast(in WorldMessage message, long nowMs)
    {
        if (!FrameCodec.TryDecodeBody<C2SSkillCast>(message.Payload, out var cast) || cast is null ||
            string.IsNullOrEmpty(cast.SkillKey))
        {
            _log.Warning("SkillCast ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;

        if (player.IsDead)
        {
            SendCombatFailure(player, ResultCode.CannotAttackTarget);
            return;
        }

        // Un cliente honesto sólo lanza habilidades de su propia clase, las que le mandó el
        // contenido: pedir otra es un dato imposible con el protocolo cerrado, no una jugada que
        // falla (mismo criterio que un EquipSlot no definido en la Fase 6).
        if (!_skills.TryGet(cast.SkillKey, out var skill) || skill.ClassKey != player.DefKey)
        {
            _log.Warning("SkillCast con habilidad ajena o desconocida '{SkillKey}' en la sesión {SessionId}",
                cast.SkillKey, message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var verdict = SkillSystem.ValidateCast(player, skill, nowMs);
        if (verdict != ResultCode.Ok)
        {
            SendCombatFailure(player, verdict);
            return;
        }

        if (skill.Kind == CombatEventKind.Heal)
        {
            CastHeal(zone, player, skill, nowMs);
            return;
        }

        if (!zone.Entities.TryGetValue(cast.TargetEntityId, out var target))
        {
            SendCombatFailure(player, ResultCode.TargetNotFound);
            return;
        }

        // Alcance/zona/línea de visión, las mismas reglas que un ataque básico (FASE-09 §2 D3),
        // sólo con el alcance de la habilidad; el cooldown ya se comprobó aparte (D7), así que
        // aquí siempre se le dice que sí está listo.
        var isPvp = target is PlayerEntity;
        var zoneVerdict = CombatSystem.ValidateAttack(
            player, target, target.State.Pos, zone.Map, skill.RangeTiles, isPvp, cooldownReady: true);

        if (zoneVerdict != ResultCode.Ok)
        {
            SendCombatFailure(player, zoneVerdict);
            return;
        }

        player.Mp -= skill.ManaCost;
        player.SkillCooldowns[skill.Key] = nowMs + skill.CooldownMs;
        player.VitalsDirty = true;

        ResolveHit(zone, player, target, nowMs, skill.Power, skill.Key);
    }

    /// <summary>Una curación siempre se apunta a quien la lanza; el objetivo que mandó el cliente se ignora (FASE-10 §2 D9).</summary>
    private void CastHeal(Zone zone, PlayerEntity caster, SkillDefinition skill, long nowMs)
    {
        caster.Mp -= skill.ManaCost;
        caster.SkillCooldowns[skill.Key] = nowMs + skill.CooldownMs;

        // Sin RNG (D8): a diferencia del daño, curar depende del contenido, no de la suerte.
        var healed = Math.Min(skill.Power, caster.HpMax - caster.Hp);
        caster.Hp += healed;
        caster.VitalsDirty = true;

        BroadcastCombatEvent(zone, caster, new S2CCombatEvent
        {
            AttackerId = caster.Id,
            TargetId = caster.Id,
            Kind = CombatEventKind.Heal,
            Amount = healed,
            Flags = CombatEventFlags.None,
            SkillKey = skill.Key,
        });

        BroadcastEntityStats(zone, caster);
    }

    private void HandleAllocateStatPoint(in WorldMessage message)
    {
        if (!FrameCodec.TryDecodeBody<C2SAllocateStatPoint>(message.Payload, out var allocate) || allocate is null ||
            !Enum.IsDefined(allocate.Stat))
        {
            _log.Warning("AllocateStatPoint ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        var player = FindPlayer(message.SessionId);
        if (player is null)
        {
            return;
        }

        var code = LevelingSystem.TryAllocateStatPoint(player, allocate.Stat);
        if (code != ResultCode.Ok)
        {
            SendProgressionFailure(player, code);
            return;
        }

        player.VitalsDirty = true;
        SendEquipmentUpdate(player);
    }

    private static void SendProgressionFailure(PlayerEntity player, ResultCode code) =>
        SendFailure(player, "progression", code);

    /// <summary>
    /// Un <c>ChatSend</c>: mensaje normal o comando de barra, según decida
    /// <see cref="ChatCommandParser"/> (FASE-11 §2 D1/D3). El cliente no sabe qué comandos
    /// existen — sólo manda texto.
    /// </summary>
    private void HandleChatSend(in WorldMessage message, long nowMs)
    {
        if (!FrameCodec.TryDecodeBody<C2SChatSend>(message.Payload, out var chat) || chat is null ||
            !Enum.IsDefined(chat.Channel) || chat.Text.Length is 0 or > ChatConstants.MaxMessageLength)
        {
            _log.Warning("ChatSend ilegible en la sesión {SessionId}", message.SessionId);
            KickSession(message.SessionId, KickReason.ProtocolError);
            return;
        }

        if (FindPlayerZone(message.SessionId) is not { } found)
        {
            return;
        }

        var (zone, player) = found;

        switch (ChatCommandParser.Parse(chat.Channel, chat.Text))
        {
            // Un cliente honesto sólo manda Global/Zone para un mensaje normal — Whisper/System
            // los pone el servidor. Mandar otra cosa es un dato imposible con el protocolo
            // cerrado, no una jugada que falla (mismo criterio que un EquipSlot inválido, Fase 6).
            case ChatCommand.Say say when say.Channel is ChatChannel.Global or ChatChannel.Zone:
                HandleSay(zone, player, say, nowMs);
                break;

            case ChatCommand.Say:
                _log.Warning("ChatSend con canal Whisper/System puesto por el cliente en la sesión {SessionId}", message.SessionId);
                KickSession(message.SessionId, KickReason.ProtocolError);
                break;

            case ChatCommand.Whisper whisper:
                HandleWhisper(player, whisper, nowMs);
                break;

            case ChatCommand.Who:
                HandleWho(player);
                break;

            case ChatCommand.Help:
                HandleHelp(player);
                break;

            case ChatCommand.Kick kick:
                HandleKick(player, kick);
                break;

            case ChatCommand.Ban ban:
                HandleBan(player, ban);
                break;

            case ChatCommand.Teleport teleport:
                HandleTeleport(zone, player, teleport, nowMs);
                break;

            case ChatCommand.Give give:
                HandleGive(player, give);
                break;

            case ChatCommand.Heal heal:
                HandleHeal(player, heal);
                break;

            case ChatCommand.GrantXp grantXp:
                HandleGrantXp(player, grantXp);
                break;

            case ChatCommand.Invalid invalid:
                SendChatFailure(player, invalid.Code);
                break;
        }
    }

    private void HandleSay(Zone zone, PlayerEntity player, ChatCommand.Say say, long nowMs)
    {
        _chatLogSink.Enqueue(new ChatLogSave(player.CharacterId, say.Channel, say.Text));

        var outgoing = new S2CChatMessage
        {
            Channel = say.Channel,
            SenderName = player.Name,
            Text = ChatFilter.Censor(say.Text),
            ServerTimeMs = nowMs,
        };

        if (say.Channel == ChatChannel.Zone)
        {
            BroadcastToZone(zone, Opcode.ChatMessage, outgoing);
        }
        else
        {
            BroadcastToWorld(Opcode.ChatMessage, outgoing);
        }
    }

    /// <summary><c>/w Nombre mensaje</c>: sólo le llega al destinatario (y de vuelta a quien lo mandó, como eco).</summary>
    private void HandleWhisper(PlayerEntity sender, ChatCommand.Whisper whisper, long nowMs)
    {
        var target = FindPlayerByName(whisper.TargetName);
        if (target is null)
        {
            SendChatFailure(sender, ResultCode.TargetNotFound);
            return;
        }

        _chatLogSink.Enqueue(new ChatLogSave(sender.CharacterId, ChatChannel.Whisper, whisper.Text));

        var outgoing = new S2CChatMessage
        {
            Channel = ChatChannel.Whisper,
            SenderName = sender.Name,
            Text = ChatFilter.Censor(whisper.Text),
            ServerTimeMs = nowMs,
        };

        target.Peer.Send(Opcode.ChatMessage, outgoing);

        if (target.Id != sender.Id)
        {
            sender.Peer.Send(Opcode.ChatMessage, outgoing);
        }
    }

    /// <summary><c>/who</c>: la lista de nombres conectados, en todas las zonas.</summary>
    private void HandleWho(PlayerEntity player)
    {
        var names = _zones.Values.SelectMany(zone => zone.Players).Select(p => p.Name).ToArray();
        player.Peer.Send(Opcode.SystemMessage, new S2CSystemMessage { Severity = 0, Key = "chat.who", Args = names });
    }

    private static readonly string[] HelpCommands =
    [
        "/w <nombre> <mensaje>",
        "/who",
        "/help",
        "/kick <nombre> [motivo]",
        "/ban <nombre> <horas> [motivo]",
        "/teleport <nombre>",
        "/give <nombre> <defKey> <cantidad>",
        "/heal <nombre>",
        "/xp <nombre> <cantidad>",
    ];

    /// <summary><c>/help</c>: la lista fija de arriba. Los de admin salen igual para todos — quien no lo sea sólo verá <c>NotAuthorized</c> si los prueba.</summary>
    private void HandleHelp(PlayerEntity player) =>
        player.Peer.Send(Opcode.SystemMessage, new S2CSystemMessage { Severity = 0, Key = "chat.help", Args = HelpCommands });

    /// <summary>
    /// Expulsa sin banear: la cuenta no queda tocada, puede reconectar en el acto
    /// (FASE-11 §2 D5: el objetivo tiene que estar conectado).
    /// </summary>
    private void HandleKick(PlayerEntity admin, ChatCommand.Kick kick)
    {
        if (!IsAuthorizedAdmin(admin))
        {
            SendChatFailure(admin, ResultCode.NotAuthorized);
            return;
        }

        var target = FindPlayerByName(kick.TargetName);
        if (target is null)
        {
            SendChatFailure(admin, ResultCode.TargetNotFound);
            return;
        }

        LogAdminAction(admin, target, AdminAction.Kick, kick.Reason);
        target.Peer.Kick(KickReason.Banned);
        ConfirmAdminAction(admin, target.Name);
    }

    /// <summary>
    /// Expulsa y deja la cuenta baneada de verdad: <c>AuthService.LoginAsync</c> ya rechaza
    /// <c>AccountStatus.Banned</c>, así que no puede volver a entrar hasta que pase
    /// <see cref="ChatCommand.Ban.Hours"/> (FASE-11 §2 D7).
    /// </summary>
    private void HandleBan(PlayerEntity admin, ChatCommand.Ban ban)
    {
        if (!IsAuthorizedAdmin(admin))
        {
            SendChatFailure(admin, ResultCode.NotAuthorized);
            return;
        }

        var target = FindPlayerByName(ban.TargetName);
        if (target is null)
        {
            SendChatFailure(admin, ResultCode.TargetNotFound);
            return;
        }

        LogAdminAction(admin, target, AdminAction.Ban, ban.Reason, banHours: ban.Hours);
        target.Peer.Kick(KickReason.Banned);
        ConfirmAdminAction(admin, target.Name);
    }

    /// <summary>
    /// Mueve al admin junto al objetivo (FASE-11 §2 D8: al revés que "traer a alguien" es la
    /// convención habitual de herramientas de GM). Sólo busca en la zona del propio admin — cruzar
    /// de zona no está soportado esta fase (FASE-11 §1); con una sola zona hoy, da igual.
    /// </summary>
    private void HandleTeleport(Zone adminZone, PlayerEntity admin, ChatCommand.Teleport teleport, long nowMs)
    {
        if (!IsAuthorizedAdmin(admin))
        {
            SendChatFailure(admin, ResultCode.NotAuthorized);
            return;
        }

        var target = adminZone.Players.FirstOrDefault(
            p => string.Equals(p.Name, teleport.TargetName, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            SendChatFailure(admin, ResultCode.TargetNotFound);
            return;
        }

        adminZone.Teleport(admin, target.State.Pos, nowMs);
        LogAdminAction(admin, target, AdminAction.Teleport, reason: string.Empty);
        ConfirmAdminAction(admin, target.Name);
    }

    /// <summary>Mete un ítem nuevo en la bolsa del objetivo, con la misma validación que cualquier otra alta (loot, compra).</summary>
    private void HandleGive(PlayerEntity admin, ChatCommand.Give give)
    {
        if (!IsAuthorizedAdmin(admin))
        {
            SendChatFailure(admin, ResultCode.NotAuthorized);
            return;
        }

        var target = FindPlayerByName(give.TargetName);
        if (target is null)
        {
            SendChatFailure(admin, ResultCode.TargetNotFound);
            return;
        }

        var result = InventorySystem.TryAddNew(target.Inventory, _items, give.DefKey, give.Quantity);
        if (!result.Ok)
        {
            SendChatFailure(admin, result.Code);
            return;
        }

        ApplyResult(target, result);
        LogAdminAction(admin, target, AdminAction.Give, reason: string.Empty, defKey: give.DefKey, quantity: give.Quantity);
        ConfirmAdminAction(admin, target.Name);
    }

    /// <summary>
    /// Lista blanca de cuentas que de verdad pueden usar los comandos de admin del chat, además de
    /// <c>accounts.is_admin</c> (pedido explícito de sesión: "asegúrate que el único que pueda
    /// hacer eso sea WaterRessistan"). <c>is_admin</c> se concede por SQL a mano y nada impide que
    /// alguien lo active sin querer en otra cuenta el día de mañana; esta lista es la comprobación
    /// de más que hace que aunque eso pase, sólo esta cuenta concreta pueda usarlo — igual que
    /// <c>ServerOptions.MetricsToken</c> es la comprobación de más sobre <c>/status</c>.
    /// <para>
    /// Por username de cuenta, no por nombre de personaje: una cuenta puede tener hasta 5
    /// personajes (CLAUDE.md §1) y el privilegio es de la cuenta, no de cuál de ellos esté jugando
    /// en cada momento.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> AuthorizedAdminUsernames =
        new(StringComparer.OrdinalIgnoreCase) { "WaterRessistan" };

    /// <summary>La comprobación real detrás de todo comando de admin: hace falta <b>las dos cosas</b>, no sólo una.</summary>
    private static bool IsAuthorizedAdmin(PlayerEntity player) =>
        player.IsAdmin && AuthorizedAdminUsernames.Contains(player.Username);

    /// <summary>Busca por nombre en todas las zonas (FASE-11 §2 D4) — no sólo la del que pregunta.</summary>
    private PlayerEntity? FindPlayerByName(string name) => _zones.Values
        .SelectMany(zone => zone.Players)
        .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Igual que <see cref="FindPlayerByName"/> pero también con la zona: <c>/heal</c> y
    /// <c>/xp</c> necesitan mandar <c>EntityStats</c> a quien tenga al objetivo en su área de
    /// interés, que es la zona del objetivo, no la del admin que manda el comando.
    /// </summary>
    private (Zone Zone, PlayerEntity Player)? FindPlayerZoneByName(string name)
    {
        foreach (var zone in _zones.Values)
        {
            var player = zone.Players.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (player is not null)
            {
                return (zone, player);
            }
        }

        return null;
    }

    /// <summary>
    /// Cura vida y maná al máximo al instante (hueco real, pedido explícito de sesión: no había
    /// forma de reponerse a mano para probar contenido, aparte de una poción de vida — de maná no
    /// existe ninguna — o de subir de nivel).
    /// </summary>
    private void HandleHeal(PlayerEntity admin, ChatCommand.Heal heal)
    {
        if (!IsAuthorizedAdmin(admin))
        {
            SendChatFailure(admin, ResultCode.NotAuthorized);
            return;
        }

        if (FindPlayerZoneByName(heal.TargetName) is not { } found)
        {
            SendChatFailure(admin, ResultCode.TargetNotFound);
            return;
        }

        var (targetZone, target) = found;
        target.Hp = target.HpMax;
        target.Mp = target.MpMax;
        target.VitalsDirty = true;
        BroadcastEntityStats(targetZone, target);

        LogAdminAction(admin, target, AdminAction.Heal, reason: string.Empty);
        ConfirmAdminAction(admin, target.Name);
    }

    /// <summary>
    /// Concede XP a mano (mismo hueco que <see cref="HandleHeal"/>): reutiliza el
    /// <see cref="GrantXp(Zone, PlayerEntity, long)"/> que ya usan las recompensas de combate tal
    /// cual, así que subir de nivel por este camino sube de nivel de verdad — mismos puntos de
    /// stat, misma curación al cruzar de nivel, nada nuevo que probar.
    /// </summary>
    private void HandleGrantXp(PlayerEntity admin, ChatCommand.GrantXp grantXp)
    {
        if (!IsAuthorizedAdmin(admin))
        {
            SendChatFailure(admin, ResultCode.NotAuthorized);
            return;
        }

        if (FindPlayerZoneByName(grantXp.TargetName) is not { } found)
        {
            SendChatFailure(admin, ResultCode.TargetNotFound);
            return;
        }

        var (targetZone, target) = found;
        GrantXp(targetZone, target, grantXp.Amount);
        LogAdminAction(admin, target, AdminAction.GrantXp, reason: string.Empty, quantity: (int)Math.Min(grantXp.Amount, int.MaxValue));
        ConfirmAdminAction(admin, target.Name);
    }

    private void LogAdminAction(
        PlayerEntity admin, PlayerEntity target, AdminAction action, string reason,
        int? banHours = null, string? defKey = null, int? quantity = null) =>
        _adminActionSink.Enqueue(new AdminActionSave(
            admin.CharacterId, admin.Name, target.CharacterId, target.Name, action, reason, banHours, defKey, quantity));

    private static void ConfirmAdminAction(PlayerEntity admin, string targetName) => admin.Peer.Send(
        Opcode.SystemMessage, new S2CSystemMessage { Severity = 0, Key = "chat.AdminActionDone", Args = [targetName] });

    private static void SendChatFailure(PlayerEntity player, ResultCode code) =>
        SendFailure(player, "chat", code);

    /// <summary>Manda a todo el mundo, en todas las zonas — el canal global no se queda en la propia (FASE-11 §2 D2).</summary>
    private void BroadcastToWorld<T>(Opcode opcode, T payload)
    {
        foreach (var zone in _zones.Values)
        {
            BroadcastToZone(zone, opcode, payload);
        }
    }

    /// <summary>
    /// Concede XP y, si cruza de nivel, cura del todo y avisa a quien tenga al jugador en su área
    /// de interés — el nivel viaja en <c>EntityStats</c> (FASE-10 §2 D2).
    /// </summary>
    private void GrantXp(Zone zone, PlayerEntity player, long amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var result = LevelingSystem.GrantXp(player, amount, _classes, _items);
        player.VitalsDirty = true;
        SendXpUpdate(player, result.LeveledUp);

        if (result.LeveledUp)
        {
            BroadcastEntityStats(zone, player);

            // BroadcastEntityStats sólo lleva HP/MP/nivel (S2CEntityStats, Fase 9): sin esto el
            // cliente nunca se entera de los puntos de stat que acaba de conceder la subida —
            // StatPoints sólo viaja en EquipmentUpdate — hasta el próximo equipar/desequipar.
            SendEquipmentUpdate(player);
        }
    }

    private static void SendXpUpdate(PlayerEntity player, bool leveledUp) => player.Peer.Send(Opcode.XpUpdate, new S2CXpUpdate
    {
        Xp = player.Xp,
        XpToNextLevel = LevelingFormulas.XpRequiredForNextLevel(player.Level),
        Level = player.Level,
        LeveledUp = leveledUp,
    });

    private static void FlagCombat(PlayerEntity player, long nowMs)
    {
        player.CombatFlagUntilMs = nowMs + CombatConstants.CombatFlagMs;
        SendCombatFlag(player, nowMs);
    }

    private static void SendCombatFlag(PlayerEntity player, long nowMs) => player.Peer.Send(
        Opcode.CombatFlagUpdate,
        new S2CCombatFlagUpdate
        {
            InCombat = player.IsInCombat(nowMs),
            MsRemaining = (int)Math.Max(0, player.CombatFlagUntilMs - nowMs),
        });

    private static void SendCombatFailure(PlayerEntity player, ResultCode code) =>
        SendFailure(player, "combat", code);

    /// <summary>Manda un evento a todo el que tenga a <paramref name="subject"/> en su área de interés.</summary>
    private static void BroadcastCombatEvent(Zone zone, WorldEntity subject, S2CCombatEvent evt)
    {
        foreach (var observer in zone.Players)
        {
            if (observer.Id == subject.Id || observer.Known.Contains(subject.Id))
            {
                observer.Peer.Send(Opcode.CombatEvent, evt);
            }
        }
    }

    private static void BroadcastEntityStats(Zone zone, WorldEntity subject)
    {
        var stats = new S2CEntityStats
        {
            Id = subject.Id,
            Hp = subject.Hp,
            HpMax = subject.HpMax,
            Mp = subject is PlayerEntity player ? player.Mp : 0,
            MpMax = subject is PlayerEntity p ? p.MpMax : 0,
            Level = subject is MonsterEntity monster ? monster.Definition.Level
                : subject is PlayerEntity pl ? pl.Level : 1,
        };

        foreach (var observer in zone.Players)
        {
            if (observer.Id == subject.Id || observer.Known.Contains(subject.Id))
            {
                observer.Peer.Send(Opcode.EntityStats, stats);
            }
        }
    }

    private static void BroadcastToZone<T>(Zone zone, Opcode opcode, T payload)
    {
        foreach (var observer in zone.Players)
        {
            observer.Peer.Send(opcode, payload);
        }
    }

    /// <summary>
    /// Barrido de combate: repone monstruos, caduca sacos de loot, expira flags de combate,
    /// regenera vida/maná y saca del mundo a quien pidió salir estando en combate y ya cumplió sus
    /// 10 s (FASE-09 §2 D11). Una vez por segundo: nada de esto necesita resolución de tick.
    /// </summary>
    private void SweepCombat(long tick, long nowMs)
    {
        if (tick % SimulationConstants.TickRate != 0)
        {
            return;
        }

        foreach (var zone in _zones.Values)
        {
            foreach (var monster in _spawners[zone.Map.Key].Spawn(_entityIds, zone.Map, nowMs))
            {
                zone.AddEntity(monster);
            }

            foreach (var bag in zone.LootBags.Where(bag => nowMs >= bag.DespawnAtMs).ToList())
            {
                zone.RemoveEntity(bag.Id, DespawnReason.OutOfRange);
            }

            foreach (var player in zone.Players.ToList())
            {
                if (player.CombatFlagUntilMs > 0 && !player.IsInCombat(nowMs))
                {
                    player.CombatFlagUntilMs = 0;
                    SendCombatFlag(player, nowMs);
                }

                // Pidió salir en combate: la entidad se quedó viva y atacable, y ahora sí se va.
                if (player.PendingLeaveAtMs is { } leaveAt && nowMs >= leaveAt)
                {
                    _log.Information("El personaje {CharacterId} sale del mundo tras expirar su flag de combate",
                        player.CharacterId);
                    RemoveFrom(zone, player.Peer.Id, force: true);
                }

                RegenPlayer(zone, player);
            }
        }
    }

    /// <summary>
    /// Regeneración pasiva de vida/maná (hallazgo real de esta sesión, no una fase planeada: el
    /// maná no se recuperaba nunca, con nada — ni con el tiempo, ni <c>content/items/</c> tiene una
    /// poción de maná. Cualquier personaje quedaba definitivamente sin poder lanzar habilidades en
    /// cuanto gastaba el maná inicial). Un muerto no regenera: está esperando reaparición, no
    /// jugando. <see cref="RegenFormulas"/> es pura y ya tiene sus propios tests; esto sólo la
    /// llama una vez por segundo y manda lo que cambió, mismo patrón que un golpe o una curación.
    /// </summary>
    private static void RegenPlayer(Zone zone, PlayerEntity player)
    {
        if (player.IsDead)
        {
            return;
        }

        var newHp = RegenFormulas.Regen(player.Hp, player.HpMax, RegenFormulas.HpRegenPerSecondFraction, elapsedSeconds: 1);
        var newMp = RegenFormulas.Regen(player.Mp, player.MpMax, RegenFormulas.MpRegenPerSecondFraction, elapsedSeconds: 1);

        if (newHp == player.Hp && newMp == player.Mp)
        {
            return;
        }

        player.Hp = newHp;
        player.Mp = newMp;
        player.VitalsDirty = true;
        BroadcastEntityStats(zone, player);
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

    private static void SendInventoryFailure(PlayerEntity player, ResultCode code) =>
        SendFailure(player, "inventory", code);

    /// <summary>
    /// El único sitio por el que sale un rechazo hacia el cliente. Aparte de mandar el
    /// <c>SystemMessage</c>, es donde se le apunta la anomalía a la sesión si el código lo merece
    /// (FASE-13 §2 D4): así los ~29 puntos que rechazan algo quedan cubiertos sin tocarlos uno a
    /// uno, y un rechazo nuevo de una fase futura queda cubierto solo.
    /// </summary>
    private static void SendFailure(PlayerEntity player, string prefix, ResultCode code)
    {
        player.Peer.Send(
            Opcode.SystemMessage, new S2CSystemMessage { Severity = 0, Key = $"{prefix}.{code}", Args = [] });

        if (AnomalyMapping.For(code) is { } anomaly)
        {
            player.Peer.RecordAnomaly(anomaly);
        }
    }

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
            player.Inventory, _items, classDef, player.Str, player.IntStat, player.Vit, player.Dex, player.Level);

        player.HpMax = stats.HpMax;
        player.MpMax = stats.MpMax;
        player.Hp = Math.Min(player.Hp, player.HpMax);
        player.Mp = Math.Min(player.Mp, player.MpMax);

        // Ataque, defensa y destreza efectiva: lo que entra en CombatFormulas (Fase 9). Se
        // recalculan aquí y no en cada golpe porque sólo cambian al tocar el equipo.
        player.AttackPower = stats.Attack;
        player.Defense = stats.Defense;
        player.DexEffective = stats.DexEffective;

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
            StatPoints = player.StatPoints,
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
        if (!player.PositionDirty && !player.GoldDirty && !player.VitalsDirty && !force)
        {
            return;
        }

        _characters.Enqueue(new CharacterSave(
            player.CharacterId,
            zone.Map.Key,
            player.State.Pos.X,
            player.State.Pos.Y,
            player.State.Facing,
            player.Gold,
            player.Hp,
            player.Mp,
            player.Xp,
            player.Level,
            player.Str,
            player.IntStat,
            player.Vit,
            player.Dex,
            player.StatPoints));

        player.PositionDirty = false;
        player.GoldDirty = false;
        player.VitalsDirty = false;
    }
}

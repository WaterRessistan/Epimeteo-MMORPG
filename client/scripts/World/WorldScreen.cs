using System;
using System.Collections.Generic;
using System.Linq;
using Epimeteo.Client.Inventory;
using Epimeteo.Client.Net;
using Epimeteo.Client.Shop;
using Epimeteo.Client.Ui;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using Godot;

namespace Epimeteo.Client.World;

/// <summary>
/// La escena de mundo. Carga el mapa, comprueba que es el mismo que el del servidor, mantiene el
/// registro de entidades y reparte cada mensaje a quien le toca: el jugador local predice, las
/// entidades remotas interpolan.
/// </summary>
public partial class WorldScreen : Node2D
{
    /// <summary>
    /// Rango cliente para decidir a qué NPC apunta <c>interact</c>. Con margen sobre el del
    /// servidor (<c>ShopInteractionRangeTiles</c> = 3, FASE-07 §2 D7) a propósito: el servidor
    /// tiene la última palabra, así que ser un poco generoso aquí sólo hace que
    /// <c>ShopOpen</c> se rechace con <c>TooFarAway</c> alguna vez de más, nunca que se acepte algo
    /// que no debía.
    /// </summary>
    private const float InteractRangeTiles = 3.5f;

    /// <summary>
    /// Rango para el marco de objetivo del HUD ("a quién tengo cerca"), no para decidir a quién se
    /// ataca — eso usa el alcance real de cada acción (<c>CombatConstants.MeleeRangeTiles</c> para
    /// <c>Attack</c>, <c>skill.RangeTiles</c> para un lanzamiento). <b>Bug real corregido esta
    /// sesión:</b> antes este mismo valor (2,5 tiles) se usaba también para elegir a quién atacar
    /// con la tecla básica, más ancho que el alcance real del cuerpo a cuerpo (1,5) — el cliente
    /// "encontraba" un objetivo que el servidor rechazaba por <c>OutOfRange</c> una fracción del
    /// tiempo, y para los hechizos de mago (hasta 6 tiles) era al revés: este radio los recortaba y
    /// ni siquiera se probaba un objetivo que sí estaba en rango. Es parte de lo que Mario reportó
    /// como "no consigo pegar".
    /// </summary>
    private const float TargetRangeTiles = 2.5f;

    private readonly Dictionary<int, RemoteEntity> _remotes = [];

    /// <summary>Reloj de interpolación. Vive en <c>Shared</c> y tiene sus propios tests.</summary>
    private readonly InterpolationClock _clock = new();

    /// <summary>
    /// Cooldown visual optimista por habilidad, en segundos restantes (FASE-10 §7): arranca al
    /// <b>mandar</b> el cast, no al confirmarlo el servidor — igual que el resto de comprobaciones
    /// de cliente en esta pantalla, es sólo cosmético; si el servidor lo rechaza no pasa nada peor
    /// que dejar el botón gris un rato de más.
    /// </summary>
    private readonly Dictionary<string, double> _skillCooldowns = [];

    private static readonly string[] SkillActionKeys = [InputActions.Skill1, InputActions.Skill2, InputActions.Skill3];

    /// <summary>
    /// Cadencia del ataque básico si se mantiene pulsado (D-QoL de esta sesión, pedido por Mario:
    /// "que se pueda atacar libremente" en vez de una pulsación por golpe). El servidor sigue
    /// siendo quien valida el cooldown real (FASE-09); esto sólo evita mandar ataques que el propio
    /// cliente ya sabe que van a rechazarse por <c>OnCooldown</c>, usando la misma constante que usa
    /// el servidor (<see cref="CombatConstants.AttackCooldownMs"/>) para no desincronizarse de ella.
    /// </summary>
    private double _attackCooldownRemainingMs;

    private NetClient _net = null!;
    private WorldRenderer _renderer = null!;
    private WorldCamera _camera = null!;
    private WorldHud _hud = null!;
    private CombatHud _combatHud = null!;
    private ShopScreen _shop = null!;

    private SkillDefinition[] _classSkills = [];
    private ItemCatalog? _items;
    private readonly InventoryState _inventory = new();

    private LocalPlayer? _local;
    private int _myEntityId;

    private string _regionName = string.Empty;
    private ZoneFlags _regionFlags = ZoneFlags.None;
    private int _myHp;
    private int _myHpMax;
    private int _myMp;
    private int _myMpMax;

    /// <inheritdoc />
    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");
        _renderer = GetNode<WorldRenderer>("Renderer");
        _camera = GetNode<WorldCamera>("Camera");
        _hud = GetNode<WorldHud>("Hud/WorldHud");
        _combatHud = GetNode<CombatHud>("CombatHud");
        _shop = GetNode<ShopScreen>("ShopScreen");

        var enter = _net.LastWorldEnter;
        if (enter is null)
        {
            Fail("Se entró al mundo sin WorldEnter.");
            return;
        }

        var map = ClientContent.LoadMap(enter.MapKey);
        if (map is null)
        {
            Fail($"Falta el mapa {enter.MapKey} en content/.");
            return;
        }

        // El contenido del cliente tiene que ser bit a bit el del servidor: si no, la predicción
        // se hace sobre otra colisión y el jugador vería goma elástica sin causa aparente. Mejor
        // pararse aquí con un mensaje claro (FASE-04 §2 D4).
        if (map.Hash != enter.MapHash)
        {
            Fail($"Contenido desactualizado: el mapa del servidor es {enter.MapHash:X8} y el tuyo {map.Hash:X8}.");
            return;
        }

        // Sólo lo que puede lanzar la clase del personaje, y en el mismo orden que las teclas
        // 1-3 le van a asignar: por nivel requerido, que es como está escrito el contenido
        // (FASE-10 §7 — no hace falta un orden distinto).
        var contentRoot = ClientContent.ResolveContentRoot();
        var classKey = _net.SelectedCharacter?.ClassKey;
        if (contentRoot is not null && classKey is not null)
        {
            var skills = new SkillCatalog(contentRoot);
            _classSkills = [.. skills.ForClass(classKey).OrderBy(s => s.RequiredLevel)];
        }

        // El catálogo de ítems completo (no sólo el del personaje, a diferencia de las
        // habilidades): hace falta para saber a qué EquipSlot le toca cada arma de la bolsa al
        // cambiar de arma con Q (HandleSwapWeapon), y para el icono del arma equipada en el HUD.
        if (contentRoot is not null)
        {
            _items = new ItemCatalog(contentRoot);
        }

        _myEntityId = enter.MyEntityId;
        // HpMax/MpMax no viajan en WorldEnter (CharacterStats sólo lleva vida/maná actuales): los
        // trae el EquipmentUpdate que llega justo después, porque dependen del equipo (FASE-06 §2 D5).
        _myHp = enter.Stats.Hp;
        _myHpMax = enter.Stats.Hp;
        _myMp = enter.Stats.Mp;
        _myMpMax = enter.Stats.Mp;

        _renderer.SetMap(map);
        _camera.SetMap(map);

        _local = new LocalPlayer(_net, map, new Vec2(enter.SpawnX, enter.SpawnY), enter.Facing);
        _renderer.Local = _local;
        _renderer.Remotes = _remotes.Values;
        _renderer.LocalName = _net.SelectedCharacter?.Name ?? string.Empty;
        _renderer.LocalPalette = _net.SelectedCharacter?.PaletteIndex ?? 0;
        _renderer.LocalDefKey = _net.SelectedCharacter?.ClassKey ?? string.Empty;
        _renderer.LocalEntityId = _myEntityId;
        _camera.FollowTile(_local.RenderPos);

        _net.SnapshotReceived += OnSnapshot;
        _net.EntitySpawnReceived += OnEntitySpawn;
        _net.EntityDespawnReceived += OnEntityDespawn;
        _net.ZoneFlagsUpdateReceived += OnZoneFlags;
        _net.EntityStatsReceived += OnEntityStats;
        _net.EquipmentUpdateReceived += OnEquipmentUpdate;
        _net.EntityDeathReceived += OnEntityDeath;
        _net.CombatEventReceived += OnCombatEvent;
        _net.InventoryFullReceived += OnInventoryFull;
        _net.InventoryDeltaReceived += OnInventoryDelta;
        _net.SystemMessageReceived += OnCombatSystemMessage;
        _net.Kicked += OnKicked;

        // Hasta aquí sólo se ha preparado el cliente. WorldReady le dice al servidor que ya puede
        // meter la entidad en el mundo y empezar a mandar snapshots.
        _net.SendWorldReady();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _net.SnapshotReceived -= OnSnapshot;
        _net.EntitySpawnReceived -= OnEntitySpawn;
        _net.EntityDespawnReceived -= OnEntityDespawn;
        _net.ZoneFlagsUpdateReceived -= OnZoneFlags;
        _net.EntityStatsReceived -= OnEntityStats;
        _net.EquipmentUpdateReceived -= OnEquipmentUpdate;
        _net.EntityDeathReceived -= OnEntityDeath;
        _net.CombatEventReceived -= OnCombatEvent;
        _net.InventoryFullReceived -= OnInventoryFull;
        _net.InventoryDeltaReceived -= OnInventoryDelta;
        _net.SystemMessageReceived -= OnCombatSystemMessage;
        _net.Kicked -= OnKicked;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_local is null)
        {
            return;
        }

        _local.Update(delta);
        _clock.Advance(delta);

        foreach (var remote in _remotes.Values)
        {
            remote.Advance(_clock.RenderTick);
        }

        _camera.FollowTile(_local.RenderPos);
        _renderer.LocalIsAlive = _myHp > 0;
        _renderer.QueueRedraw();
        TickSkillCooldowns(delta);
        UpdateHud();
        HandleInteract();
        HandleAttack(delta);
        HandleSkillCast();
        HandleSwapWeapon();
    }

    private void TickSkillCooldowns(double delta)
    {
        if (_skillCooldowns.Count == 0)
        {
            return;
        }

        foreach (var key in _skillCooldowns.Keys.ToArray())
        {
            var remaining = _skillCooldowns[key] - delta;
            if (remaining <= 0)
            {
                _skillCooldowns.Remove(key);
            }
            else
            {
                _skillCooldowns[key] = remaining;
            }
        }
    }

    /// <summary>
    /// Teclas 1-3: lanzan la habilidad de ese hueco. Curaciones se apuntan a uno mismo (D9); el
    /// resto, al objetivo atacable más cercano — igual criterio que <see cref="HandleAttack"/>.
    /// El nivel y el maná los comprueba también el servidor; aquí sólo se evita mandar algo que ya
    /// se sabe que va a fallar (clase equivocada para el hueco, cooldown visual en marcha).
    /// </summary>
    private void HandleSkillCast()
    {
        if (_local is null)
        {
            return;
        }

        for (var i = 0; i < SkillActionKeys.Length && i < _classSkills.Length; i++)
        {
            if (!Input.IsActionJustPressed(SkillActionKeys[i]))
            {
                continue;
            }

            var skill = _classSkills[i];
            if (_net.Level < skill.RequiredLevel || _skillCooldowns.ContainsKey(skill.Key))
            {
                continue;
            }

            int targetId;
            if (skill.Kind == CombatEventKind.Heal)
            {
                targetId = _myEntityId;
            }
            else
            {
                var target = FindNearestTarget(_local.Current.Pos, skill.RangeTiles);
                if (target is null)
                {
                    continue;
                }

                targetId = target.Id;
            }

            _net.SendSkillCast(skill.Key, targetId);
            _skillCooldowns[skill.Key] = skill.CooldownMs / 1000.0;
            _renderer.NotifyAttackSwing(_myEntityId);
        }
    }

    /// <summary>
    /// Tecla <c>attack</c> (espacio): pega al objetivo atacable más cercano. El cliente sólo elige
    /// a quién apuntar; si vale o no lo decide el servidor (FASE-09 §2 D3), así que aquí no se
    /// comprueba ni zona ni alcance — sólo se evita mandar un ataque sin nadie delante.
    /// <para>
    /// <b>Ataque libre (pedido por Mario):</b> mantener pulsado sigue pegando solo, al ritmo real
    /// del servidor (<see cref="CombatConstants.AttackCooldownMs"/>) — antes hacía falta soltar y
    /// volver a pulsar por cada golpe, que es fricción de UI, no una regla del juego.
    /// </para>
    /// </summary>
    private void HandleAttack(double deltaSeconds)
    {
        if (_attackCooldownRemainingMs > 0)
        {
            _attackCooldownRemainingMs -= deltaSeconds * 1000.0;
        }

        if (_local is null || !Input.IsActionPressed(InputActions.Attack) || _attackCooldownRemainingMs > 0)
        {
            return;
        }

        var target = FindNearestTarget(_local.Current.Pos, CombatConstants.MeleeRangeTiles);
        if (target is null)
        {
            return;
        }

        _net.SendAttack(target.Id);
        _attackCooldownRemainingMs = CombatConstants.AttackCooldownMs;
        _renderer.NotifyAttackSwing(_myEntityId);
    }

    /// <summary>
    /// Tecla <c>swap_weapon</c> (Q, pedida por Mario: "que podamos cambiar de arma si la
    /// tenemos"): equipa la siguiente arma de mano principal que haya en la bolsa de armas, o la
    /// quita si no queda ninguna — <c>InventorySystem.TryEquip</c> ya intercambia solo lo que
    /// hubiera puesto antes de vuelta a la bolsa (FASE-06 §4), así que aquí sólo hace falta elegir
    /// el hueco de origen.
    /// </summary>
    private void HandleSwapWeapon()
    {
        if (!Input.IsActionJustPressed(InputActions.SwapWeapon) || _items is null)
        {
            return;
        }

        byte? nextSlot = null;
        foreach (var ((container, slot), item) in _inventory.Bags)
        {
            if (container != ContainerId.WeaponBag)
            {
                continue;
            }

            if (_items.TryGet(item.DefKey, out var def) && def.EquipCategory == EquipCategory.MainHand &&
                (nextSlot is null || slot < nextSlot))
            {
                nextSlot = slot;
            }
        }

        if (nextSlot is { } slotToEquip)
        {
            _net.SendEquip(ContainerId.WeaponBag, slotToEquip, EquipSlot.MainHand);
        }
        else if (_inventory.EquippedAt(EquipSlot.MainHand) is not null)
        {
            _net.SendUnequip(EquipSlot.MainHand);
        }
    }

    private RemoteEntity? FindNearestTarget(Vec2 fromPos, float rangeTiles = TargetRangeTiles)
    {
        RemoteEntity? nearest = null;
        var nearestDistanceSq = rangeTiles * rangeTiles;

        foreach (var remote in _remotes.Values)
        {
            if (remote.Type is not (EntityType.Monster or EntityType.Player) || !remote.IsAlive)
            {
                continue;
            }

            var distanceSq = Vec2.DistanceSquared(fromPos, remote.State.Pos);
            if (distanceSq <= nearestDistanceSq)
            {
                nearest = remote;
                nearestDistanceSq = distanceSq;
            }
        }

        return nearest;
    }

    private void OnEntityStats(S2CEntityStats stats)
    {
        if (stats.Id == _myEntityId)
        {
            _myHp = stats.Hp;
            _myHpMax = stats.HpMax;
            _myMp = stats.Mp;
            _myMpMax = stats.MpMax;
            return;
        }

        if (_remotes.TryGetValue(stats.Id, out var remote))
        {
            remote.Hp = stats.Hp;
            remote.HpMax = stats.HpMax;
        }
    }

    private void OnEquipmentUpdate(S2CEquipmentUpdate update)
    {
        _myHpMax = update.HpMax;
        _myMpMax = update.MpMax;
        _inventory.ApplyEquipment(update);
    }

    private void OnInventoryFull(S2CInventoryFull full) => _inventory.ApplyFull(full);

    private void OnInventoryDelta(S2CInventoryDelta delta) => _inventory.ApplyDelta(delta);

    private void OnEntityDeath(S2CEntityDeath death)
    {
        if (_remotes.TryGetValue(death.Id, out var remote))
        {
            remote.Hp = 0;
        }
    }

    /// <summary>
    /// Traduce el golpe que cuenta el servidor a lo que se ve: número flotante sobre la víctima
    /// (color y "¡crítico!" según <c>Kind</c>/<c>Flags</c>), tinte rojo instantáneo en la víctima y
    /// resalte en quien pega (FASE-09, opcode 0x8060 — llega a todo el que tenga a la víctima en su
    /// área de interés, no sólo a los dos implicados, así que esto pinta lo mismo para cualquiera
    /// que lo vea).
    /// </summary>
    private void OnCombatEvent(S2CCombatEvent evt)
    {
        if (evt.AttackerId != _myEntityId)
        {
            // El ataque propio ya se resalta al mandarlo (HandleAttack/HandleSkillCast) para que
            // se sienta inmediato sin esperar la vuelta de red; esto es para verlo en los demás.
            _renderer.NotifyAttackSwing(evt.AttackerId);
        }

        if (PositionOf(evt.TargetId) is not { } targetPos)
        {
            return;
        }

        switch (evt.Kind)
        {
            case CombatEventKind.Damage:
                _renderer.NotifyHit(evt.TargetId);
                var critical = evt.Flags.HasFlag(CombatEventFlags.Critical);
                var text = critical ? $"¡{evt.Amount}!" : evt.Amount.ToString();
                _renderer.SpawnFloatingText(targetPos, text, critical ? WorldRenderer.CritColor : WorldRenderer.DamageColor);
                break;
            case CombatEventKind.Heal:
                _renderer.SpawnFloatingText(targetPos, $"+{evt.Amount}", WorldRenderer.HealColor);
                break;
            case CombatEventKind.Miss:
                var label = evt.Flags.HasFlag(CombatEventFlags.Blocked) ? "bloqueado" : "esquiva";
                _renderer.SpawnFloatingText(targetPos, label, WorldRenderer.MissColor);
                break;
        }
    }

    /// <summary>
    /// Por qué un <c>Attack</c>/<c>SkillCast</c> se rechazó (<c>combat.SafeZone</c>,
    /// <c>combat.OutOfRange</c>, <c>combat.OnCooldown</c>…). <b>Bug real encontrado esta sesión:</b>
    /// el servidor siempre mandó este mensaje (<c>GameWorld.SendCombatFailure</c>, Fase 9), pero
    /// ninguna pantalla lo enseñaba durante el combate —<c>ChatScreen</c> sólo mira el prefijo
    /// <c>chat.</c> e <c>InventoryScreen</c> sólo se ve con el inventario abierto— así que un ataque
    /// rechazado no daba ninguna pista de por qué. Es lo que reportó Mario como "no consigo pegar".
    /// </summary>
    private void OnCombatSystemMessage(S2CSystemMessage message)
    {
        const string Prefix = "combat.";
        if (!message.Key.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return;
        }

        if (!Enum.TryParse<ResultCode>(message.Key[Prefix.Length..], out var code))
        {
            return;
        }

        _combatHud.ShowCombatMessage(ResultCodeText.Describe(code));
    }

    /// <summary>Posición de mundo de una entidad por id, propia o remota, o <c>null</c> si ya no está a la vista.</summary>
    private Vec2? PositionOf(int entityId)
    {
        if (entityId == _myEntityId)
        {
            return _local?.Current.Pos;
        }

        return _remotes.TryGetValue(entityId, out var remote) ? remote.State.Pos : null;
    }

    /// <summary>
    /// Tecla <c>interact</c> (E): si la tienda está abierta, la cierra; si no, abre la del NPC
    /// más cercano dentro de rango. El servidor decide de verdad si la distancia vale
    /// (FASE-07 §2 D7) — esto sólo elige a qué NPC apuntar.
    /// </summary>
    private void HandleInteract()
    {
        if (!Input.IsActionJustPressed(InputActions.Interact) || _local is null)
        {
            return;
        }

        if (_shop.IsOpen)
        {
            _shop.RequestClose();
            return;
        }

        var npc = FindNearestNpc(_local.Current.Pos);
        if (npc is null)
        {
            return;
        }

        _shop.NoteOpenedNpc(npc.Id);
        _net.SendShopOpen(npc.Id);
    }

    private RemoteEntity? FindNearestNpc(Vec2 fromPos)
    {
        RemoteEntity? nearest = null;
        var nearestDistanceSq = InteractRangeTiles * InteractRangeTiles;

        foreach (var remote in _remotes.Values)
        {
            if (remote.Type != EntityType.Npc)
            {
                continue;
            }

            var distanceSq = Vec2.DistanceSquared(fromPos, remote.State.Pos);
            if (distanceSq <= nearestDistanceSq)
            {
                nearest = remote;
                nearestDistanceSq = distanceSq;
            }
        }

        return nearest;
    }

    private void OnSnapshot(S2CSnapshot snapshot)
    {
        _clock.OnSnapshot(snapshot.ServerTick);

        foreach (var delta in snapshot.Entities)
        {
            if (delta.Id == _myEntityId)
            {
                _local?.ApplyAuthoritative(delta, snapshot.LastAckedInputSeq);
            }
            else if (_remotes.TryGetValue(delta.Id, out var remote))
            {
                remote.PushSample(snapshot.ServerTick, delta);
            }

            // Un delta de una entidad sin spawn previo se ignora: es la carrera normal entre un
            // snapshot en vuelo y el EntityDespawn que ya se procesó.
        }
    }

    private void OnEntitySpawn(S2CEntitySpawn spawn)
    {
        foreach (var info in spawn.Entities)
        {
            if (info.Id == _myEntityId)
            {
                continue;
            }

            _remotes[info.Id] = new RemoteEntity(info);
        }
    }

    private void OnEntityDespawn(S2CEntityDespawn despawn)
    {
        foreach (var entry in despawn.Entities)
        {
            _remotes.Remove(entry.Id);
        }
    }

    /// <summary>
    /// Expulsado estando dentro. Se vuelve a la pantalla de conexión, igual que hacen las demás
    /// pantallas: quedarse en un mundo congelado sin decir nada es lo peor que puede hacer aquí el
    /// cliente. El motivo llega en el mensaje porque el servidor lo manda antes de cerrar.
    /// </summary>
    private void OnKicked(KickReason reason, ResultCode detail, int serverProtocolVersion)
    {
        GD.Print(ResultCodeText.Describe(reason, detail, serverProtocolVersion));
        GetTree().ChangeSceneToFile("res://scenes/Connect.tscn");
    }

    private void OnZoneFlags(S2CZoneFlagsUpdate update)
    {
        _regionName = update.RegionName;
        _regionFlags = update.Flags;
    }

    private void UpdateHud()
    {
        if (_local is null)
        {
            return;
        }

        _hud.SetPosition(_local.Current.Pos, _remotes.Count);
        _hud.SetNetwork(_net.LastRttMs, _net.SimulatedLagMs);
        _hud.SetPrediction(
            _local.Prediction.Corrections,
            _local.Prediction.MaxErrorTiles,
            _local.Prediction.PendingCount);

        var target = FindNearestTarget(_local.Current.Pos);
        _hud.SetCombat(
            _myHp,
            _myHpMax,
            _net.Level,
            _net.Xp,
            _net.XpToNextLevel,
            target is null ? "sin objetivo" : $"{target.Name} {target.Hp}/{target.HpMax}",
            _net.InCombat);
        _hud.SetSkills(BuildSkillBarText());

        _combatHud.SetVitals(_myHp, _myHpMax, _myMp, _myMpMax, _net.Level, _net.Xp, _net.XpToNextLevel);
        _combatHud.SetWeapon(
            WeaponSlot(_inventory.EquippedAt(EquipSlot.MainHand)),
            WeaponSlot(_inventory.EquippedAt(EquipSlot.OffHand)));
        // Al alcance de un ataque básico de verdad, no sólo "está en el radio del marco de HUD"
        // (FindNearestTarget aquí usa el radio ancho de "quién tengo cerca" a propósito, ver
        // TargetRangeTiles) — así el jugador ve de un vistazo si acercarse un poco más antes de
        // pulsar, en vez de enterarse sólo cuando el ataque ya se ha rechazado.
        var inMeleeRange = target is not null &&
            Vec2.DistanceSquared(_local.Current.Pos, target.State.Pos) <= CombatConstants.MeleeRangeTiles * CombatConstants.MeleeRangeTiles;
        _combatHud.SetTarget(target is null ? null : target.Name, target?.Hp ?? 0, target?.HpMax ?? 1, inMeleeRange);
        _combatHud.SetSkills(BuildCombatSkillSlots());

        // Mientras no llegue el primer ZoneFlagsUpdate se enseña lo que el cliente deduce del
        // mapa; en cuanto el servidor habla, manda él.
        if (string.IsNullOrEmpty(_regionName))
        {
            var region = _local.Region;
            _hud.SetRegion(region.Name, region.Flags);
        }
        else
        {
            _hud.SetRegion(_regionName, _regionFlags);
        }
    }

    /// <summary>Resuelve nombre y clave visual de un hueco de equipo para <c>CombatHud</c>, o <c>null</c> si está vacío.</summary>
    private WeaponSlotInfo? WeaponSlot(ItemStackInfo? item)
    {
        if (item is null)
        {
            return null;
        }

        var displayName = _items is not null && _items.TryGet(item.DefKey, out var def) ? def.DisplayName : item.DefKey;
        return new WeaponSlotInfo(displayName, item.DefKey);
    }

    /// <summary>Lo mismo que <see cref="BuildSkillBarText"/> pero estructurado, para que <c>CombatHud</c> pinte iconos y un cooldown de verdad en vez de texto.</summary>
    private List<SkillSlotInfo> BuildCombatSkillSlots()
    {
        var slots = new List<SkillSlotInfo>(_classSkills.Length);
        for (var i = 0; i < _classSkills.Length && i < SkillActionKeys.Length; i++)
        {
            var skill = _classSkills[i];
            var locked = _net.Level < skill.RequiredLevel;
            var cooldownFraction = 0f;
            if (!locked && _skillCooldowns.TryGetValue(skill.Key, out var remaining))
            {
                cooldownFraction = (float)Math.Clamp(remaining / (skill.CooldownMs / 1000.0), 0.0, 1.0);
            }

            slots.Add(new SkillSlotInfo(skill.DisplayName, skill.Kind, locked, skill.RequiredLevel, cooldownFraction));
        }

        return slots;
    }

    /// <summary>Texto de la barra de habilidades del HUD: tecla, nombre, y cooldown si lo hay.</summary>
    private string BuildSkillBarText()
    {
        if (_classSkills.Length == 0)
        {
            return string.Empty;
        }

        var parts = new string[Math.Min(_classSkills.Length, SkillActionKeys.Length)];
        for (var i = 0; i < parts.Length; i++)
        {
            var skill = _classSkills[i];
            var locked = _net.Level < skill.RequiredLevel;
            var status = locked
                ? $"Nv {skill.RequiredLevel}"
                : _skillCooldowns.TryGetValue(skill.Key, out var remaining)
                    ? $"{remaining:F1}s"
                    : "listo";

            parts[i] = $"[{i + 1}] {skill.DisplayName} ({status})";
        }

        return string.Join("  ·  ", parts);
    }

    private void Fail(string message)
    {
        GD.PushError(message);
        _hud.ShowFatal(message);
    }
}

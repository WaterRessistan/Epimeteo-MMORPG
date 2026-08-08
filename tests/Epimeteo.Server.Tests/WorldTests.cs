using Epimeteo.Server.Content;
using Epimeteo.Server.Farm;
using Epimeteo.Server.Inventory;
using Epimeteo.Server.Persistence.Admin;
using Epimeteo.Server.Persistence.Chat;
using Epimeteo.Server.Persistence.Combat;
using Epimeteo.Server.Persistence.Economy;
using Epimeteo.Server.Persistence.Farm;
using Epimeteo.Server.Persistence.Items;
using Epimeteo.Server.Shop;
using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using MessagePack;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// El mundo completo con el mapa real de <c>content/maps/</c>: frontera con el hilo de red
/// (join/leave/inputs) y cola de guardado. Sin Postgres: el destino de los guardados es un doble.
/// </summary>
public sealed class WorldTests
{
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());
    private static readonly ClassCatalog Classes = new(ContentPaths.ResolveContentRoot());
    private static readonly ShopCatalog Shops = new(ContentPaths.ResolveContentRoot());
    private static readonly CropCatalog Crops = new(ContentPaths.ResolveContentRoot());
    private static readonly MonsterCatalog Monsters = new(ContentPaths.ResolveContentRoot());
    private static readonly SkillCatalog Skills = new(ContentPaths.ResolveContentRoot());

    private sealed class FakeSink : ICharacterSink
    {
        public List<CharacterSave> Saves { get; } = [];

        public void Enqueue(in CharacterSave save) => Saves.Add(save);
    }

    private sealed class FakeInventorySink : IInventorySink
    {
        public List<InventorySave> Saves { get; } = [];

        public void Enqueue(in InventorySave save) => Saves.Add(save);
    }

    private sealed class FakeEconomySink : IEconomySink
    {
        public List<EconomySave> Saves { get; } = [];

        public void Enqueue(in EconomySave save) => Saves.Add(save);
    }

    private sealed class FakeFarmSink : IFarmSink
    {
        public List<FarmTileSave> Saves { get; } = [];

        public void Enqueue(in FarmTileSave save) => Saves.Add(save);
    }

    private sealed class FakeCombatLogSink : ICombatLogSink
    {
        public List<CombatLogSave> Saves { get; } = [];

        public void Enqueue(in CombatLogSave save) => Saves.Add(save);
    }

    private sealed class FakeChatLogSink : IChatLogSink
    {
        public List<ChatLogSave> Saves { get; } = [];

        public void Enqueue(in ChatLogSave save) => Saves.Add(save);
    }

    private sealed class FakeAdminActionSink : IAdminActionSink
    {
        public List<AdminActionSave> Saves { get; } = [];

        public void Enqueue(in AdminActionSave save) => Saves.Add(save);
    }

    private static (GameWorld World, WorldInbox Inbox, FakeSink Sink) Build(int saveIntervalSeconds = 30)
    {
        var maps = new MapCatalog(ContentPaths.ResolveContentRoot());
        var inbox = new WorldInbox();
        var sink = new FakeSink();
        var shopRuntime = new ShopRuntime(Shops, []);
        var farmRuntime = new FarmRuntime([], [], FarmCalendar.DayIndex(DateTimeOffset.UtcNow));
        var world = new GameWorld(
            maps, inbox, sink, Items, Classes, new FakeInventorySink(),
            Shops, shopRuntime, new FakeEconomySink(), Crops, farmRuntime, new FakeFarmSink(),
            Monsters, Skills, new FakeCombatLogSink(), new FakeChatLogSink(), new FakeAdminActionSink(),
            new EntityIdAllocator(), saveIntervalSeconds);
        return (world, inbox, sink);
    }

    /// <summary>
    /// Los NPCs de tienda (Fase 7) se registran al construir el mundo y ya ocupan los primeros
    /// ids del <c>EntityIdAllocator</c> real que usa <c>GameWorld</c>. Estos tests montan su
    /// <see cref="WorldJoinRequest"/> a mano, sin pasar por ese allocator (un jugador de verdad sí
    /// lo hace, en <c>CharSelect</c>) — así que sus ids de prueba tienen que quedar por encima de
    /// cualquier NPC para no pisarlos en <c>_entities</c>. Con margen de sobra para las tiendas
    /// que se añadan más adelante.
    /// </summary>
    private const int TestEntityIdBase = 1000;

    private static WorldJoinRequest VillageJoin(int entityId, Vec2 position, long characterId, long gold = 0, bool isAdmin = false) => new(
        TestEntityIdBase + entityId,
        characterId,
        $"Jugador{entityId}",
        "class.warrior",
        "map.village",
        position,
        Facing.South,
        PaletteIndex: 0,
        Hp: 100,
        HpMax: 120,
        Mp: 50,
        MpMax: 50,
        StatStr: 8,
        StatInt: 2,
        StatVit: 6,
        StatDex: 4,
        Items: [],
        Gold: gold,
        Level: 1,
        Xp: 0,
        StatPoints: 0, AccountId: entityId, IsAdmin: isAdmin);

    private static void PostInput(WorldInbox inbox, int sessionId, uint seq, int dirX, int dirY)
    {
        var payload = MessagePackSerializer.Serialize(
            new C2SInputState
            {
                Seq = seq,
                DirX = (sbyte)dirX,
                DirY = (sbyte)dirY,
                Facing = Facing.South,
                Flags = 0,
                DtMs = 50,
            },
            FrameCodec.Options);

        inbox.Post(sessionId, Opcode.InputState, payload);
    }

    private static void PostChat(WorldInbox inbox, int sessionId, ChatChannel channel, string text)
    {
        var payload = MessagePackSerializer.Serialize(
            new C2SChatSend { Channel = channel, Text = text }, FrameCodec.Options);

        inbox.Post(sessionId, Opcode.ChatSend, payload);
    }

    [Fact]
    public void ElMapaDelPueblo_SeCargaYTieneSuZona()
    {
        var (world, _, _) = Build();

        Assert.Contains(world.Zones, zone => zone.Map.Key == "map.village");
    }

    /// <summary>
    /// Las dos zonas exteriores nuevas (Fase 12) cargan como cualquier otro mapa —
    /// <c>GameWorld</c> ya crea una <c>Zone</c> por cada uno desde la Fase 4, genérico— con una
    /// región segura junto al spawn y otra <c>pvp</c> para el resto, mismo patrón que
    /// <c>campo_norte</c> en <c>map.village</c>.
    /// </summary>
    [Theory]
    [InlineData("map.forest")]
    [InlineData("map.mountain")]
    public void LasZonasExterioresNuevas_CarganConSuRegionSeguraYSuRegionPvp(string mapKey)
    {
        var (world, _, _) = Build();

        var zone = world.Zones.Single(z => z.Map.Key == mapKey);
        Assert.Contains(zone.Map.Regions.Regions, r => r.Flags.HasFlag(ZoneFlags.Safe));
        Assert.Contains(zone.Map.Regions.Regions, r => r.Flags.HasFlag(ZoneFlags.Pvp));
    }

    /// <summary>El barrido va a 1 Hz (Fase 9): hace falta un segundo entero de ticks para que el spawner actúe.</summary>
    [Theory]
    [InlineData("map.forest")]
    [InlineData("map.mountain")]
    public void LasZonasExterioresNuevas_ElSpawnerLasPuebla(string mapKey)
    {
        var (world, _, _) = Build();

        for (long tick = 1; tick <= SimulationConstants.TickRate; tick++)
        {
            world.Tick(tick, tick * SimulationConstants.TickDtMs);
        }

        var zone = world.Zones.Single(z => z.Map.Key == mapKey);
        Assert.True(zone.Monsters.Count > 0, $"el spawner de {mapKey} tendría que haber poblado sus puntos");
    }

    [Fact]
    public void UnJoin_MaterializaLaEntidadEnElTickSiguiente()
    {
        var (world, inbox, _) = Build();
        var peer = new FakeWorldPeer(1);

        // world.EntityCount arranca por encima de 0: los NPCs de tienda (Fase 7) ya están
        // registrados al construir el mundo, antes de que nadie se una. Se compara contra el
        // valor de salida, no contra una cuenta fija, para no volver a romperse la próxima vez
        // que se añada una tienda.
        var entitiesBeforeJoin = world.EntityCount;

        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        Assert.Equal(0, world.PlayerCount);

        world.Tick(1, 50);

        Assert.Equal(1, world.PlayerCount);
        Assert.Equal(entitiesBeforeJoin + 1, world.EntityCount);
    }

    /// <summary>
    /// Regresión: el oro guardado (<c>characters.gold</c>) viajaba hasta <c>WorldEnter</c> para
    /// pintarlo en el cliente, pero <see cref="WorldJoinRequest"/> nunca lo llevaba y
    /// <c>PlayerEntity.Gold</c> se quedaba en su valor por defecto, 0 — un reconectar silencioso
    /// habría sobrescrito el oro real de Postgres con 0 en el siguiente barrido de guardado
    /// (hallazgo de la verificación E2E de la Fase 7).
    /// </summary>
    [Fact]
    public void UnJoin_ConservaElOroGuardado()
    {
        var (world, inbox, _) = Build();
        var peer = new FakeWorldPeer(1);

        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100, gold: 250)));
        world.Tick(1, 50);

        var player = world.Zones.First(z => z.Map.Key == "map.village").FindBySession(1);
        Assert.NotNull(player);
        Assert.Equal(250, player.Gold);
    }

    /// <summary>
    /// Los monstruos aparecen solos en los puntos de <c>content/maps/</c> (Fase 9). El barrido va a
    /// 1 Hz, así que hace falta un segundo entero de ticks: comprobarlo en el tick 1 no probaría
    /// nada.
    /// </summary>
    [Fact]
    public void LosMonstruos_AparecenSolosEnSusPuntos()
    {
        var (world, _, _) = Build();

        Assert.Equal(0, world.MonsterCount);

        for (long tick = 1; tick <= SimulationConstants.TickRate; tick++)
        {
            world.Tick(tick, tick * SimulationConstants.TickDtMs);
        }

        Assert.True(world.MonsterCount > 0, "el spawner tendría que haber poblado los puntos del mapa");
    }

    /// <summary>
    /// Regresión de la Fase 9: <c>characters.hp/mp/xp</c> existen desde la Fase 2 pero no se
    /// escribían nunca — con combate, un moribundo se curaría del todo reconectando (§2 D12).
    /// </summary>
    [Fact]
    public void AlGuardar_ViajanVidaYExperiencia()
    {
        var (world, inbox, sink) = Build();
        var peer = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);

        var player = world.Zones.First(z => z.Map.Key == "map.village").FindBySession(1);
        Assert.NotNull(player);
        player.Hp = 37;
        player.Xp = 1234;
        player.VitalsDirty = true;

        inbox.PostControl(new PlayerLeaveCommand(1));
        world.Tick(2, 100);

        var save = Assert.Single(sink.Saves);
        Assert.Equal(37, save.Hp);
        Assert.Equal(1234, save.Xp);
    }

    [Fact]
    public void UnInputState_MueveLaEntidadAutoritativa()
    {
        var (world, inbox, _) = Build();
        var peer = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);

        for (uint i = 1; i <= 10; i++)
        {
            PostInput(inbox, 1, i, 0, -1);
            world.Tick(1 + i, (1 + i) * 50);
        }

        var player = world.Zones.First(z => z.Map.Key == "map.village").FindBySession(1);
        Assert.NotNull(player);
        Assert.Equal(60.5f - (10 * 0.2f), player.State.Pos.Y, 1e-4f);
    }

    /// <summary>
    /// Una dirección que no es -1, 0 o 1 no la manda un cliente honesto: no se clampa en silencio,
    /// se cierra la sesión (FASE-04 §8).
    /// </summary>
    [Fact]
    public void UnInputStateConDireccionInvalida_CierraLaSesion()
    {
        var (world, inbox, _) = Build();
        var peer = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);

        PostInput(inbox, 1, 1, 7, 0);
        world.Tick(2, 100);

        Assert.True(peer.Kicked);
        Assert.Equal(KickReason.ProtocolError, peer.KickedReason);
    }

    [Fact]
    public void AlSalir_SeGuardaLaPosicionFinalAunqueNoTocaraElBarrido()
    {
        var (world, inbox, sink) = Build();
        var peer = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);

        for (uint i = 1; i <= 10; i++)
        {
            PostInput(inbox, 1, i, 1, 0);
            world.Tick(1 + i, (1 + i) * 50);
        }

        inbox.PostControl(new PlayerLeaveCommand(1));
        world.Tick(12, 600);

        var save = Assert.Single(sink.Saves);
        Assert.Equal(100, save.CharacterId);
        Assert.Equal("map.village", save.MapKey);
        Assert.Equal(48.5f + (10 * 0.2f), save.X, 1e-4f);
        Assert.Equal(0, world.PlayerCount);
    }

    [Fact]
    public void CadaCiertoTiempo_LaPosicionSeGuardaSola()
    {
        var (world, inbox, sink) = Build(saveIntervalSeconds: 1);
        var peer = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);

        for (uint i = 1; i <= 60; i++)
        {
            PostInput(inbox, 1, i, 1, 0);
            world.Tick(1 + i, (1 + i) * 50);
        }

        Assert.NotEmpty(sink.Saves);
        Assert.All(sink.Saves, save => Assert.Equal(100, save.CharacterId));
    }

    [Fact]
    public void SinMoverse_ElBarridoNoEscribeNada()
    {
        var (world, inbox, sink) = Build(saveIntervalSeconds: 1);
        var peer = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));

        for (long tick = 1; tick <= 60; tick++)
        {
            world.Tick(tick, tick * 50);
        }

        Assert.Empty(sink.Saves);
    }

    /// <summary>
    /// Dos sesiones con el mismo personaje se pisarían la posición al guardar. Gana la nueva y la
    /// vieja se va con <see cref="KickReason.LoggedInElsewhere"/>.
    /// </summary>
    [Fact]
    public void ConElMismoPersonajeDosVeces_SeEchaALaSesionAntigua()
    {
        var (world, inbox, _) = Build();
        var vieja = new FakeWorldPeer(1);
        var nueva = new FakeWorldPeer(2);

        inbox.PostControl(new PlayerJoinCommand(vieja, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);
        inbox.PostControl(new PlayerJoinCommand(nueva, VillageJoin(2, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(2, 100);

        Assert.True(vieja.Kicked);
        Assert.Equal(KickReason.LoggedInElsewhere, vieja.KickedReason);
        Assert.False(nueva.Kicked);
        Assert.Equal(1, world.PlayerCount);
    }

    [Fact]
    public void AlApagar_SeVuelcanTodasLasPosiciones()
    {
        var (world, inbox, sink) = Build();
        var peer = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);

        for (uint i = 1; i <= 5; i++)
        {
            PostInput(inbox, 1, i, 1, 0);
            world.Tick(1 + i, (1 + i) * 50);
        }

        world.FlushAllState();

        Assert.Single(sink.Saves);
    }

    [Fact]
    public void UnMensajeGlobal_LeLlegaATodosLosDemas()
    {
        var (world, inbox, _) = Build();
        var a = new FakeWorldPeer(1);
        var b = new FakeWorldPeer(2);
        inbox.PostControl(new PlayerJoinCommand(a, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        inbox.PostControl(new PlayerJoinCommand(b, VillageJoin(2, new Vec2(49.5f, 60.5f), 101)));
        world.Tick(1, 50);

        PostChat(inbox, 1, ChatChannel.Global, "hola a todos");
        world.Tick(2, 100);

        var received = b.Messages<S2CChatMessage>(Opcode.ChatMessage).Single();
        Assert.Equal(ChatChannel.Global, received.Channel);
        Assert.Equal("Jugador1", received.SenderName);
        Assert.Equal("hola a todos", received.Text);
    }

    [Fact]
    public void UnSusurro_SoloLeLlegaAlDestinatario()
    {
        var (world, inbox, _) = Build();
        var a = new FakeWorldPeer(1);
        var b = new FakeWorldPeer(2);
        var c = new FakeWorldPeer(3);
        inbox.PostControl(new PlayerJoinCommand(a, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        inbox.PostControl(new PlayerJoinCommand(b, VillageJoin(2, new Vec2(49.5f, 60.5f), 101)));
        inbox.PostControl(new PlayerJoinCommand(c, VillageJoin(3, new Vec2(50.5f, 60.5f), 102)));
        world.Tick(1, 50);

        PostChat(inbox, 1, ChatChannel.Global, "/w Jugador2 esto es privado");
        world.Tick(2, 100);

        var receivedByTarget = b.Messages<S2CChatMessage>(Opcode.ChatMessage).Single();
        Assert.Equal(ChatChannel.Whisper, receivedByTarget.Channel);
        Assert.Equal("esto es privado", receivedByTarget.Text);
        Assert.Empty(c.Messages<S2CChatMessage>(Opcode.ChatMessage));

        // Eco: quien susurra también ve lo que mandó.
        Assert.Single(a.Messages<S2CChatMessage>(Opcode.ChatMessage));
    }

    [Fact]
    public void UnSusurroAUnNombreQueNoExiste_SeRechaza()
    {
        var (world, inbox, _) = Build();
        var a = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(a, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);

        PostChat(inbox, 1, ChatChannel.Global, "/w Fantasma hola");
        world.Tick(2, 100);

        Assert.Empty(a.Messages<S2CChatMessage>(Opcode.ChatMessage));
        var failure = a.Messages<S2CSystemMessage>(Opcode.SystemMessage).Single();
        Assert.Equal($"chat.{ResultCode.TargetNotFound}", failure.Key);
    }

    [Fact]
    public void UnComandoDeAdmin_SinSerAdmin_SeRechaza()
    {
        var (world, inbox, _) = Build();
        var noAdmin = new FakeWorldPeer(1);
        var target = new FakeWorldPeer(2);
        inbox.PostControl(new PlayerJoinCommand(noAdmin, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        inbox.PostControl(new PlayerJoinCommand(target, VillageJoin(2, new Vec2(49.5f, 60.5f), 101)));
        world.Tick(1, 50);

        PostChat(inbox, 1, ChatChannel.Global, "/kick Jugador2 porque sí");
        world.Tick(2, 100);

        Assert.False(target.Kicked);
        var failure = noAdmin.Messages<S2CSystemMessage>(Opcode.SystemMessage).Single();
        Assert.Equal($"chat.{ResultCode.NotAuthorized}", failure.Key);
    }

    [Fact]
    public void UnKickDeUnAdmin_ExpulsaAlObjetivoYQuedaAuditado()
    {
        var (world, inbox, _) = Build();
        var admin = new FakeWorldPeer(1);
        var target = new FakeWorldPeer(2);
        inbox.PostControl(new PlayerJoinCommand(admin, VillageJoin(1, new Vec2(48.5f, 60.5f), 100, isAdmin: true)));
        inbox.PostControl(new PlayerJoinCommand(target, VillageJoin(2, new Vec2(49.5f, 60.5f), 101)));
        world.Tick(1, 50);

        PostChat(inbox, 1, ChatChannel.Global, "/kick Jugador2 se porta mal");
        world.Tick(2, 100);

        Assert.True(target.Kicked);
        Assert.Equal(KickReason.Banned, target.KickedReason);
    }

    [Fact]
    public void QuienNoEsAdmin_PuedeUsarWhoYHelp()
    {
        var (world, inbox, _) = Build();
        var a = new FakeWorldPeer(1);
        inbox.PostControl(new PlayerJoinCommand(a, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        world.Tick(1, 50);

        PostChat(inbox, 1, ChatChannel.Global, "/who");
        PostChat(inbox, 1, ChatChannel.Global, "/help");
        world.Tick(2, 100);

        var messages = a.Messages<S2CSystemMessage>(Opcode.SystemMessage).ToList();
        Assert.Contains(messages, m => m.Key == "chat.who" && m.Args.Contains("Jugador1"));
        Assert.Contains(messages, m => m.Key == "chat.help" && m.Args.Length > 0);
    }
}

using Epimeteo.Server.Content;
using Epimeteo.Server.Inventory;
using Epimeteo.Server.Persistence.Items;
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

    private sealed class FakeSink : IPositionSink
    {
        public List<PositionSave> Saves { get; } = [];

        public void Enqueue(in PositionSave save) => Saves.Add(save);
    }

    private sealed class FakeInventorySink : IInventorySink
    {
        public List<InventorySave> Saves { get; } = [];

        public void Enqueue(in InventorySave save) => Saves.Add(save);
    }

    private static (GameWorld World, WorldInbox Inbox, FakeSink Sink) Build(int saveIntervalSeconds = 30)
    {
        var maps = new MapCatalog(ContentPaths.ResolveContentRoot());
        var inbox = new WorldInbox();
        var sink = new FakeSink();
        var world = new GameWorld(maps, inbox, sink, Items, Classes, new FakeInventorySink(), saveIntervalSeconds);
        return (world, inbox, sink);
    }

    private static WorldJoinRequest VillageJoin(int entityId, Vec2 position, long characterId) => new(
        entityId,
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
        Items: []);

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

    [Fact]
    public void ElMapaDelPueblo_SeCargaYTieneSuZona()
    {
        var (world, _, _) = Build();

        Assert.Contains(world.Zones, zone => zone.Map.Key == "map.village");
    }

    [Fact]
    public void UnJoin_MaterializaLaEntidadEnElTickSiguiente()
    {
        var (world, inbox, _) = Build();
        var peer = new FakeWorldPeer(1);

        inbox.PostControl(new PlayerJoinCommand(peer, VillageJoin(1, new Vec2(48.5f, 60.5f), 100)));
        Assert.Equal(0, world.PlayerCount);

        world.Tick(1, 50);

        Assert.Equal(1, world.PlayerCount);
        Assert.Equal(1, world.EntityCount);
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

        var player = world.Zones.First().FindBySession(1);
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
}

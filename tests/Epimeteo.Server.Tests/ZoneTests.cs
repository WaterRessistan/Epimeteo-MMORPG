using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// La zona entera sin red ni BD: entidades, AOI, snapshots y flags de región. Es donde se
/// comprueba que el servidor manda lo justo y a quien toca.
/// </summary>
public sealed class ZoneTests
{
    [Fact]
    public void AlEntrar_ElJugadorRecibeSusFlagsDeRegion()
    {
        var map = TestWorld.Map(96, 96,
            new MapRegionDefinition { Name = "plaza", Rect = [40, 40, 16, 16], Flags = ["safe"] });
        var zone = new Zone(map);
        var peer = new FakeWorldPeer(1);

        zone.Join(peer, TestWorld.Join(1, new Vec2(48f, 48f)), tick: 0, nowMs: 0);

        var update = peer.Last<S2CZoneFlagsUpdate>(Opcode.ZoneFlagsUpdate);
        Assert.NotNull(update);
        Assert.Equal("plaza", update.RegionName);
        Assert.Equal(ZoneFlags.Safe, update.Flags);
    }

    [Fact]
    public void AlCruzarDeRegion_LlegaUnZoneFlagsUpdateNuevo()
    {
        var map = TestWorld.Map(96, 96,
            new MapRegionDefinition { Name = "campo", Rect = [1, 1, 94, 40], Flags = ["pvp"] },
            new MapRegionDefinition { Name = "pueblo", Rect = [1, 41, 94, 54], Flags = ["safe"] });
        var zone = new Zone(map);
        var peer = new FakeWorldPeer(1);
        zone.Join(peer, TestWorld.Join(1, new Vec2(48f, 42f)), tick: 0, nowMs: 0);
        peer.Clear();

        uint seq = 1;
        long tick = 0;
        TestWorld.Walk(zone, peer, 0, -1, ticks: 20, ref seq, ref tick);

        var update = peer.Last<S2CZoneFlagsUpdate>(Opcode.ZoneFlagsUpdate);
        Assert.NotNull(update);
        Assert.Equal("campo", update.RegionName);
        Assert.Equal(ZoneFlags.Pvp, update.Flags);
    }

    [Fact]
    public void DosJugadoresLejos_NoSeVen()
    {
        var zone = new Zone(TestWorld.Map());
        var a = new FakeWorldPeer(1);
        var b = new FakeWorldPeer(2);

        zone.Join(a, TestWorld.Join(1, new Vec2(8f, 8f)), 0, 0);
        zone.Join(b, TestWorld.Join(2, new Vec2(88f, 88f)), 0, 0);
        zone.Tick(1, 50);

        Assert.Empty(TestWorld.SpawnedIds(a));
        Assert.Empty(TestWorld.SpawnedIds(b));
    }

    [Fact]
    public void DosJugadoresCerca_SeVenAlEntrar()
    {
        var zone = new Zone(TestWorld.Map());
        var a = new FakeWorldPeer(1);
        var b = new FakeWorldPeer(2);

        zone.Join(a, TestWorld.Join(1, new Vec2(48f, 48f)), 0, 0);
        zone.Join(b, TestWorld.Join(2, new Vec2(50f, 48f)), 0, 0);
        zone.Tick(1, 50);

        Assert.Contains(2, TestWorld.SpawnedIds(a));
        Assert.Contains(1, TestWorld.SpawnedIds(b));
    }

    /// <summary>
    /// El corazón del AOI: acercarse dispara <c>EntitySpawn</c> <b>una sola vez</b>, no uno por
    /// tick, y alejarse dispara <c>EntityDespawn</c> con motivo <c>OutOfRange</c>.
    /// </summary>
    [Fact]
    public void AlAcercarseYAlejarse_LlegaUnSpawnYUnDespawn()
    {
        var zone = new Zone(TestWorld.Map());
        var quieto = new FakeWorldPeer(1);
        var andante = new FakeWorldPeer(2);

        // Con celdas de 16 tiles, el que anda empieza a tres celdas de distancia.
        zone.Join(quieto, TestWorld.Join(1, new Vec2(20f, 20f)), 0, 0);
        zone.Join(andante, TestWorld.Join(2, new Vec2(85f, 20f)), 0, 0);
        quieto.Clear();
        andante.Clear();

        uint seq = 1;
        long tick = 0;

        // Se acerca hasta la celda contigua: 4 tiles/s × 0.05 s × 200 ticks = 40 tiles.
        TestWorld.Walk(zone, andante, -1, 0, ticks: 200, ref seq, ref tick);

        Assert.Equal([2], TestWorld.SpawnedIds(quieto).ToArray());
        Assert.Equal([1], TestWorld.SpawnedIds(andante).ToArray());
        Assert.Empty(TestWorld.DespawnedIds(quieto));

        quieto.Clear();
        andante.Clear();

        // Y vuelve por donde vino.
        TestWorld.Walk(zone, andante, 1, 0, ticks: 200, ref seq, ref tick);

        var despawns = TestWorld.DespawnedIds(quieto).ToArray();
        Assert.Single(despawns);
        Assert.Equal(2, despawns[0].Id);
        Assert.Equal(DespawnReason.OutOfRange, despawns[0].Reason);
    }

    [Fact]
    public void AlSalirDelMundo_LosDemasRecibenDespawnDeLogout()
    {
        var zone = new Zone(TestWorld.Map());
        var a = new FakeWorldPeer(1);
        var b = new FakeWorldPeer(2);
        zone.Join(a, TestWorld.Join(1, new Vec2(48f, 48f)), 0, 0);
        zone.Join(b, TestWorld.Join(2, new Vec2(50f, 48f)), 0, 0);
        zone.Tick(1, 50);
        a.Clear();

        zone.Leave(b.Id);

        var despawns = TestWorld.DespawnedIds(a).ToArray();
        Assert.Single(despawns);
        Assert.Equal(DespawnReason.Logout, despawns[0].Reason);
        Assert.Single(zone.Players);
        Assert.Single(zone.Entities);
        Assert.Null(zone.FindBySession(b.Id));
    }

    [Fact]
    public void LosSnapshots_SalenADiezHerciosYSiempreLlevanLaEntidadPropia()
    {
        var zone = new Zone(TestWorld.Map());
        var peer = new FakeWorldPeer(1);
        zone.Join(peer, TestWorld.Join(1, new Vec2(48f, 48f)), 0, 0);
        peer.Clear();

        for (long tick = 1; tick <= 20; tick++)
        {
            zone.Tick(tick, tick * 50);
        }

        var snapshots = peer.Messages<S2CSnapshot>(Opcode.Snapshot).ToArray();

        // 20 ticks a 20 Hz = 1 s ⇒ 10 snapshots.
        Assert.Equal(10, snapshots.Length);
        Assert.All(snapshots, snapshot => Assert.Contains(snapshot.Entities, entity => entity.Id == 1));
    }

    [Fact]
    public void UnaEntidadQuieta_NoSeRepiteEnLosSnapshots()
    {
        var zone = new Zone(TestWorld.Map());
        var a = new FakeWorldPeer(1);
        var b = new FakeWorldPeer(2);
        zone.Join(a, TestWorld.Join(1, new Vec2(48f, 48f)), 0, 0);
        zone.Join(b, TestWorld.Join(2, new Vec2(50f, 48f)), 0, 0);

        for (long tick = 1; tick <= 10; tick++)
        {
            zone.Tick(tick, tick * 50);
        }

        var snapshots = a.Messages<S2CSnapshot>(Opcode.Snapshot).ToArray();
        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, snapshot => Assert.Single(snapshot.Entities));
    }

    [Fact]
    public void ElSnapshot_AcusaReciboDelUltimoInputConsumido()
    {
        var zone = new Zone(TestWorld.Map());
        var peer = new FakeWorldPeer(1);
        zone.Join(peer, TestWorld.Join(1, new Vec2(48f, 48f)), 0, 0);

        uint seq = 1;
        long tick = 0;
        TestWorld.Walk(zone, peer, 1, 0, ticks: 10, ref seq, ref tick);

        var snapshot = peer.Last<S2CSnapshot>(Opcode.Snapshot);
        Assert.NotNull(snapshot);
        Assert.Equal(10u, snapshot.LastAckedInputSeq);
    }

    [Fact]
    public void ElMovimiento_LoDecideElServidorYLoParaLaPared()
    {
        var zone = new Zone(TestWorld.Map(32, 32));
        var peer = new FakeWorldPeer(1);
        zone.Join(peer, TestWorld.Join(1, new Vec2(16f, 16f)), 0, 0);

        uint seq = 1;
        long tick = 0;
        TestWorld.Walk(zone, peer, 1, 0, ticks: 400, ref seq, ref tick);

        var player = zone.FindBySession(1);
        Assert.NotNull(player);
        Assert.Equal(31f - SimulationConstants.PlayerHalfWidth, player.State.Pos.X, 1e-4f);
    }

    [Fact]
    public void SinInputs_ElJugadorSeQuedaQuieto()
    {
        var zone = new Zone(TestWorld.Map());
        var peer = new FakeWorldPeer(1);
        zone.Join(peer, TestWorld.Join(1, new Vec2(48f, 48f)), 0, 0);

        for (long tick = 1; tick <= 40; tick++)
        {
            zone.Tick(tick, tick * 50);
        }

        var player = zone.FindBySession(1);
        Assert.NotNull(player);
        Assert.Equal(new Vec2(48f, 48f), player.State.Pos);
        Assert.Equal(AnimState.Idle, player.State.Anim);
    }

    /// <summary>
    /// Una posición guardada que hoy cae dentro de un muro (el mapa se editó entre sesiones) no
    /// puede dejar al jugador atrapado: entra por el spawn del mapa.
    /// </summary>
    [Fact]
    public void ConLaPosicionGuardadaDentroDeUnMuro_SeEntraPorElSpawn()
    {
        var map = TestWorld.Map(32, 32);
        var zone = new Zone(map);
        var peer = new FakeWorldPeer(1);

        zone.Join(peer, TestWorld.Join(1, new Vec2(0.5f, 0.5f)), 0, 0);

        var player = zone.FindBySession(1);
        Assert.NotNull(player);
        Assert.Equal(map.Spawn, player.State.Pos);
    }
}

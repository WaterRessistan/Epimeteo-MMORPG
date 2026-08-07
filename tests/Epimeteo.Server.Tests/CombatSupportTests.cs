using Epimeteo.Server.Combat;
using Epimeteo.Server.Content;
using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Rebobinado de la compensación de latencia (FASE-09 §2 D1 y D2). Es la pieza que decide a quién
/// alcanza un golpe con latencia real de por medio, así que se prueba sola.
/// </summary>
public sealed class PositionHistoryTests
{
    [Fact]
    public void SinHistorial_DevuelveLaPosicionActual()
    {
        var history = new PositionHistory();
        var current = new Vec2(10, 10);

        Assert.Equal(current, history.PositionAt(1000, rewindMs: 100, current));
    }

    [Fact]
    public void SinRebobinar_DevuelveLaPosicionActual()
    {
        var history = new PositionHistory();
        history.Record(1000, new Vec2(1, 1));
        var current = new Vec2(10, 10);

        Assert.Equal(current, history.PositionAt(1000, rewindMs: 0, current));
    }

    /// <summary>Lo que compra todo esto: el golpe se resuelve donde estaba la víctima, no donde está.</summary>
    [Fact]
    public void RebobinaALaPosicionQueOcupabaEntonces()
    {
        var history = new PositionHistory();

        // Se mueve un tile por tick, 10 ticks: en t=1000 estaba en x=0, en t=1500 en x=10.
        for (var i = 0; i <= 10; i++)
        {
            history.Record(1000 + (i * SimulationConstants.TickDtMs), new Vec2(i, 0));
        }

        var current = new Vec2(10, 0);
        var rewound = history.PositionAt(1500, rewindMs: 200, current);

        // 200 ms son 4 ticks a 20 Hz: x = 10 - 4 = 6.
        Assert.Equal(6f, rewound.X, 0.001f);
    }

    /// <summary>
    /// Más allá de la ventana que se guarda no se inventa nada: se valida contra la posición
    /// actual, como manda <c>docs/00 §6</c>.
    /// </summary>
    [Fact]
    public void MasAllaDelHistorial_CaeALaPosicionActual()
    {
        var history = new PositionHistory();
        history.Record(10_000, new Vec2(1, 1));

        var current = new Vec2(50, 50);

        Assert.Equal(current, history.PositionAt(30_000, rewindMs: 200, current));
    }

    [Fact]
    public void ElAnilloNoCreceSinLimite()
    {
        var history = new PositionHistory();

        for (var i = 0; i < 1000; i++)
        {
            history.Record(i * SimulationConstants.TickDtMs, new Vec2(i, 0));
        }

        Assert.Equal(CombatConstants.PositionHistoryMs / SimulationConstants.TickDtMs, history.Count);
    }

    /// <summary>
    /// El tope de rebobinado es lo que acota lo que gana un cliente que mienta con su RTT (D1):
    /// por mucho que infle, no pasa de <c>MaxRewindMs</c>.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 50)]
    [InlineData(400, 200)]
    [InlineData(5000, CombatConstants.MaxRewindMs)]
    [InlineData(-50, 0)]
    public void RewindFor_EsLaMitadDelRttConTope(int rttMs, int expected)
    {
        Assert.Equal(expected, PositionHistory.RewindFor(rttMs));
    }

    /// <summary>
    /// Al teletransportar (reaparecer tras morir) el historial se olvida: si no, un golpe
    /// rebobinado alcanzaría al jugador en el sitio donde acaba de morir.
    /// </summary>
    [Fact]
    public void Reset_OlvidaElHistorialAnterior()
    {
        var history = new PositionHistory();
        for (var i = 0; i <= 10; i++)
        {
            history.Record(1000 + (i * SimulationConstants.TickDtMs), new Vec2(i, 0));
        }

        history.Reset(1500, new Vec2(99, 99));

        Assert.Equal(1, history.Count);
        Assert.Equal(new Vec2(99, 99), history.PositionAt(1500, rewindMs: 200, new Vec2(99, 99)));
    }
}

/// <summary>Tabla de amenaza (FASE-09 §2 D6).</summary>
public sealed class AggroTableTests
{
    [Fact]
    public void VaciaNoTieneObjetivo()
    {
        var table = new AggroTable();

        Assert.Null(table.Top());
        Assert.False(table.Any);
    }

    [Fact]
    public void ElObjetivoEsQuienMasAmenazaLleva()
    {
        var table = new AggroTable();
        table.Add(1, 10);
        table.Add(2, 25);
        table.Add(3, 5);

        Assert.Equal(2, table.Top());
    }

    [Fact]
    public void LaAmenazaSeAcumula()
    {
        var table = new AggroTable();
        table.Add(1, 10);
        table.Add(1, 15);
        table.Add(2, 20);

        Assert.Equal(1, table.Top());
    }

    /// <summary>El desempate tiene que ser estable, o el monstruo cambiaría de objetivo cada tick.</summary>
    [Fact]
    public void ConEmpate_GanaElIdMasBajoYNoOscila()
    {
        var table = new AggroTable();
        table.Add(7, 10);
        table.Add(3, 10);

        Assert.Equal(3, table.Top());
        Assert.Equal(3, table.Top());
    }

    [Fact]
    public void AmenazaNoPositiva_SeIgnora()
    {
        var table = new AggroTable();
        table.Add(1, 0);
        table.Add(2, -5);

        Assert.False(table.Any);
    }

    [Fact]
    public void RemoveYClear_VacianLaTabla()
    {
        var table = new AggroTable();
        table.Add(1, 10);
        table.Add(2, 20);

        table.Remove(2);
        Assert.Equal(1, table.Top());

        table.Clear();
        Assert.False(table.Any);
    }
}

/// <summary>La máquina de estados de los monstruos (FASE-09 §2 D7 y D8).</summary>
public sealed class MonsterAiTests
{
    private static MonsterDefinition Definition(float aggro = 5f, float leash = 12f) => new()
    {
        Key = "monster.test",
        DisplayName = "Prueba",
        Level = 1,
        HpMax = 40,
        Attack = 5,
        Defense = 1,
        Dex = 0,
        MoveSpeedTiles = 4,
        AttackRangeTiles = 1.2f,
        AttackCooldownMs = 1000,
        AggroRadiusTiles = aggro,
        LeashRadiusTiles = leash,
        XpReward = 5,
    };

    private static GameMap Map() => TestWorld.Map(64, 64);

    private static PlayerEntity Player(int id, Vec2 position) => new(
        id, new FakeWorldPeer(id), id, "class.warrior", $"J{id}",
        MoveState.AtRest(position, Facing.South), 0, [])
    {
        Hp = 100,
        HpMax = 100,
    };

    [Fact]
    public void SinNadieCerca_SeQuedaTranquilo()
    {
        var monster = new MonsterEntity(1, Definition(), new Vec2(30, 30), 4, 0);

        var action = MonsterAi.Tick(monster, [], Map(), new DeterministicRng(1), tick: 1, nowMs: 0);

        Assert.Null(action.AttackTargetId);
        Assert.True(monster.AiState is MonsterState.Idle or MonsterState.Patrol);
    }

    [Fact]
    public void ConUnJugadorDentroDelRadio_LoPersigue()
    {
        var monster = new MonsterEntity(1, Definition(), new Vec2(30, 30), 4, 0);
        var player = Player(2, new Vec2(33, 30));

        MonsterAi.Tick(monster, [player], Map(), new DeterministicRng(1), tick: 1, nowMs: 0);

        Assert.Equal(MonsterState.Chase, monster.AiState);
        Assert.Equal(2, monster.Aggro.Top());
    }

    [Fact]
    public void ConUnJugadorLejos_NoSeEntera()
    {
        var monster = new MonsterEntity(1, Definition(aggro: 3f), new Vec2(30, 30), 4, 0);
        var player = Player(2, new Vec2(45, 30));

        MonsterAi.Tick(monster, [player], Map(), new DeterministicRng(1), tick: 1, nowMs: 0);

        Assert.False(monster.Aggro.Any);
    }

    [Fact]
    public void PersiguiendoSeAcerca()
    {
        var monster = new MonsterEntity(1, Definition(), new Vec2(30, 30), 4, 0);
        var player = Player(2, new Vec2(34, 30));
        var map = Map();
        var rng = new DeterministicRng(1);

        MonsterAi.Tick(monster, [player], map, rng, tick: 1, nowMs: 0);
        var before = Vec2.DistanceSquared(monster.State.Pos, player.State.Pos);

        MonsterAi.Tick(monster, [player], map, rng, tick: 2, nowMs: 50);
        var after = Vec2.DistanceSquared(monster.State.Pos, player.State.Pos);

        Assert.True(after < before, "el monstruo tendría que haberse acercado");
    }

    [Fact]
    public void EnAlcanceYConCooldownCumplido_PideAtacar()
    {
        var monster = new MonsterEntity(1, Definition(), new Vec2(30, 30), 4, 0);
        var player = Player(2, new Vec2(31, 30));
        monster.Aggro.Add(player.Id, 10);
        monster.AiState = MonsterState.Chase;

        var action = MonsterAi.Tick(monster, [player], Map(), new DeterministicRng(1), tick: 1, nowMs: 10_000);

        Assert.Equal(MonsterState.Attack, monster.AiState);
        Assert.Equal(2, action.AttackTargetId);
    }

    [Fact]
    public void EnAlcancePeroSinCooldown_NoPideAtacar()
    {
        var monster = new MonsterEntity(1, Definition(), new Vec2(30, 30), 4, 0) { LastAttackMs = 10_000 };
        var player = Player(2, new Vec2(31, 30));
        monster.Aggro.Add(player.Id, 10);
        monster.AiState = MonsterState.Chase;

        var action = MonsterAi.Tick(monster, [player], Map(), new DeterministicRng(1), tick: 1, nowMs: 10_100);

        Assert.Null(action.AttackTargetId);
    }

    /// <summary>
    /// La correa: sin esto, un jugador arrastra un monstruo hasta la plaza y lo suelta encima de
    /// otro (D7).
    /// </summary>
    [Fact]
    public void MasAllaDeLaCorrea_SeRindeYVuelve()
    {
        var monster = new MonsterEntity(1, Definition(leash: 5f), new Vec2(30, 30), 4, 0);
        monster.Aggro.Add(2, 100);
        monster.AiState = MonsterState.Chase;
        monster.MoveTo(MoveState.AtRest(new Vec2(50, 30), Facing.East), 1);

        var player = Player(2, new Vec2(51, 30));
        MonsterAi.Tick(monster, [player], Map(), new DeterministicRng(1), tick: 2, nowMs: 0);

        Assert.Equal(MonsterState.Returning, monster.AiState);
        Assert.False(monster.Aggro.Any);
    }

    [Fact]
    public void AlLlegarACasa_SeCuraYVuelveAIdle()
    {
        var monster = new MonsterEntity(1, Definition(), new Vec2(30, 30), 4, 0);
        monster.AiState = MonsterState.Returning;
        monster.Hp = 5;
        monster.MoveTo(MoveState.AtRest(new Vec2(30.1f, 30), Facing.East), 1);

        MonsterAi.Tick(monster, [], Map(), new DeterministicRng(1), tick: 2, nowMs: 0);

        Assert.Equal(MonsterState.Idle, monster.AiState);
        Assert.Equal(monster.HpMax, monster.Hp);
        Assert.Equal(new Vec2(30, 30), monster.State.Pos);
    }

    [Fact]
    public void UnMonstruoMuerto_NoHaceNada()
    {
        var monster = new MonsterEntity(1, Definition(), new Vec2(30, 30), 4, 0);
        monster.Hp = 0;
        var player = Player(2, new Vec2(31, 30));

        var action = MonsterAi.Tick(monster, [player], Map(), new DeterministicRng(1), tick: 1, nowMs: 0);

        Assert.Null(action.AttackTargetId);
        Assert.False(monster.Aggro.Any);
    }

    /// <summary>Si su objetivo muere, deja de perseguirlo en vez de quedarse pegado a un cadáver.</summary>
    [Fact]
    public void SiSuObjetivoMuere_LoSacaDeLaTabla()
    {
        var monster = new MonsterEntity(1, Definition(), new Vec2(30, 30), 4, 0);
        var player = Player(2, new Vec2(31, 30));
        monster.Aggro.Add(player.Id, 50);
        monster.AiState = MonsterState.Chase;

        player.Hp = 0;
        MonsterAi.Tick(monster, [player], Map(), new DeterministicRng(1), tick: 2, nowMs: 0);

        Assert.False(monster.Aggro.Any);
        Assert.Equal(MonsterState.Returning, monster.AiState);
    }
}

/// <summary>Contenido real de <c>content/monsters/</c> y sus puntos de aparición en el mapa.</summary>
public sealed class MonsterCatalogTests
{
    private static MonsterCatalog Load() => new(ContentPaths.ResolveContentRoot());

    [Fact]
    public void CargaLosDosMonstruos()
    {
        var catalog = Load();

        Assert.True(catalog.TryGet("monster.slime", out _));
        Assert.True(catalog.TryGet("monster.wolf", out _));
    }

    [Fact]
    public void TodosTienenCorreaMayorQueElAggro()
    {
        foreach (var monster in Load().All)
        {
            Assert.True(
                monster.LeashRadiusTiles > monster.AggroRadiusTiles,
                $"{monster.Key}: la correa tiene que dar más que el aggro o el monstruo oscilaría");
        }
    }

    /// <summary>Si alguien renombra un ítem sin tocar el loot, esto lo caza aquí y no en producción.</summary>
    [Fact]
    public void TodoElLootReferenciaItemsQueExisten()
    {
        var items = new ItemCatalog(ContentPaths.ResolveContentRoot());

        foreach (var monster in Load().All)
        {
            foreach (var entry in monster.Loot)
            {
                Assert.True(items.TryGet(entry.DefKey, out _), $"{monster.Key} suelta '{entry.DefKey}', que no existe");
            }
        }
    }

    /// <summary>
    /// Los puntos de aparición del pueblo tienen que estar en región <c>pvp</c> y nunca en la
    /// plaza, que es <c>no_monsters</c>.
    /// </summary>
    [Fact]
    public void LosSpawnsDelPueblo_EstanFueraDeLaPlazaYApuntanAMonstruosQueExisten()
    {
        var maps = new MapCatalog(ContentPaths.ResolveContentRoot());
        var catalog = Load();

        Assert.True(maps.TryGet("map.village", out var village));
        Assert.NotEmpty(village!.Spawns);

        foreach (var spawn in village.Spawns)
        {
            Assert.True(catalog.TryGet(spawn.MonsterKey, out _), $"spawn de '{spawn.MonsterKey}', que no existe");

            var region = village.Regions.Resolve(new Vec2(spawn.X, spawn.Y));
            Assert.False(
                region.Flags.HasFlag(ZoneFlags.NoMonsters),
                $"hay un spawn de '{spawn.MonsterKey}' en '{region.Name}', que es no_monsters");
            Assert.False(
                village.Collision.IsSolid((int)spawn.X, (int)spawn.Y),
                $"el spawn de '{spawn.MonsterKey}' cae dentro de un muro");
        }
    }
}

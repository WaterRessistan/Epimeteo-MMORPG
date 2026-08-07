using Epimeteo.Server.Combat;
using Epimeteo.Server.Content;
using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Las reglas de PvP de la Fase 9, sin red ni BD. Aquí vive el criterio de aceptación de la fase
/// (<c>docs/03</c>): dos jugadores se pegan en el bosque y no pueden hacerlo en la plaza,
/// <b>ni siquiera atacando desde el borde</b>.
/// </summary>
public sealed class CombatSystemTests
{
    private const int SafeSize = 40;

    /// <summary>Mitad izquierda segura, mitad derecha PvP, sin muros salvo el perímetro.</summary>
    private static GameMap Map() => TestWorld.Map(
        SafeSize, SafeSize,
        new MapRegionDefinition { Name = "plaza", Rect = [0, 0, SafeSize / 2, SafeSize], Flags = ["safe"] },
        new MapRegionDefinition { Name = "bosque", Rect = [SafeSize / 2, 0, SafeSize / 2, SafeSize], Flags = ["pvp"] });

    private static PlayerEntity Player(int id, Vec2 position, int attack = 20)
    {
        var player = new PlayerEntity(
            id, new FakeWorldPeer(id), characterId: id, "class.warrior", $"J{id}",
            MoveState.AtRest(position, Facing.South), nowMs: 0, items: [])
        {
            Hp = 100,
            HpMax = 100,
            AttackPower = attack,
            Defense = 4,
            DexEffective = 0,
        };

        return player;
    }

    private static MonsterEntity Monster(int id, Vec2 position) => new(
        id,
        new MonsterDefinition
        {
            Key = "monster.test",
            DisplayName = "Prueba",
            Level = 1,
            HpMax = 40,
            Attack = 5,
            Defense = 1,
            Dex = 0,
            MoveSpeedTiles = 2,
            AttackRangeTiles = 1.2f,
            AttackCooldownMs = 1000,
            AggroRadiusTiles = 5,
            LeashRadiusTiles = 12,
            XpReward = 5,
        },
        position,
        radius: 4,
        spawnPointIndex: 0);

    private static ResultCode Validate(
        WorldEntity attacker, WorldEntity target, GameMap map, bool pvp = true, bool cooldownReady = true) =>
        CombatSystem.ValidateAttack(
            attacker, target, target.State.Pos, map, CombatConstants.MeleeRangeTiles, pvp, cooldownReady);

    // ── Zona ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnZonaPvp_ElAtaqueEsLegal()
    {
        var map = Map();
        var a = Player(1, new Vec2(25.5f, 10.5f));
        var b = Player(2, new Vec2(26.0f, 10.5f));

        Assert.Equal(ResultCode.Ok, Validate(a, b, map));
    }

    [Fact]
    public void ConElAtacanteEnZonaSegura_SafeZone()
    {
        var map = Map();
        var a = Player(1, new Vec2(19.5f, 10.5f));
        var b = Player(2, new Vec2(20.5f, 10.5f));

        Assert.Equal(ResultCode.SafeZone, Validate(a, b, map));
    }

    [Fact]
    public void ConLaVictimaEnZonaSegura_TargetInSafeZone()
    {
        var map = Map();
        var a = Player(1, new Vec2(20.5f, 10.5f));
        var b = Player(2, new Vec2(19.5f, 10.5f));

        Assert.Equal(ResultCode.TargetInSafeZone, Validate(a, b, map));
    }

    /// <summary>
    /// El criterio de aceptación de <c>docs/03</c>, literal: pegar "desde el borde" de la plaza.
    /// El atacante está en el último tile seguro y la víctima en el primero hostil, dentro de
    /// alcance — y aun así se rechaza, porque D3 exige <c>pvp</c> también en el atacante.
    /// </summary>
    [Fact]
    public void DesdeElBordeDeLaPlaza_NoSePuedeAtacarAlDeFuera()
    {
        var map = Map();
        var attacker = Player(1, new Vec2(19.9f, 10.5f));
        var victim = Player(2, new Vec2(20.1f, 10.5f));

        Assert.True(
            CombatFormulas.IsWithinRange(attacker.State.Pos, victim.State.Pos, CombatConstants.MeleeRangeTiles),
            "el test no vale si los dos no están en alcance: lo que tiene que rechazar es la zona, no la distancia");

        Assert.Equal(ResultCode.SafeZone, Validate(attacker, victim, map));
    }

    /// <summary>
    /// Un punto fuera de toda región declarada no es <c>pvp</c>: falla cerrado (D3).
    /// </summary>
    [Fact]
    public void EnRegionSinFlags_NoSePuedeAtacar()
    {
        var map = TestWorld.Map(SafeSize, SafeSize);
        var a = Player(1, new Vec2(10.5f, 10.5f));
        var b = Player(2, new Vec2(11.0f, 10.5f));

        Assert.Equal(ResultCode.SafeZone, Validate(a, b, map));
    }

    /// <summary>Contra monstruos no se exige <c>pvp</c>: se puede cazar en cualquier sitio.</summary>
    [Fact]
    public void ContraUnMonstruo_LaZonaSeguraNoImpideNada()
    {
        var map = Map();
        var player = Player(1, new Vec2(10.5f, 10.5f));
        var monster = Monster(2, new Vec2(11.0f, 10.5f));

        Assert.Equal(ResultCode.Ok, Validate(player, monster, map, pvp: false));
    }

    // ── Objetivo ─────────────────────────────────────────────────────────

    [Fact]
    public void ATiMismo_CannotAttackTarget()
    {
        var map = Map();
        var a = Player(1, new Vec2(25.5f, 10.5f));

        Assert.Equal(ResultCode.CannotAttackTarget, Validate(a, a, map));
    }

    [Fact]
    public void AUnMuerto_TargetDead()
    {
        var map = Map();
        var a = Player(1, new Vec2(25.5f, 10.5f));
        var b = Player(2, new Vec2(26.0f, 10.5f));
        b.Hp = 0;

        Assert.Equal(ResultCode.TargetDead, Validate(a, b, map));
    }

    /// <summary>Un NPC de tienda no es objetivo: <c>IsAttackable</c> es falso por defecto.</summary>
    [Fact]
    public void AUnNpc_CannotAttackTarget()
    {
        var map = Map();
        var a = Player(1, new Vec2(25.5f, 10.5f));
        var npc = new NpcEntity(2, "shop.test", "Tendero", new Vec2(26.0f, 10.5f), Facing.South);

        Assert.Equal(ResultCode.CannotAttackTarget, Validate(a, npc, map));
    }

    // ── Cooldown, alcance y visión ───────────────────────────────────────

    [Fact]
    public void SinCooldownCumplido_OnCooldown()
    {
        var map = Map();
        var a = Player(1, new Vec2(25.5f, 10.5f));
        var b = Player(2, new Vec2(26.0f, 10.5f));

        Assert.Equal(ResultCode.OnCooldown, Validate(a, b, map, cooldownReady: false));
    }

    [Fact]
    public void FueraDeAlcance_OutOfRange()
    {
        var map = Map();
        var a = Player(1, new Vec2(25.5f, 10.5f));
        var b = Player(2, new Vec2(30.5f, 10.5f));

        Assert.Equal(ResultCode.OutOfRange, Validate(a, b, map));
    }

    /// <summary>
    /// No se pega a través de un muro aunque se esté en alcance. Sin esto se dispara a través de
    /// la muralla del pueblo.
    /// </summary>
    [Fact]
    public void ConUnMuroEnMedio_OutOfRange()
    {
        // Mapa con una columna sólida en x=10, todo él PvP: lo único que cambia entre los dos
        // casos de este test es si el muro está en medio o no.
        var rows = new string[SafeSize];
        for (var y = 0; y < SafeSize; y++)
        {
            rows[y] = y == 0 || y == SafeSize - 1
                ? new string('#', SafeSize)
                : "#" + new string('.', 9) + "#" + new string('.', SafeSize - 12) + "#";
        }

        var map = MapLoader.Compile(
            new MapDefinition
            {
                Key = "map.wall",
                DisplayName = "Muro",
                Width = SafeSize,
                Height = SafeSize,
                Spawn = new MapSpawnDefinition { X = 5.5f, Y = 5.5f, Facing = 2 },
                Collision = rows,
                Regions = [new MapRegionDefinition { Name = "bosque", Rect = [0, 0, SafeSize, SafeSize], Flags = ["pvp"] }],
            },
            "test");

        Assert.True(map.Collision.IsSolid(10, 10), "el test no vale si el muro no está donde se cree");

        // Uno a cada lado de la columna —tiles 9 y 11, con el 10 sólido en medio— y a 1,15 tiles:
        // dentro del alcance de 1,5. La víctima va en el tile 11 y no en el 10 a propósito: un
        // objetivo *dentro* del muro no bloquea por sí mismo (LineOfSight, para no volver
        // invulnerable a quien se quede atrapado en la geometría), así que ponerlo ahí probaría
        // otra cosa.
        var attacker = Player(1, new Vec2(9.9f, 10.5f));
        var blocked = Player(2, new Vec2(11.05f, 10.5f));
        Assert.True(CombatFormulas.IsWithinRange(attacker.State.Pos, blocked.State.Pos, CombatConstants.MeleeRangeTiles));
        Assert.Equal(ResultCode.OutOfRange, Validate(attacker, blocked, map));

        // Control: misma distancia, sin muro entre medias → sí vale.
        var clear = Player(3, new Vec2(8.75f, 10.5f));
        Assert.True(CombatFormulas.IsWithinRange(attacker.State.Pos, clear.State.Pos, CombatConstants.MeleeRangeTiles));
        Assert.Equal(ResultCode.Ok, Validate(attacker, clear, map));
    }

    // ── Aplicación del daño ──────────────────────────────────────────────

    [Fact]
    public void ApplyHit_RestaVidaYNuncaBajaDeCero()
    {
        var attacker = Player(1, new Vec2(25.5f, 10.5f), attack: 10_000);
        var target = Player(2, new Vec2(26.0f, 10.5f));

        var hit = CombatSystem.ApplyHit(attacker, target, new DeterministicRng(7));

        Assert.True(hit.Damage > 0);
        Assert.Equal(0, target.Hp);
        Assert.False(target.IsAlive);
    }

    [Fact]
    public void ApplyHit_ConLaMismaSemilla_EsReproducible()
    {
        var a1 = Player(1, new Vec2(25.5f, 10.5f));
        var t1 = Player(2, new Vec2(26.0f, 10.5f));
        var a2 = Player(1, new Vec2(25.5f, 10.5f));
        var t2 = Player(2, new Vec2(26.0f, 10.5f));

        var first = CombatSystem.ApplyHit(a1, t1, new DeterministicRng(99));
        var second = CombatSystem.ApplyHit(a2, t2, new DeterministicRng(99));

        Assert.Equal(first, second);
        Assert.Equal(t1.Hp, t2.Hp);
    }
}

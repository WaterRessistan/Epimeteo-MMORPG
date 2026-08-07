using Epimeteo.Server.Combat;
using Epimeteo.Server.Content;
using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Subir de nivel y gastar puntos de stat (FASE-10 §2 D2, D4), sin red ni BD.</summary>
public sealed class LevelingSystemTests
{
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());
    private static readonly ClassCatalog Classes = new(ContentPaths.ResolveContentRoot());

    private static PlayerEntity Warrior(int level = 1, long xp = 0) => new(
        1, new FakeWorldPeer(1), characterId: 1, "class.warrior", "J",
        MoveState.AtRest(new Vec2(5, 5), Facing.South), nowMs: 0, items: [])
    {
        Level = level,
        Xp = xp,
        Hp = 1,
        Mp = 1,
    };

    // ── GrantXp ──────────────────────────────────────────────────────────

    [Fact]
    public void XpQueNoLlegaAlUmbral_NoSubeDeNivel()
    {
        var player = Warrior();

        var result = LevelingSystem.GrantXp(player, 50, Classes, Items);

        Assert.False(result.LeveledUp);
        Assert.Equal(1, player.Level);
        Assert.Equal(50, player.Xp);
    }

    [Fact]
    public void XpExacta_SubeUnNivelYDejaLaXpSobranteEnCero()
    {
        var player = Warrior();

        var result = LevelingSystem.GrantXp(player, 100, Classes, Items);

        Assert.True(result.LeveledUp);
        Assert.Equal(1, result.LevelsGained);
        Assert.Equal(2, player.Level);
        Assert.Equal(0, player.Xp);
    }

    [Fact]
    public void XpDeSobra_DejaElRestoAcumuladoParaElSiguienteNivel()
    {
        var player = Warrior();

        LevelingSystem.GrantXp(player, 130, Classes, Items);

        Assert.Equal(2, player.Level);
        Assert.Equal(30, player.Xp);
    }

    /// <summary>Una sola concesión enorme cruza más de un nivel: el bucle no se para en el primero.</summary>
    [Fact]
    public void UnaConcesionEnorme_CruzaVariosNiveles()
    {
        var player = Warrior();

        // 1→2 cuesta 100, 2→3 cuesta 200: 100+200+50 = 350 sube dos niveles y deja 50 de sobra.
        var result = LevelingSystem.GrantXp(player, 350, Classes, Items);

        Assert.Equal(2, result.LevelsGained);
        Assert.Equal(3, player.Level);
        Assert.Equal(50, player.Xp);
    }

    [Fact]
    public void CadaNivel_ConcedeLosPuntosDeStatQueToca()
    {
        var player = Warrior();

        var result = LevelingSystem.GrantXp(player, 350, Classes, Items); // dos niveles

        Assert.Equal(2 * ProgressionConstants.StatPointsPerLevel, result.StatPointsGained);
        Assert.Equal(2 * ProgressionConstants.StatPointsPerLevel, player.StatPoints);
    }

    [Fact]
    public void SubirDeNivel_SubeElHpMaximoYCuraDelTodo()
    {
        var player = Warrior();
        player.Hp = 1;
        player.Mp = 1;

        LevelingSystem.GrantXp(player, 100, Classes, Items);

        Assert.True(Classes.TryGet("class.warrior", out var warrior));
        var hpMaxEsperado = warrior!.BaseHp + warrior.HpPerLevel; // nivel 2
        Assert.Equal(hpMaxEsperado, player.HpMax);
        Assert.Equal(player.HpMax, player.Hp);
        Assert.Equal(player.MpMax, player.Mp);
    }

    [Fact]
    public void XpNoPositiva_NoHaceNada()
    {
        var player = Warrior();

        var result = LevelingSystem.GrantXp(player, 0, Classes, Items);

        Assert.False(result.LeveledUp);
        Assert.Equal(0, player.Xp);
    }

    // ── TryAllocateStatPoint ─────────────────────────────────────────────

    [Fact]
    public void AllocateStatPoint_SinPuntos_Falla()
    {
        var player = Warrior();
        player.StatPoints = 0;

        var code = LevelingSystem.TryAllocateStatPoint(player, StatKind.Str);

        Assert.Equal(ResultCode.NoStatPointsAvailable, code);
    }

    [Theory]
    [InlineData(StatKind.Str)]
    [InlineData(StatKind.Int)]
    [InlineData(StatKind.Vit)]
    [InlineData(StatKind.Dex)]
    public void AllocateStatPoint_ConPuntos_SubeElStatYBajaLosPuntos(StatKind stat)
    {
        var player = Warrior();
        player.StatPoints = 3;
        player.Str = 10;
        player.IntStat = 10;
        player.Vit = 10;
        player.Dex = 10;

        var code = LevelingSystem.TryAllocateStatPoint(player, stat);

        Assert.Equal(ResultCode.Ok, code);
        Assert.Equal(2, player.StatPoints);

        var subido = stat switch
        {
            StatKind.Str => player.Str,
            StatKind.Int => player.IntStat,
            StatKind.Vit => player.Vit,
            StatKind.Dex => player.Dex,
            _ => throw new ArgumentOutOfRangeException(nameof(stat)),
        };
        Assert.Equal(11, subido);
    }
}

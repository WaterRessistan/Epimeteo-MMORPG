using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>
/// El núcleo puro de combate de la Fase 9: generador determinista, fórmulas de daño y línea de
/// visión. Sin servidor, sin red y sin BD — es justo lo que obliga CLAUDE.md §4 para
/// <c>Shared/Simulation</c>.
/// </summary>
public sealed class DeterministicRngTests
{
    [Fact]
    public void MismaSemilla_DaLaMismaSecuencia()
    {
        var a = new DeterministicRng(12345);
        var b = new DeterministicRng(12345);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void SemillasDistintas_DivergenEnseguida()
    {
        var a = new DeterministicRng(1);
        var b = new DeterministicRng(2);

        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    /// <summary>La semilla 0 dejaría clavado un xorshift; el constructor la sustituye.</summary>
    [Fact]
    public void SemillaCero_NoSeQuedaClavada()
    {
        var rng = new DeterministicRng(0);

        Assert.NotEqual(0UL, rng.NextUInt64());
        Assert.NotEqual(0UL, rng.NextUInt64());
    }

    [Fact]
    public void NextInt_SeQuedaDentroDelRango()
    {
        var rng = new DeterministicRng(99);

        for (var i = 0; i < 1000; i++)
        {
            var value = rng.NextInt(-15, 16);
            Assert.InRange(value, -15, 15);
        }
    }

    [Fact]
    public void NextDouble_SeQuedaEnCeroUno()
    {
        var rng = new DeterministicRng(7);

        for (var i = 0; i < 1000; i++)
        {
            var value = rng.NextDouble();
            Assert.True(value is >= 0 and < 1);
        }
    }

    [Fact]
    public void NextChance_ConCeroYUno_EsSiempreFalsoYSiempreCierto()
    {
        var rng = new DeterministicRng(4);

        for (var i = 0; i < 50; i++)
        {
            Assert.False(rng.NextChance(0));
            Assert.True(rng.NextChance(1));
        }
    }
}

public sealed class CombatFormulasTests
{
    [Fact]
    public void BaseDamage_RestaLaMitadDeLaDefensa()
    {
        var attacker = new CombatantStats(Attack: 20, Defense: 0, Dex: 0);
        var defender = new CombatantStats(Attack: 0, Defense: 10, Dex: 0);

        Assert.Equal(15, CombatFormulas.BaseDamage(attacker, defender));
    }

    /// <summary>Un tanque encaja poco, pero encaja: el daño nunca es 0 (FASE-09 §2 D5).</summary>
    [Fact]
    public void BaseDamage_ConDefensaEnorme_NuncaBajaDeUno()
    {
        var attacker = new CombatantStats(Attack: 1, Defense: 0, Dex: 0);
        var defender = new CombatantStats(Attack: 0, Defense: 10_000, Dex: 0);

        Assert.Equal(1, CombatFormulas.BaseDamage(attacker, defender));
    }

    /// <summary>
    /// Con semilla fija el daño es un número exacto, no un rango: es lo que compra tener el RNG
    /// determinista (D4). Si alguien retoca la fórmula, este test lo dice.
    /// </summary>
    [Fact]
    public void Hit_ConSemillaFija_EsReproducible()
    {
        var attacker = new CombatantStats(Attack: 20, Defense: 0, Dex: 0);
        var defender = new CombatantStats(Attack: 0, Defense: 10, Dex: 0);

        var first = CombatFormulas.Hit(attacker, defender, new DeterministicRng(2024));
        var second = CombatFormulas.Hit(attacker, defender, new DeterministicRng(2024));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Hit_SinDestreza_NuncaEsCritico()
    {
        var attacker = new CombatantStats(Attack: 20, Defense: 0, Dex: 0);
        var defender = new CombatantStats(Attack: 0, Defense: 0, Dex: 0);
        var rng = new DeterministicRng(31337);

        for (var i = 0; i < 200; i++)
        {
            Assert.False(CombatFormulas.Hit(attacker, defender, rng).Critical);
        }
    }

    [Fact]
    public void Hit_SeQuedaDentroDeLaDispersionEsperada()
    {
        var attacker = new CombatantStats(Attack: 100, Defense: 0, Dex: 0);
        var defender = new CombatantStats(Attack: 0, Defense: 0, Dex: 0);
        var rng = new DeterministicRng(5);

        for (var i = 0; i < 500; i++)
        {
            // Sin crítico posible (Dex 0): 100 de base ±15 %.
            Assert.InRange(CombatFormulas.Hit(attacker, defender, rng).Damage, 85, 115);
        }
    }

    [Fact]
    public void CriticalChance_TopeAlCincuentaPorCiento()
    {
        Assert.Equal(0.5, CombatFormulas.CriticalChance(new CombatantStats(0, 0, Dex: 10_000)));
    }

    [Fact]
    public void IsWithinRange_MideDeCentroACentro()
    {
        Assert.True(CombatFormulas.IsWithinRange(new Vec2(10, 10), new Vec2(11, 10), 1.5f));
        Assert.False(CombatFormulas.IsWithinRange(new Vec2(10, 10), new Vec2(12, 10), 1.5f));
    }
}

public sealed class LineOfSightTests
{
    /// <summary>Mapa abierto de 16×16 con muro perimetral, más los sólidos que pida el test.</summary>
    private static CollisionMap Map(params (int X, int Y)[] walls)
    {
        const int Size = 16;
        var solid = new bool[Size * Size];

        for (var i = 0; i < Size; i++)
        {
            solid[i] = true;
            solid[((Size - 1) * Size) + i] = true;
            solid[i * Size] = true;
            solid[(i * Size) + Size - 1] = true;
        }

        foreach (var (x, y) in walls)
        {
            solid[(y * Size) + x] = true;
        }

        return new CollisionMap(Size, Size, solid);
    }

    [Fact]
    public void SinNadaEnMedio_HayVision()
    {
        Assert.True(LineOfSight.IsClear(Map(), new Vec2(2.5f, 2.5f), new Vec2(10.5f, 2.5f)));
    }

    [Fact]
    public void ConUnMuroEnMedio_NoHayVision()
    {
        var map = Map((6, 2));

        Assert.False(LineOfSight.IsClear(map, new Vec2(2.5f, 2.5f), new Vec2(10.5f, 2.5f)));
    }

    [Fact]
    public void EnDiagonal_ConMuroEnLaTrayectoria_NoHayVision()
    {
        var map = Map((5, 5));

        Assert.False(LineOfSight.IsClear(map, new Vec2(2.5f, 2.5f), new Vec2(8.5f, 8.5f)));
    }

    [Fact]
    public void EnElMismoTile_SiempreHayVision()
    {
        Assert.True(LineOfSight.IsClear(Map(), new Vec2(4.2f, 4.2f), new Vec2(4.8f, 4.8f)));
    }

    /// <summary>
    /// Ni el tile de origen ni el de destino bloquean: si el mapa cambió y alguien quedó dentro de
    /// un muro, sigue siendo atacable en vez de volverse invulnerable.
    /// </summary>
    [Fact]
    public void ElTileDeDestinoSolido_NoBloqueaPorSiMismo()
    {
        var map = Map((8, 2));

        Assert.True(LineOfSight.IsClear(map, new Vec2(6.5f, 2.5f), new Vec2(8.5f, 2.5f)));
    }

    [Fact]
    public void ElTileDeOrigenSolido_NoBloqueaPorSiMismo()
    {
        var map = Map((6, 2));

        Assert.True(LineOfSight.IsClear(map, new Vec2(6.5f, 2.5f), new Vec2(8.5f, 2.5f)));
    }

    [Fact]
    public void EsSimetrica()
    {
        var map = Map((6, 2));
        var a = new Vec2(2.5f, 2.5f);
        var b = new Vec2(10.5f, 2.5f);

        Assert.Equal(LineOfSight.IsClear(map, a, b), LineOfSight.IsClear(map, b, a));
    }
}

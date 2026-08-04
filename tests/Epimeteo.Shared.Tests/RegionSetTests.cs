using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class RegionSetTests
{
    private static RegionSet Build() => new(
    [
        new MapRegion("plaza", 4, 4, 4, 4, ZoneFlags.Safe | ZoneFlags.NoMonsters),
        new MapRegion("campo", 0, 0, 16, 16, ZoneFlags.Pvp | ZoneFlags.Outdoor),
    ]);

    [Fact]
    public void UnPuntoDentroDeUnaRegion_DevuelveSusFlags()
    {
        var region = Build().Resolve(new Vec2(5.5f, 5.5f));

        Assert.Equal("plaza", region.Name);
        Assert.True(region.Flags.HasFlag(ZoneFlags.Safe));
    }

    /// <summary>
    /// La plaza está declarada dentro del campo. Gana la primera del array: así se puede declarar
    /// un claro seguro dentro de un bosque hostil sin recortar rectángulos a mano.
    /// </summary>
    [Fact]
    public void SiDosRegionesSeSolapan_GanaLaPrimeraDeclarada()
    {
        var set = Build();

        Assert.Equal("plaza", set.Resolve(new Vec2(6f, 6f)).Name);
        Assert.Equal("campo", set.Resolve(new Vec2(1f, 1f)).Name);
    }

    [Fact]
    public void UnPuntoSinRegion_NoTieneFlags()
    {
        var region = Build().Resolve(new Vec2(20f, 20f));

        Assert.Equal(string.Empty, region.Name);
        Assert.Equal(ZoneFlags.None, region.Flags);
    }

    [Fact]
    public void ElBordeDerechoYInferior_QuedanFuera()
    {
        var set = new RegionSet([new MapRegion("r", 2, 2, 2, 2, ZoneFlags.Safe)]);

        Assert.Equal("r", set.Resolve(new Vec2(2f, 2f)).Name);
        Assert.Equal("r", set.Resolve(new Vec2(3.9f, 3.9f)).Name);
        Assert.Equal(string.Empty, set.Resolve(new Vec2(4f, 3f)).Name);
        Assert.Equal(string.Empty, set.Resolve(new Vec2(3f, 4f)).Name);
    }

    [Fact]
    public void UnConjuntoVacio_NoTieneFlagsEnNingunSitio()
    {
        Assert.Equal(ZoneFlags.None, RegionSet.Empty.Resolve(new Vec2(1f, 1f)).Flags);
    }
}

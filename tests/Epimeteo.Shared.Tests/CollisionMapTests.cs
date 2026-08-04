using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class CollisionMapTests
{
    [Fact]
    public void FueraDelMapa_EsSolido()
    {
        var map = TestMaps.OpenRoom();

        Assert.True(map.IsSolid(-1, 4));
        Assert.True(map.IsSolid(4, -1));
        Assert.True(map.IsSolid(map.Width, 4));
        Assert.True(map.IsSolid(4, map.Height));
        Assert.False(map.IsSolid(4, 4));
    }

    [Fact]
    public void LaRejilla_SeIndexaEnOrdenFilaMayor()
    {
        var map = TestMaps.From(
            "#.",
            ".#");

        Assert.True(map.IsSolid(0, 0));
        Assert.False(map.IsSolid(1, 0));
        Assert.False(map.IsSolid(0, 1));
        Assert.True(map.IsSolid(1, 1));
    }

    [Fact]
    public void UnaRejillaDeTamanoEquivocado_NoSeConstruye()
    {
        Assert.Throws<ArgumentException>(() => new CollisionMap(4, 4, new bool[15]));
    }

    /// <summary>
    /// Los bordes son abiertos: una caja que termina exactamente en el borde del tile sólido no
    /// lo toca. Sin esta regla, un pasillo del ancho justo sería intransitable.
    /// </summary>
    [Fact]
    public void UnaCajaQueTerminaJustoEnElBorde_NoTocaElTileSolido()
    {
        var map = TestMaps.From(
            "..#",
            "..#",
            "..#");

        Assert.False(map.IsBlocked(new Vec2(1.625f, 1f), 0.375f, 0.25f));
        Assert.True(map.IsBlocked(new Vec2(1.63f, 1f), 0.375f, 0.25f));
    }

    [Fact]
    public void UnaCajaQueSaleDelMapa_EstaBloqueada()
    {
        var map = TestMaps.From(
            "...",
            "...",
            "...");

        Assert.True(map.IsBlocked(new Vec2(0.1f, 1f), 0.375f, 0.25f));
        Assert.True(map.IsBlocked(new Vec2(1f, 2.9f), 0.375f, 0.25f));
        Assert.False(map.IsBlocked(new Vec2(1.5f, 1.5f), 0.375f, 0.25f));
    }
}

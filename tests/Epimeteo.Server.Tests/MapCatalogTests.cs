using Epimeteo.Server.Content;
using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Sin Postgres: valida el contenido real de <c>content/maps/</c>. Si alguien edita el mapa a mano
/// y lo deja incoherente, salta aquí antes que en producción.
/// </summary>
public sealed class MapCatalogTests
{
    private static MapCatalog LoadCatalog() => new(ContentPaths.ResolveContentRoot());

    [Fact]
    public void Constructor_CargaElMapaDelPueblo()
    {
        var catalog = LoadCatalog();

        Assert.True(catalog.TryGet("map.village", out var map));
        Assert.Equal(96, map!.Width);
        Assert.Equal(96, map.Height);
    }

    [Fact]
    public void ElSpawn_EstaLibreYDentroDelMapa()
    {
        Assert.True(LoadCatalog().TryGet("map.village", out var map));

        Assert.False(map!.Collision.IsBlocked(
            map.Spawn, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight));
    }

    [Fact]
    public void ElMapa_TieneMuroPerimetral()
    {
        Assert.True(LoadCatalog().TryGet("map.village", out var map));

        for (var x = 0; x < map!.Width; x++)
        {
            Assert.True(map.Collision.IsSolid(x, 0));
            Assert.True(map.Collision.IsSolid(x, map.Height - 1));
        }

        for (var y = 0; y < map.Height; y++)
        {
            Assert.True(map.Collision.IsSolid(0, y));
            Assert.True(map.Collision.IsSolid(map.Width - 1, y));
        }
    }

    /// <summary>
    /// El pueblo es zona segura y el campo del norte tiene PvP: es lo que hace observable el
    /// <c>ZoneFlagsUpdate</c> al cruzar la muralla.
    /// </summary>
    [Fact]
    public void ElPueblo_EsSeguroYElCampoEsPvp()
    {
        Assert.True(LoadCatalog().TryGet("map.village", out var map));

        Assert.True(map!.Regions.Resolve(map.Spawn).Flags.HasFlag(ZoneFlags.Safe));
        Assert.True(map.Regions.Resolve(new Vec2(48.5f, 20f)).Flags.HasFlag(ZoneFlags.Pvp));
    }

    [Fact]
    public void TryGet_ConClaveDesconocida_DevuelveFalse()
    {
        Assert.False(LoadCatalog().TryGet("map.nope", out var map));
        Assert.Null(map);
    }
}

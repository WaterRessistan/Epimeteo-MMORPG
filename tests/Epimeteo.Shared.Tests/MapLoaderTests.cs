using Epimeteo.Shared.Data;
using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class MapLoaderTests
{
    private static readonly string[] ValidRows = ["####", "#..#", "#..#", "####"];
    private const string ValidRegions = """{ "name": "centro", "rect": [1, 1, 2, 2], "flags": ["safe", "indoor"] }""";

    /// <summary>
    /// Compone el JSON pieza a pieza para que cada test rompa exactamente una cosa, sin depender
    /// de reemplazos de texto sobre un literal.
    /// </summary>
    private static string MapJson(
        string[]? rows = null,
        int? width = null,
        int? height = null,
        string spawn = """{ "x": 1.5, "y": 1.5, "facing": 2 }""",
        string regions = ValidRegions)
    {
        rows ??= ValidRows;
        var collision = string.Join(", ", rows.Select(r => $"\"{r}\""));

        return $$"""
        {
          "key": "map.test",
          "displayName": "Prueba",
          "width": {{width ?? rows[0].Length}},
          "height": {{height ?? rows.Length}},
          "spawn": {{spawn}},
          "collision": [{{collision}}],
          "regions": [{{regions}}]
        }
        """;
    }

    [Fact]
    public void UnMapaValido_SeCargaEntero()
    {
        var map = MapLoader.Parse(MapJson(), "test");

        Assert.Equal("map.test", map.Key);
        Assert.Equal("Prueba", map.DisplayName);
        Assert.Equal(4, map.Width);
        Assert.True(map.Collision.IsSolid(0, 0));
        Assert.False(map.Collision.IsSolid(1, 1));
        Assert.Equal(new Vec2(1.5f, 1.5f), map.Spawn);
        Assert.Equal(Facing.South, map.SpawnFacing);
        Assert.Equal(ZoneFlags.Safe | ZoneFlags.Indoor, map.Regions.Resolve(new Vec2(1.5f, 1.5f)).Flags);
    }

    [Fact]
    public void UnaFilaConLongitudEquivocada_NoCarga()
    {
        var json = MapJson(["####", "#.#", "#..#", "####"], width: 4);

        var ex = Assert.Throws<InvalidDataException>(() => MapLoader.Parse(json, "roto.json"));
        Assert.Contains("la fila 1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnNumeroDeFilasEquivocado_NoCarga()
    {
        var json = MapJson(height: 5);

        var ex = Assert.Throws<InvalidDataException>(() => MapLoader.Parse(json, "roto.json"));
        Assert.Contains("filas", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnCaracterDesconocido_NoCarga()
    {
        var json = MapJson(["####", "#.x#", "#..#", "####"]);

        var ex = Assert.Throws<InvalidDataException>(() => MapLoader.Parse(json, "roto.json"));
        Assert.Contains("desconocido", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnaRegionFueraDelMapa_NoCarga()
    {
        var json = MapJson(regions: """{ "name": "centro", "rect": [1, 1, 9, 2], "flags": [] }""");

        var ex = Assert.Throws<InvalidDataException>(() => MapLoader.Parse(json, "roto.json"));
        Assert.Contains("se sale del mapa", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnFlagDesconocido_NoCarga()
    {
        var json = MapJson(regions: """{ "name": "centro", "rect": [1, 1, 2, 2], "flags": ["segura"] }""");

        var ex = Assert.Throws<InvalidDataException>(() => MapLoader.Parse(json, "roto.json"));
        Assert.Contains("segura", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnaRegionSeguraYPvpALaVez_NoCarga()
    {
        var json = MapJson(regions: """{ "name": "centro", "rect": [1, 1, 2, 2], "flags": ["safe", "pvp"] }""");

        var ex = Assert.Throws<InvalidDataException>(() => MapLoader.Parse(json, "roto.json"));
        Assert.Contains("a la vez", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnSpawnDentroDeUnMuro_NoCarga()
    {
        var json = MapJson(spawn: """{ "x": 0.5, "y": 0.5, "facing": 2 }""");

        var ex = Assert.Throws<InvalidDataException>(() => MapLoader.Parse(json, "roto.json"));
        Assert.Contains("spawn", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnJsonInvalido_DaUnErrorConElFichero()
    {
        var ex = Assert.Throws<InvalidDataException>(() => MapLoader.Parse("{ esto no es json", "roto.json"));

        Assert.Contains("roto.json", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// El hash es la red de seguridad contra un cliente con contenido viejo (FASE-04 §2 D4): tiene
    /// que reaccionar a un tile movido y no reaccionar a un cambio de formato del fichero.
    /// </summary>
    [Fact]
    public void ElHash_CambiaSiCambiaUnTile()
    {
        var original = MapLoader.Parse(MapJson(), "test");
        var modified = MapLoader.Parse(MapJson(["####", "#..#", "##.#", "####"]), "test");

        Assert.NotEqual(original.Hash, modified.Hash);
    }

    [Fact]
    public void ElHash_CambiaSiCambianLasRegiones()
    {
        var original = MapLoader.Parse(MapJson(), "test");
        var modified = MapLoader.Parse(
            MapJson(regions: """{ "name": "centro", "rect": [1, 1, 2, 2], "flags": ["pvp"] }"""), "test");

        Assert.NotEqual(original.Hash, modified.Hash);
    }

    [Fact]
    public void ElHash_NoCambiaAlReordenarElJson()
    {
        var reordered = """
        {
          "displayName": "Prueba",
          "height": 4,
          "width": 4,
          "collision": ["####", "#..#", "#..#", "####"],
          "key": "map.test",
          "regions": [{ "flags": ["safe", "indoor"], "rect": [1, 1, 2, 2], "name": "centro" }],
          "spawn": { "facing": 2, "y": 1.5, "x": 1.5 }
        }
        """;

        Assert.Equal(MapLoader.Parse(MapJson(), "test").Hash, MapLoader.Parse(reordered, "test").Hash);
    }
}

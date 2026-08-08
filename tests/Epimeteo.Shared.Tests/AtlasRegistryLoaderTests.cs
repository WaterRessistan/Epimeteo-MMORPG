using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>Validación pura de <c>client/assets/atlas_registry.json</c> (FASE-12 §2 D1), sin tocar disco.</summary>
public sealed class AtlasRegistryLoaderTests
{
    [Fact]
    public void ArrayVacio_DevuelveUnRegistroVacio()
    {
        var byKey = AtlasRegistryLoader.Parse("[]", "test");
        Assert.Empty(byKey);
    }

    [Fact]
    public void UnaEntradaValida_SeParseaEntera()
    {
        const string json = """
            [
              { "key": "item.iron_sword", "atlasPath": "res://assets/sprites/items/weapons.png", "x": 0, "y": 16, "width": 16, "height": 16 }
            ]
            """;

        var byKey = AtlasRegistryLoader.Parse(json, "test");

        var region = Assert.Single(byKey).Value;
        Assert.Equal("item.iron_sword", region.Key);
        Assert.Equal("res://assets/sprites/items/weapons.png", region.AtlasPath);
        Assert.Equal(0, region.X);
        Assert.Equal(16, region.Y);
        Assert.Equal(16, region.Width);
        Assert.Equal(16, region.Height);
    }

    [Fact]
    public void ClaveDuplicada_LanzaExcepcion()
    {
        const string json = """
            [
              { "key": "a", "atlasPath": "res://x.png", "x": 0, "y": 0, "width": 16, "height": 16 },
              { "key": "a", "atlasPath": "res://y.png", "x": 0, "y": 0, "width": 16, "height": 16 }
            ]
            """;

        Assert.Throws<InvalidOperationException>(() => AtlasRegistryLoader.Parse(json, "test"));
    }

    [Theory]
    [InlineData("""[{ "atlasPath": "res://x.png", "x": 0, "y": 0, "width": 16, "height": 16 }]""")]
    [InlineData("""[{ "key": "a", "x": 0, "y": 0, "width": 16, "height": 16 }]""")]
    [InlineData("""[{ "key": "a", "atlasPath": "res://x.png", "x": 0, "y": 0, "width": 0, "height": 16 }]""")]
    [InlineData("""[{ "key": "a", "atlasPath": "res://x.png", "x": -1, "y": 0, "width": 16, "height": 16 }]""")]
    public void EntradaInvalida_LanzaExcepcion(string json) =>
        Assert.Throws<InvalidOperationException>(() => AtlasRegistryLoader.Parse(json, "test"));

    [Fact]
    public void JsonQueNoEsUnArray_LanzaExcepcion() =>
        Assert.Throws<InvalidOperationException>(() => AtlasRegistryLoader.Parse("{}", "test"));
}

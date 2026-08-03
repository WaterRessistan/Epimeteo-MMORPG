using Epimeteo.Server.Content;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Sin Postgres: sólo lee <c>content/classes/*.json</c> del disco. Corre siempre.</summary>
public sealed class ClassCatalogTests
{
    private static ClassCatalog LoadCatalog() => new(ContentPaths.ResolveContentRoot());

    [Fact]
    public void Constructor_CargaLasTresClases()
    {
        var catalog = LoadCatalog();

        Assert.True(catalog.TryGet("class.warrior", out _));
        Assert.True(catalog.TryGet("class.mage", out _));
        Assert.True(catalog.TryGet("class.hybrid", out _));
        Assert.Equal(3, catalog.All.Count);
    }

    [Fact]
    public void TryGet_ConClaveDesconocida_DevuelveFalse()
    {
        var catalog = LoadCatalog();

        Assert.False(catalog.TryGet("class.nope", out var definition));
        Assert.Null(definition);
    }

    [Theory]
    [InlineData("class.warrior")]
    [InlineData("class.mage")]
    [InlineData("class.hybrid")]
    public void TryGet_StatsBaseSonPositivos(string classKey)
    {
        var catalog = LoadCatalog();
        Assert.True(catalog.TryGet(classKey, out var definition));

        Assert.True(definition!.BaseStr > 0);
        Assert.True(definition.BaseInt > 0);
        Assert.True(definition.BaseVit > 0);
        Assert.True(definition.BaseDex > 0);
        Assert.True(definition.BaseHp > 0);
        Assert.True(definition.BaseMp > 0);
    }
}

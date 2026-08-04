using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class AoiGridTests
{
    [Fact]
    public void ElNumeroDeCeldas_RedondeaHaciaArriba()
    {
        Assert.Equal(6, new AoiGrid(96, 96).CellsX);
        Assert.Equal(2, new AoiGrid(17, 17).CellsX);
        Assert.Equal(1, new AoiGrid(16, 16).CellsX);
    }

    [Fact]
    public void LaCelda_CambiaAlCruzarElMultiploDeDieciseis()
    {
        var grid = new AoiGrid(96, 96);

        Assert.Equal(0, grid.CellOf(new Vec2(15.99f, 0.5f)));
        Assert.Equal(1, grid.CellOf(new Vec2(16f, 0.5f)));
        Assert.Equal(grid.CellsX, grid.CellOf(new Vec2(0.5f, 16f)));
    }

    [Fact]
    public void UnaPosicionFueraDelMapa_SeRecortaAlBorde()
    {
        var grid = new AoiGrid(96, 96);

        Assert.Equal(0, grid.CellOf(new Vec2(-40f, -40f)));
        Assert.Equal(grid.CellCount - 1, grid.CellOf(new Vec2(500f, 500f)));
    }

    [Fact]
    public void EnElInterior_LaVecindadSonNueveCeldas()
    {
        var grid = new AoiGrid(96, 96);
        Span<int> cells = stackalloc int[AoiGrid.MaxNeighborhood];

        var count = grid.GetNeighborhood(grid.CellOf(new Vec2(40f, 40f)), cells);

        Assert.Equal(9, count);
        Assert.Equal(9, cells[..count].ToArray().Distinct().Count());
    }

    [Fact]
    public void EnUnaEsquina_LaVecindadSonCuatroCeldas()
    {
        var grid = new AoiGrid(96, 96);
        Span<int> cells = stackalloc int[AoiGrid.MaxNeighborhood];

        var count = grid.GetNeighborhood(0, cells);

        Assert.Equal(4, count);
        Assert.Contains(0, cells[..count].ToArray());
        Assert.Contains(grid.CellsX + 1, cells[..count].ToArray());
    }

    [Fact]
    public void EnUnBorde_LaVecindadSonSeisCeldas()
    {
        var grid = new AoiGrid(96, 96);
        Span<int> cells = stackalloc int[AoiGrid.MaxNeighborhood];

        var count = grid.GetNeighborhood(grid.CellOf(new Vec2(40f, 0.5f)), cells);

        Assert.Equal(6, count);
    }

    [Fact]
    public void UnDestinoDemasiadoPequeno_EsUnError()
    {
        var grid = new AoiGrid(96, 96);

        Assert.Throws<ArgumentException>(() =>
        {
            var small = new int[4];
            grid.GetNeighborhood(0, small);
        });
    }
}

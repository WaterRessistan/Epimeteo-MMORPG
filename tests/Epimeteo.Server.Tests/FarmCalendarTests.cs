using Epimeteo.Server.Content;
using Epimeteo.Server.Farm;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Puro, sin BD ni tick: aritmética de días de granja y estación (FASE-08 §2 D1, D8, D12).</summary>
public sealed class FarmCalendarTests
{
    [Fact]
    public void DayIndex_CambiaExactamenteEnLaFronteraDeLas5Utc()
    {
        var justBefore = new DateTimeOffset(2026, 8, 5, 4, 59, 59, TimeSpan.Zero);
        var atBoundary = new DateTimeOffset(2026, 8, 5, 5, 0, 0, TimeSpan.Zero);

        Assert.Equal(FarmCalendar.DayIndex(justBefore) + 1, FarmCalendar.DayIndex(atBoundary));
    }

    [Fact]
    public void DayIndex_SubeUnoCadaVeinticuatroHorasExactas()
    {
        var start = new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero);
        var startIndex = FarmCalendar.DayIndex(start);

        for (var i = 1; i <= 5; i++)
        {
            Assert.Equal(startIndex + i, FarmCalendar.DayIndex(start.AddDays(i)));
        }
    }

    [Fact]
    public void BoundaryOf_EsElInversoDeDayIndex()
    {
        var instant = new DateTimeOffset(2026, 5, 20, 5, 0, 0, TimeSpan.Zero);
        var index = FarmCalendar.DayIndex(instant);

        Assert.Equal(instant, FarmCalendar.BoundaryOf(index));
    }

    [Theory]
    [InlineData(1, FarmSeason.Spring)]
    [InlineData(91, FarmSeason.Spring)]
    [InlineData(92, FarmSeason.Summer)]
    [InlineData(182, FarmSeason.Summer)]
    [InlineData(183, FarmSeason.Autumn)]
    [InlineData(273, FarmSeason.Autumn)]
    [InlineData(274, FarmSeason.Winter)]
    [InlineData(365, FarmSeason.Winter)]
    public void SeasonOf_CaeEnElTramoQueLeToca(int dayOfYear, FarmSeason expected)
    {
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(dayOfYear - 1);

        Assert.Equal(expected, FarmCalendar.SeasonOf(instant));
    }

    [Fact]
    public void EstimateEta_SinProgresoPendiente_DevuelveAhora()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now, FarmCalendar.EstimateEta(now, growthDays: 3, growthNeeded: 3));
    }

    [Fact]
    public void EstimateEta_ConTresDiasPendientes_CaeTresFronterasDespues()
    {
        var now = new DateTimeOffset(2026, 6, 1, 5, 0, 0, TimeSpan.Zero);

        var eta = FarmCalendar.EstimateEta(now, growthDays: 0, growthNeeded: 3);

        Assert.Equal(FarmCalendar.BoundaryOf(FarmCalendar.DayIndex(now) + 3), eta);
    }
}

using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>La curva de XP de la Fase 10 (§2 D1): pura, sin servidor ni BD.</summary>
public sealed class LevelingFormulasTests
{
    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 200)]
    [InlineData(3, 300)]
    [InlineData(10, 1000)]
    public void XpRequiredForNextLevel_EsLinealYExacta(int level, long expected)
    {
        Assert.Equal(expected, LevelingFormulas.XpRequiredForNextLevel(level));
    }

    [Fact]
    public void XpRequiredForNextLevel_SubeConCadaNivel()
    {
        var previous = LevelingFormulas.XpRequiredForNextLevel(1);

        for (var level = 2; level <= 20; level++)
        {
            var current = LevelingFormulas.XpRequiredForNextLevel(level);
            Assert.True(current > previous, $"nivel {level}: {current} tendría que ser mayor que {previous}");
            previous = current;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void XpRequiredForNextLevel_ConNivelImposible_Lanza(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelingFormulas.XpRequiredForNextLevel(level));
    }
}

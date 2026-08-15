using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>Regeneración pasiva de HP/MP (hallazgo de la sesión fuera de fase: el maná no se recuperaba nunca).</summary>
public sealed class RegenFormulasTests
{
    [Fact]
    public void Regen_YaAlMaximo_SeQuedaIgual()
    {
        Assert.Equal(100, RegenFormulas.Regen(100, 100, RegenFormulas.MpRegenPerSecondFraction, 5));
    }

    [Fact]
    public void Regen_NuncaSuperaElMaximo()
    {
        var result = RegenFormulas.Regen(90, 100, RegenFormulas.MpRegenPerSecondFraction, 1000);

        Assert.Equal(100, result);
    }

    [Fact]
    public void Regen_SubeConElTiempo()
    {
        var afterOneSecond = RegenFormulas.Regen(0, 100, RegenFormulas.MpRegenPerSecondFraction, 1);
        var afterTwoSeconds = RegenFormulas.Regen(0, 100, RegenFormulas.MpRegenPerSecondFraction, 2);

        Assert.True(afterOneSecond > 0);
        Assert.True(afterTwoSeconds > afterOneSecond);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 5)]
    public void Regen_ConMaximoPequeno_SubeAlMenosUnPuntoPorSegundo(int current, int elapsedSeconds)
    {
        // Un guerrero de nivel 1 tiene 20 de maná máximo: el 5% son 0,05 puntos por segundo, que
        // redondeando a la baja nunca subiría de 0. Es justo el caso que dejaba al maná bloqueado
        // para siempre si el redondeo no garantizase un mínimo — el hallazgo real de esta sesión.
        const int smallMax = 20;

        var result = RegenFormulas.Regen(current, smallMax, RegenFormulas.MpRegenPerSecondFraction, elapsedSeconds);

        Assert.True(result >= current + elapsedSeconds);
    }

    [Fact]
    public void Regen_SinTiempoTranscurrido_SeQuedaIgual()
    {
        Assert.Equal(50, RegenFormulas.Regen(50, 100, RegenFormulas.MpRegenPerSecondFraction, 0));
    }

    [Fact]
    public void Regen_ConMaximoCero_SeQuedaIgual()
    {
        Assert.Equal(0, RegenFormulas.Regen(0, 0, RegenFormulas.MpRegenPerSecondFraction, 5));
    }
}

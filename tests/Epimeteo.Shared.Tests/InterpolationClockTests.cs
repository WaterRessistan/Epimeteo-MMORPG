using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>
/// El reloj de interpolación. Corregir el desfase suave en vez de a saltos es lo que hace que las
/// entidades remotas no den tirones cuando los snapshots no llegan a intervalos perfectos, que es
/// siempre.
/// </summary>
public sealed class InterpolationClockTests
{
    /// <summary>Un frame de 50 ms, el mismo paso que la simulación.</summary>
    private const double Frame = SimulationConstants.TickDt;

    [Fact]
    public void AntesDelPrimerSnapshotNoAvanza()
    {
        var clock = new InterpolationClock();

        clock.Advance(1.0);

        Assert.False(clock.IsStarted);
        Assert.Equal(0, clock.RenderTick);
    }

    /// <summary>El primero coloca el reloj en hora: no tiene sentido recuperar desde cero.</summary>
    [Fact]
    public void ElPrimerSnapshotArrancaElRelojEnHora()
    {
        var clock = new InterpolationClock();

        clock.OnSnapshot(1000);

        Assert.True(clock.IsStarted);
        Assert.Equal(1000 - InterpolationClock.DelayTicks, clock.RenderTick);
        Assert.Equal(0, clock.Jumps);
    }

    /// <summary>El retraso es el de la constante compartida: 100 ms = 2 ticks a 20 Hz.</summary>
    [Fact]
    public void ElRetrasoSaleDeLaConstanteCompartida()
    {
        Assert.Equal(2.0, InterpolationClock.DelayTicks);
    }

    [Fact]
    public void AvanzaAlRitmoDelTiempoRealCuandoEstaEnHora()
    {
        var clock = new InterpolationClock();
        clock.OnSnapshot(1000);
        var start = clock.RenderTick;

        // Un segundo de frames, con el objetivo avanzando a la vez: el reloj no tiene que derivar.
        for (var i = 0; i < 20; i++)
        {
            clock.OnSnapshot(1000 + i + 1);
            clock.Advance(Frame);
        }

        Assert.Equal(start + 20, clock.RenderTick, 1);
        Assert.Equal(0, clock.Jumps);
    }

    [Fact]
    public void UnDesfasePequenoSeRecuperaAcelerandoUnDiezPorCiento()
    {
        var clock = new InterpolationClock();
        clock.OnSnapshot(1000);

        // El servidor se adelanta 2 ticks: por debajo del umbral de salto.
        clock.OnSnapshot(1002);
        clock.Advance(Frame);

        Assert.Equal(1.0 + InterpolationClock.Correction, clock.Rate);
        Assert.Equal(0, clock.Jumps);
    }

    [Fact]
    public void SiElRelojVaPorDelanteSeFrena()
    {
        var clock = new InterpolationClock();
        clock.OnSnapshot(1000);

        // El objetivo retrocede 2 ticks (reordenación o jitter): hay que esperar, no saltar atrás.
        clock.OnSnapshot(998);
        clock.Advance(Frame);

        Assert.Equal(1.0 - InterpolationClock.Correction, clock.Rate);
        Assert.Equal(0, clock.Jumps);
    }

    /// <summary>
    /// Un desfase grande sí se salta: arrastrarlo al 10 % tardaría segundos, y durante todos ellos
    /// las entidades remotas se verían a destiempo.
    /// </summary>
    [Fact]
    public void UnDesfaseGrandeSeSaltaDeGolpe()
    {
        var clock = new InterpolationClock();
        clock.OnSnapshot(1000);

        clock.OnSnapshot(1100);
        clock.Advance(Frame);

        Assert.Equal(1100 - InterpolationClock.DelayTicks, clock.RenderTick);
        Assert.Equal(1.0, clock.Rate);
        Assert.Equal(1, clock.Jumps);
    }

    /// <summary>
    /// El caso realista: el servidor sigue ticando y mandando snapshots, y el cliente arranca
    /// 3 ticks por detrás. Tiene que alcanzarlo poco a poco y quedarse pegado, sin un solo salto.
    /// <para>
    /// Ojo con lo que <b>no</b> dice este test: con el objetivo congelado —conexión caída— el reloj
    /// sigue avanzando con el tiempo real y se aleja, que es lo correcto. El reloj mide tiempo,
    /// no persigue un número.
    /// </para>
    /// </summary>
    [Fact]
    public void AlcanzaAlServidorPocoAPocoYSinSaltos()
    {
        var clock = new InterpolationClock();
        var serverTick = 1000L;
        clock.OnSnapshot(serverTick);

        // Se pone 3 ticks por detrás de golpe (un pico de latencia al entrar).
        serverTick += 3;

        // 5 s de servidor real: un snapshot cada 2 ticks, un frame cada tick.
        for (var i = 0; i < 100; i++)
        {
            serverTick++;
            if (i % 2 == 0)
            {
                clock.OnSnapshot(serverTick);
            }

            clock.Advance(Frame);
        }

        // Se queda pegado dentro de la zona muerta (±0,5 ticks = ±25 ms). No se le pide más:
        // afinar por debajo de eso sería microcorregir el ritmo en cada frame para nada.
        //
        // El ritmo sí sigue oscilando entre 1,0 y 0,9, porque el objetivo avanza a saltos de dos
        // ticks (un snapshot cada dos) y el reloj es continuo, así que se adelanta un poco entre
        // snapshot y snapshot. Es exactamente el diseño: un 10 % de variación no se percibe, y es
        // lo que evita el tirón de saltar al objetivo cada vez que llega un paquete.
        Assert.InRange(clock.TargetTick - clock.RenderTick, -0.6, 0.6);
        Assert.Equal(0, clock.Jumps);
    }
}

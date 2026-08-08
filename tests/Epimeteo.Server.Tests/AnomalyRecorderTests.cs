using Epimeteo.Server.Security;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// La escalada del anticheat (FASE-13 §2 D4). El <c>AnomalyRecorder</c> recibe <c>nowMs</c> en vez
/// de mirar el reloj, así que se puede probar una ventana de 60 s sin esperar 60 s — mismo
/// criterio que <c>TokenBucket</c> y <c>DeterministicRng</c>.
/// </summary>
public sealed class AnomalyRecorderTests
{
    [Fact]
    public void PorDebajoDelUmbralDeAviso_SoloCuenta()
    {
        var recorder = new AnomalyRecorder();
        var (warn, _) = AnomalyThresholds.For(AnomalyKind.OutOfRange);

        for (var i = 1; i < warn; i++)
        {
            var outcome = recorder.Record(AnomalyKind.OutOfRange, nowMs: 0);
            Assert.Equal(AnomalyVerdict.Counted, outcome.Verdict);
            Assert.Equal(i, outcome.CountInWindow);
        }
    }

    [Fact]
    public void AlCruzarElUmbralDeAviso_Avisa()
    {
        var recorder = new AnomalyRecorder();
        var (warn, _) = AnomalyThresholds.For(AnomalyKind.OutOfRange);

        AnomalyOutcome outcome = default;
        for (var i = 0; i < warn; i++)
        {
            outcome = recorder.Record(AnomalyKind.OutOfRange, nowMs: 0);
        }

        Assert.Equal(AnomalyVerdict.Warn, outcome.Verdict);
        Assert.Equal(warn, outcome.CountInWindow);
    }

    /// <summary>
    /// Sólo el cruce avisa, no cada anomalía a partir de ahí: si no, pasado el umbral cada
    /// rechazo escribiría una línea de log y una fila de BD, y el propio detector sería el que
    /// inunda.
    /// </summary>
    [Fact]
    public void PasadoElUmbralDeAviso_NoVuelveAAvisarEnLaMismaVentana()
    {
        var recorder = new AnomalyRecorder();
        var (warn, kick) = AnomalyThresholds.For(AnomalyKind.OutOfRange);

        for (var i = 0; i < warn; i++)
        {
            recorder.Record(AnomalyKind.OutOfRange, nowMs: 0);
        }

        for (var i = warn; i < kick - 1; i++)
        {
            Assert.Equal(AnomalyVerdict.Counted, recorder.Record(AnomalyKind.OutOfRange, nowMs: 0).Verdict);
        }
    }

    [Fact]
    public void AlCruzarElUmbralDuro_Desconecta()
    {
        var recorder = new AnomalyRecorder();
        var (_, kick) = AnomalyThresholds.For(AnomalyKind.OutOfRange);

        AnomalyOutcome outcome = default;
        for (var i = 0; i < kick; i++)
        {
            outcome = recorder.Record(AnomalyKind.OutOfRange, nowMs: 0);
        }

        Assert.Equal(AnomalyVerdict.Kick, outcome.Verdict);
        Assert.Equal(kick, outcome.CountInWindow);
    }

    /// <summary>Pasada la ventana, la cuenta arranca de cero: una anomalía de hace una hora no cuenta hoy.</summary>
    [Fact]
    public void PasadaLaVentana_LaCuentaSeReinicia()
    {
        var recorder = new AnomalyRecorder();
        var (warn, _) = AnomalyThresholds.For(AnomalyKind.OutOfRange);

        for (var i = 0; i < warn; i++)
        {
            recorder.Record(AnomalyKind.OutOfRange, nowMs: 0);
        }

        var outcome = recorder.Record(AnomalyKind.OutOfRange, nowMs: AnomalyRecorder.WindowMs + 1);

        Assert.Equal(AnomalyVerdict.Counted, outcome.Verdict);
        Assert.Equal(1, outcome.CountInWindow);
    }

    /// <summary>
    /// La ventana es por tipo, no global: una ráfaga de un tipo no debe reiniciar ni disparar la
    /// cuenta de otro. Sin esto, quien tuviera mala latencia (muchos <c>OutOfRange</c>) taparía o
    /// provocaría el umbral de <c>ProtocolError</c>, que es mucho más estricto.
    /// </summary>
    [Fact]
    public void CadaTipoLlevaSuPropiaCuenta()
    {
        var recorder = new AnomalyRecorder();
        var (protocolWarn, _) = AnomalyThresholds.For(AnomalyKind.ProtocolError);

        for (var i = 0; i < 50; i++)
        {
            recorder.Record(AnomalyKind.OutOfRange, nowMs: 0);
        }

        Assert.Equal(0, recorder.CountOf(AnomalyKind.ProtocolError));

        AnomalyOutcome outcome = default;
        for (var i = 0; i < protocolWarn; i++)
        {
            outcome = recorder.Record(AnomalyKind.ProtocolError, nowMs: 0);
        }

        Assert.Equal(AnomalyVerdict.Warn, outcome.Verdict);
        Assert.Equal(protocolWarn, outcome.CountInWindow);
    }

    /// <summary>
    /// El umbral de protocolo tiene que ser bastante más estricto que el de alcance: un cliente
    /// honesto produce el segundo con sólo tener latencia, y el primero no lo produce nunca.
    /// </summary>
    [Fact]
    public void ElUmbralDeProtocolo_EsMasEstrictoQueElDeAlcance()
    {
        var (protocolWarn, protocolKick) = AnomalyThresholds.For(AnomalyKind.ProtocolError);
        var (rangeWarn, rangeKick) = AnomalyThresholds.For(AnomalyKind.OutOfRange);

        Assert.True(protocolWarn < rangeWarn);
        Assert.True(protocolKick < rangeKick);
    }

    [Fact]
    public void ElUmbralDeAviso_SiempreLlegaAntesQueElDeDesconexion()
    {
        foreach (var kind in Enum.GetValues<AnomalyKind>())
        {
            var (warn, kick) = AnomalyThresholds.For(kind);
            Assert.True(warn > 0, $"{kind}: el umbral de aviso tiene que ser positivo");
            Assert.True(warn < kick, $"{kind}: avisar tiene que llegar antes que desconectar");
        }
    }
}

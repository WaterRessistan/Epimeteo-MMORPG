using System.Globalization;
using Epimeteo.Server.Observability;
using Epimeteo.Server.Security;
using Epimeteo.Shared.Net;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// El formato de exposición de Prometheus (FASE-13 §2 D1). Se comprueba el texto exacto porque es
/// un contrato con un consumidor externo: si el <c># TYPE</c> falta o un histograma no lleva su
/// bucket <c>+Inf</c>, Prometheus rechaza la respuesta entera y no hay forma de enterarse desde
/// aquí.
/// </summary>
public sealed class MetricsRegistryTests
{
    [Fact]
    public void UnContador_SeExponeConSuCabeceraYSuValor()
    {
        var registry = new MetricsRegistry();
        var counter = registry.Counter("epimeteo_test_total", "Un contador de prueba.");
        counter.Increment();
        counter.Add(4);

        var text = registry.Render();

        Assert.Contains("# HELP epimeteo_test_total Un contador de prueba.\n", text, StringComparison.Ordinal);
        Assert.Contains("# TYPE epimeteo_test_total counter\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_test_total 5\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnGaugeConFuente_SeLeeAlExponer()
    {
        var registry = new MetricsRegistry();
        var players = 0;
        registry.Gauge("epimeteo_test_players", "Jugadores.", () => players);

        Assert.Contains("epimeteo_test_players 0\n", registry.Render(), StringComparison.Ordinal);

        players = 7;

        Assert.Contains("epimeteo_test_players 7\n", registry.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnHistograma_LlevaBucketsAcumulativosSumYCount()
    {
        var registry = new MetricsRegistry();
        var histogram = registry.Histogram("epimeteo_test_seconds", "Latencias.", [10, 100]);

        histogram.Observe(5);     // cae en le=10
        histogram.Observe(50);    // cae en le=100
        histogram.Observe(500);   // cae en +Inf

        var text = registry.Render();

        Assert.Contains("# TYPE epimeteo_test_seconds histogram\n", text, StringComparison.Ordinal);

        // Acumulativos: le=10 lleva 1, le=100 lleva ese 1 más el suyo, +Inf lleva los tres.
        Assert.Contains("epimeteo_test_seconds_bucket{le=\"10\"} 1\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_test_seconds_bucket{le=\"100\"} 2\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_test_seconds_bucket{le=\"+Inf\"} 3\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_test_seconds_sum 555\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_test_seconds_count 3\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regresión de un fallo fácil de escribir y difícil de ver: en una máquina con locale
    /// español, <c>ToString()</c> de un <c>double</c> escribe <c>0,5</c> — y Prometheus rechaza la
    /// respuesta entera por una coma.
    /// </summary>
    [Fact]
    public void LosDecimales_SeEscribenConPuntoAunqueElLocaleUseComa()
    {
        // La cultura se arma a mano en vez de con `new CultureInfo("es-ES")`: los tests corren en
        // modo globalization-invariant, donde construir una cultura por nombre lanza. Lo que hace
        // falta reproducir es sólo el separador decimal, y eso sí se puede.
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = comma;

            // Que la premisa del test siga siendo cierta: si esto dejara de escribir coma, el
            // resto no probaría nada y pasaría igual.
            Assert.Equal("0,5", 0.5.ToString("0.###", CultureInfo.CurrentCulture));

            var registry = new MetricsRegistry();
            registry.Histogram("epimeteo_test_decimal", "Con decimales.", [0.5]);

            Assert.Contains("le=\"0.5\"", registry.Render(), StringComparison.Ordinal);
            Assert.DoesNotContain("le=\"0,5\"", registry.Render(), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void UnHistogramaSinBuckets_NoSePuedeConstruir() =>
        Assert.Throws<ArgumentException>(() => new Histogram("x", "y", []));

    [Fact]
    public void LasMetricasDelServidor_SeExponenTodasSinFuentesEnganchadas()
    {
        // Los gauges se enganchan después, ya construido el contenedor: renderizar antes no debe
        // reventar, sólo devolver los contadores e histogramas.
        var text = new ServerMetrics().Render();

        Assert.Contains("epimeteo_messages_received_total", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_tick_duration_microseconds_bucket", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_db_open_duration_microseconds_bucket", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LasMetricasDelServidor_ConGaugesEnganchadosLosExponen()
    {
        var metrics = new ServerMetrics();
        metrics.BindWorldSources(() => 1, () => 2, () => 3, () => 4, () => 5);

        var text = metrics.Render();

        Assert.Contains("epimeteo_sessions 1\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_players 2\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_entities 3\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_monsters 4\n", text, StringComparison.Ordinal);
        Assert.Contains("epimeteo_pending_saves 5\n", text, StringComparison.Ordinal);
    }
}

/// <summary>
/// El mapeo de rechazo a anomalía (FASE-13 §2 D4). Lo que se prueba aquí no es la tabla en sí,
/// sino el criterio: distinguir "no puedes" de "eso no debería haber llegado".
/// </summary>
public sealed class AnomalyMappingTests
{
    [Theory]
    [InlineData(ResultCode.TooFarAway)]
    [InlineData(ResultCode.OutOfRange)]
    [InlineData(ResultCode.TargetNotFound)]
    public void LosRechazosDeAlcance_CuentanComoAnomalia(ResultCode code) =>
        Assert.Equal(AnomalyKind.OutOfRange, AnomalyMapping.For(code));

    [Theory]
    [InlineData(ResultCode.PriceChanged)]
    [InlineData(ResultCode.NotEnoughGold)]
    [InlineData(ResultCode.ItemNotFound)]
    public void LosRechazosConDineroDePorMedio_CuentanComoAnomalia(ResultCode code) =>
        Assert.Equal(AnomalyKind.EconomyRejected, AnomalyMapping.For(code));

    /// <summary>
    /// Lo importante de la tabla: quedarse sin maná, atacar en zona segura o tener el inventario
    /// lleno son respuestas normales que un cliente honesto provoca constantemente. Contarlas
    /// sería echar a jugadores por jugar.
    /// </summary>
    [Theory]
    [InlineData(ResultCode.SafeZone)]
    [InlineData(ResultCode.TargetInSafeZone)]
    [InlineData(ResultCode.OnCooldown)]
    [InlineData(ResultCode.NotEnoughMana)]
    [InlineData(ResultCode.InventoryFull)]
    [InlineData(ResultCode.WrongTool)]
    [InlineData(ResultCode.NotAuthorized)]
    [InlineData(ResultCode.Ok)]
    public void ElJuegoNormal_NoCuentaComoAnomalia(ResultCode code) =>
        Assert.Null(AnomalyMapping.For(code));
}

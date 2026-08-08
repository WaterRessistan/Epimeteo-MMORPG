namespace Epimeteo.Server.Security;

/// <summary>
/// Lo que devuelve apuntar una anomalía: qué hacer y con qué cuenta se cruzó el umbral.
/// </summary>
/// <param name="Verdict">Qué toca hacer con la sesión.</param>
/// <param name="Kind">Tipo apuntado.</param>
/// <param name="CountInWindow">Cuántas van de ese tipo en la ventana actual.</param>
public readonly record struct AnomalyOutcome(AnomalyVerdict Verdict, AnomalyKind Kind, int CountInWindow);

/// <summary>
/// Cuenta rechazos por sesión y por tipo en una ventana deslizante, y decide cuándo dejan de ser
/// ruido (FASE-13 §2 D4).
/// <para>
/// <b>El hueco que cierra:</b> hay ~29 puntos en <c>GameWorld</c> que rechazan una acción, más los
/// de <c>InputQueue</c> y <c>SessionRateLimiter</c>. Cada uno se resolvía solo y se olvidaba. Un
/// cliente honesto falla alguno de vez en cuando; uno parcheado falla <b>el mismo</b> cientos de
/// veces por minuto. Nadie miraba esa diferencia. Detectar ya se detectaba — lo que faltaba era
/// sumar.
/// </para>
/// <para>
/// Puro y determinista: recibe <c>nowMs</c>, nunca consulta el reloj. Así el escalado exacto se
/// puede probar sin esperar un minuto real (mismo criterio que <c>TokenBucket</c> y
/// <c>DeterministicRng</c>). No es seguro entre hilos: cada instancia pertenece a una sesión y
/// sólo la toca su bucle de lectura o el hilo del tick, nunca los dos a la vez.
/// </para>
/// </summary>
public sealed class AnomalyRecorder
{
    /// <summary>Ventana deslizante de conteo, en ms.</summary>
    public const int WindowMs = 60_000;

    private readonly int[] _counts = new int[Enum.GetValues<AnomalyKind>().Length];
    private readonly long[] _windowStartMs = new long[Enum.GetValues<AnomalyKind>().Length];
    private readonly bool[] _warned = new bool[Enum.GetValues<AnomalyKind>().Length];

    /// <summary>
    /// Apunta una anomalía y dice qué hacer. La ventana es por tipo, no global: una ráfaga de
    /// <see cref="AnomalyKind.OutOfRange"/> no debe reiniciar la cuenta de
    /// <see cref="AnomalyKind.ProtocolError"/> ni al revés.
    /// </summary>
    public AnomalyOutcome Record(AnomalyKind kind, long nowMs)
    {
        var index = (int)kind;

        if (_counts[index] == 0 || nowMs - _windowStartMs[index] > WindowMs)
        {
            _windowStartMs[index] = nowMs;
            _counts[index] = 0;
            _warned[index] = false;
        }

        _counts[index]++;
        var count = _counts[index];
        var (warn, kick) = AnomalyThresholds.For(kind);

        if (count >= kick)
        {
            return new AnomalyOutcome(AnomalyVerdict.Kick, kind, count);
        }

        // Sólo el cruce del umbral avisa, no cada anomalía a partir de ahí: si no, pasado el
        // umbral cada rechazo escribiría una línea de log y una fila de BD, y el propio detector
        // sería el que inunda.
        if (count >= warn && !_warned[index])
        {
            _warned[index] = true;
            return new AnomalyOutcome(AnomalyVerdict.Warn, kind, count);
        }

        return new AnomalyOutcome(AnomalyVerdict.Counted, kind, count);
    }

    /// <summary>Cuántas anomalías de ese tipo van en la ventana actual. Para los tests y para el log.</summary>
    public int CountOf(AnomalyKind kind) => _counts[(int)kind];
}

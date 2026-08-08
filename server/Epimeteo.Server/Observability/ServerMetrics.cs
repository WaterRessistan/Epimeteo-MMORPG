namespace Epimeteo.Server.Observability;

/// <summary>
/// Las métricas concretas del juego, declaradas una sola vez al arrancar (FASE-13 §2 D1). Se
/// inyecta donde haga falta incrementarlas; el <see cref="MetricsRegistry"/> de dentro es quien
/// las serializa en <c>/metrics</c>.
/// <para>
/// Los contadores e histogramas existen desde el constructor porque hay hilos que los tocan desde
/// el primer frame. Los <i>gauges</i> que leen del mundo se enganchan aparte, con
/// <see cref="BindWorldSources"/>: <c>GameWorld</c> y <c>SessionManager</c> se construyen después
/// que esto en el contenedor de dependencias, y hacerlos obligatorios en el constructor crearía
/// un ciclo.
/// </para>
/// </summary>
public sealed class ServerMetrics
{
    private readonly MetricsRegistry _registry = new();

    public ServerMetrics()
    {
        MessagesReceived = _registry.Counter(
            "epimeteo_messages_received_total", "Frames recibidos de clientes, ya decodificado el opcode.");

        MessagesRejected = _registry.Counter(
            "epimeteo_messages_rejected_total", "Frames rechazados por rate limit, estado ilegal o formato.");

        SessionsOpened = _registry.Counter(
            "epimeteo_sessions_opened_total", "Conexiones WebSocket aceptadas desde el arranque.");

        SessionsKicked = _registry.Counter(
            "epimeteo_sessions_kicked_total", "Sesiones cerradas por el servidor (rate limit, timeout, protocolo).");

        AnomaliesDetected = _registry.Counter(
            "epimeteo_anomalies_total", "Anomalías de anticheat que cruzaron su umbral de aviso.");

        TicksTotal = _registry.Counter(
            "epimeteo_ticks_total", "Ticks de simulación ejecutados.");

        TickOverruns = _registry.Counter(
            "epimeteo_tick_overruns_total", "Ticks que tardaron más que su intervalo nominal.");

        // Buckets en microsegundos. El presupuesto de un tick a 20 Hz son 50 000 µs, así que lo
        // que importa distinguir es "holgado" (decenas de µs, que es donde está hoy) de
        // "empezando a apretar" (milisegundos) de "se pasó" (>50 ms).
        TickDurationMicros = _registry.Histogram(
            "epimeteo_tick_duration_microseconds",
            "Duración de cada tick de simulación, en microsegundos.",
            [50, 100, 250, 500, 1_000, 5_000, 10_000, 25_000, 50_000]);

        // Abrir una conexión a Postgres en la misma máquina son microsegundos; los buckets altos
        // están para que se note cuando el pool se agota o la BD sufre.
        DatabaseOpenMicros = _registry.Histogram(
            "epimeteo_db_open_duration_microseconds",
            "Tiempo en abrir una conexión a Postgres, en microsegundos.",
            [100, 500, 1_000, 5_000, 10_000, 50_000, 100_000, 500_000]);
    }

    public Counter MessagesReceived { get; }

    public Counter MessagesRejected { get; }

    public Counter SessionsOpened { get; }

    public Counter SessionsKicked { get; }

    public Counter AnomaliesDetected { get; }

    public Counter TicksTotal { get; }

    public Counter TickOverruns { get; }

    public Histogram TickDurationMicros { get; }

    public Histogram DatabaseOpenMicros { get; }

    /// <summary>
    /// Engancha los gauges que se leen del estado vivo. Se llama una vez, al arrancar, cuando el
    /// mundo y el gestor de sesiones ya existen. Los gauges se evalúan al exponer, así que no hay
    /// forma de que se queden desactualizados (ver <see cref="Gauge"/>).
    /// </summary>
    public void BindWorldSources(
        Func<double> sessions, Func<double> players, Func<double> entities, Func<double> monsters, Func<double> pendingSaves)
    {
        _registry.Gauge("epimeteo_sessions", "Sesiones conectadas ahora mismo.", sessions);
        _registry.Gauge("epimeteo_players", "Jugadores dentro del mundo ahora mismo.", players);
        _registry.Gauge("epimeteo_entities", "Entidades vivas en todas las zonas.", entities);
        _registry.Gauge("epimeteo_monsters", "Monstruos vivos en todas las zonas.", monsters);
        _registry.Gauge("epimeteo_pending_saves", "Escrituras pendientes en todas las colas de guardado.", pendingSaves);
    }

    /// <summary>El cuerpo de la respuesta de <c>/metrics</c>.</summary>
    public string Render() => _registry.Render();
}

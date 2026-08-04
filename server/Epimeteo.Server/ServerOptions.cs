namespace Epimeteo.Server;

/// <summary>
/// Configuración del servidor, sección <c>Epimeteo</c> de <c>appsettings.json</c>.
/// Los puertos son siempre de loopback: en producción el acceso público llega por el proxy
/// inverso del 443, nunca directo (ver CLAUDE.md §2).
/// </summary>
public sealed class ServerOptions
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string SectionName = "Epimeteo";

    /// <summary>Puerto del WebSocket de juego, en 127.0.0.1.</summary>
    public int WebSocketPort { get; init; } = 5100;

    /// <summary>Puerto de la API HTTP (<c>/version</c>, <c>/status</c>), en 127.0.0.1.</summary>
    public int HttpPort { get; init; } = 5101;

    /// <summary>Ticks de simulación por segundo.</summary>
    public int TickRate { get; init; } = 20;

    /// <summary>Snapshots enviados a cada cliente por segundo.</summary>
    public int SnapshotRate { get; init; } = 10;

    /// <summary>Margen para que una conexión nueva mande su <c>Hello</c> antes de cerrarla.</summary>
    public int HelloTimeoutMs { get; init; } = 5_000;

    /// <summary>Tiempo sin ningún frame entrante tras el cual se cierra la sesión.</summary>
    public int IdleTimeoutMs { get; init; } = 30_000;

    /// <summary>Frames pendientes de envío por sesión antes de considerarla atascada y cerrarla.</summary>
    public int OutboundQueueCapacity { get; init; } = 256;

    /// <summary>Sesiones simultáneas admitidas. Conexiones extra se rechazan con 503.</summary>
    public int MaxSessions { get; init; } = 200;

    /// <summary>
    /// Intentos de login o registro por IP y minuto (<c>docs/01-protocolo.md § Rate limiting</c>).
    /// Es configurable —no una constante— porque una corrida de <c>tools/Epimeteo.WorldBot</c> con
    /// diez bots desde loopback necesita más de cinco conexiones por minuto. <b>En producción se
    /// queda en 5</b>: subirlo abre la puerta a probar contraseñas a ritmo.
    /// </summary>
    public int LoginAttemptsPerMinute { get; init; } = 5;
}

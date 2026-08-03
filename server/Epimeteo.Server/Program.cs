using Epimeteo.Server;
using Epimeteo.Server.Net;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Accounts;
using Epimeteo.Server.World;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Time;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/epimeteo-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    var options = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();

    var connectionString = builder.Configuration.GetConnectionString("Epimeteo");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Falta ConnectionStrings:Epimeteo. Configúrala en appsettings.Development.json " +
            "(fuera de git) — ver docs/fases/FASE-02-persistencia.md §2.");
    }

    MigrationRunner.Run(connectionString);

    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton(new NpgsqlConnectionFactory(connectionString));
    builder.Services.AddSingleton<PasswordHasher>();
    builder.Services.AddSingleton<AccountRepository>();
    builder.Services.AddSingleton<LoginAttemptRepository>();
    builder.Services.AddSingleton<SessionTokenService>();
    builder.Services.AddSingleton<AuthService>();
    builder.Services.AddSingleton<WorldInbox>();
    builder.Services.AddSingleton<IWorldInbox>(sp => sp.GetRequiredService<WorldInbox>());
    builder.Services.AddSingleton<SessionMessageHandler>();
    builder.Services.AddSingleton<SessionManager>();
    builder.Services.AddSingleton(sp => new GameLoop(
        options.TickRate,
        sp.GetRequiredService<WorldInbox>(),
        sp.GetRequiredService<SessionManager>().OnTick));
    builder.Services.AddHostedService<GameLoopService>();

    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        // Sólo loopback: al exterior se sale por el proxy inverso del 443 (CLAUDE.md §2).
        kestrel.ListenLocalhost(options.WebSocketPort);
        kestrel.ListenLocalhost(options.HttpPort);
        kestrel.AddServerHeader = false;
        kestrel.Limits.MaxRequestBodySize = 64 * 1024;
    });

    var app = builder.Build();

    app.UseWebSockets(new WebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(15),
    });

    // Cada puerto expone exactamente una superficie. El puerto de juego no sirve HTTP y el de
    // la API no acepta upgrades: así una regla de proxy mal puesta falla de forma ruidosa.
    app.Use(async (context, next) =>
    {
        var port = context.Connection.LocalPort;
        var path = context.Request.Path;

        var allowed = port == options.WebSocketPort
            ? path.Equals("/ws", StringComparison.Ordinal)
            : path.Equals("/version", StringComparison.Ordinal) || path.Equals("/status", StringComparison.Ordinal);

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    });

    app.MapGet("/ws", async (HttpContext context, SessionManager sessions, ServerOptions opts) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (sessions.Count >= opts.MaxSessions)
        {
            Log.Warning("Conexión rechazada: {Count} sesiones, límite {Max}", sessions.Count, opts.MaxSessions);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var remote = context.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        var session = sessions.Create(socket, remote);

        try
        {
            await session.RunAsync(context.RequestAborted);
        }
        finally
        {
            sessions.Remove(session);
        }
    });

    app.MapGet("/version", (ServerOptions opts) => Results.Json(new
    {
        server = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        protocolVersion = ProtocolVersion.Current,
        tickRate = opts.TickRate,
        snapshotRate = opts.SnapshotRate,
    }));

    app.MapGet("/status", (SessionManager sessions, GameLoop loop) =>
    {
        var stats = loop.Metrics.Snapshot();
        return Results.Json(new
        {
            uptimeMs = ServerClock.NowMs,
            sessions = sessions.Count,
            tick = new
            {
                current = loop.CurrentTick,
                total = stats.Ticks,
                overruns = stats.Overruns,
                lastUs = stats.LastUs,
                avgUs = stats.AvgUs,
                p99Us = stats.P99Us,
                maxUs = stats.MaxUs,
            },
        });
    });

    Log.Information("Epimeteo servidor · protocolo v{Protocol} · WS 127.0.0.1:{WsPort} · HTTP 127.0.0.1:{HttpPort}",
        ProtocolVersion.Current, options.WebSocketPort, options.HttpPort);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "El servidor no pudo arrancar");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Punto de entrada. Declarado explícitamente para poder referenciarlo desde los tests.</summary>
public partial class Program;

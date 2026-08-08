using System.Net;
using Epimeteo.Server;
using Epimeteo.Server.Content;
using Epimeteo.Server.Farm;
using Epimeteo.Server.Inventory;
using Epimeteo.Server.Net;
using Epimeteo.Server.Observability;
using Epimeteo.Server.Persistence;
using Epimeteo.Server.Persistence.Accounts;
using Epimeteo.Server.Persistence.Admin;
using Epimeteo.Server.Persistence.Anomalies;
using Epimeteo.Server.Persistence.Characters;
using Epimeteo.Server.Persistence.Chat;
using Epimeteo.Server.Persistence.Combat;
using Epimeteo.Server.Persistence.Economy;
using Epimeteo.Server.Persistence.Farm;
using Epimeteo.Server.Persistence.Items;
using Epimeteo.Server.Shop;
using Epimeteo.Server.World;
using System.Security.Cryptography;
using System.Text;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Time;
using Microsoft.AspNetCore.HttpOverrides;
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

    // Se construye antes que nada porque casi todo lo demás la recibe por constructor.
    var metrics = new ServerMetrics();
    builder.Services.AddSingleton(metrics);
    builder.Services.AddSingleton(new NpgsqlConnectionFactory(connectionString, metrics));
    builder.Services.AddSingleton<PasswordHasher>();
    builder.Services.AddSingleton<AccountRepository>();
    builder.Services.AddSingleton<LoginAttemptRepository>();
    builder.Services.AddSingleton<SessionTokenService>();
    builder.Services.AddSingleton<AuthService>();
    var contentRoot = ContentPaths.ResolveContentRoot();
    builder.Services.AddSingleton(new ClassCatalog(contentRoot));
    builder.Services.AddSingleton(new MapCatalog(contentRoot));
    builder.Services.AddSingleton(new ItemCatalog(contentRoot));
    var shopCatalog = new ShopCatalog(contentRoot);
    builder.Services.AddSingleton(shopCatalog);
    builder.Services.AddSingleton(new CropCatalog(contentRoot));
    builder.Services.AddSingleton(new MonsterCatalog(contentRoot));
    builder.Services.AddSingleton(new SkillCatalog(contentRoot));
    builder.Services.AddSingleton<CharacterRepository>();
    builder.Services.AddSingleton<CharacterService>();
    builder.Services.AddSingleton<ItemRepository>();
    builder.Services.AddSingleton<EconomyLogRepository>();
    builder.Services.AddSingleton<ShopStockRepository>();
    builder.Services.AddSingleton<FarmPlotRepository>();
    builder.Services.AddSingleton<FarmTileRepository>();
    builder.Services.AddSingleton<FarmCalendarRepository>();
    builder.Services.AddSingleton<CombatLogRepository>();
    builder.Services.AddSingleton<ChatLogRepository>();
    builder.Services.AddSingleton<AdminActionRepository>();
    builder.Services.AddSingleton<AnomalyRepository>();
    builder.Services.AddSingleton<EntityIdAllocator>();
    builder.Services.AddSingleton<WorldInbox>();
    builder.Services.AddSingleton<IWorldInbox>(sp => sp.GetRequiredService<WorldInbox>());
    builder.Services.AddSingleton<CharacterSaver>();
    builder.Services.AddSingleton<ICharacterSink>(sp => sp.GetRequiredService<CharacterSaver>());
    builder.Services.AddSingleton<InventorySaver>();
    builder.Services.AddSingleton<IInventorySink>(sp => sp.GetRequiredService<InventorySaver>());
    builder.Services.AddSingleton<EconomySaver>();
    builder.Services.AddSingleton<IEconomySink>(sp => sp.GetRequiredService<EconomySaver>());
    builder.Services.AddSingleton<FarmTileSaver>();
    builder.Services.AddSingleton<IFarmSink>(sp => sp.GetRequiredService<FarmTileSaver>());
    builder.Services.AddSingleton<CombatLogSaver>();
    builder.Services.AddSingleton<ICombatLogSink>(sp => sp.GetRequiredService<CombatLogSaver>());
    builder.Services.AddSingleton<ChatLogSaver>();
    builder.Services.AddSingleton<IChatLogSink>(sp => sp.GetRequiredService<ChatLogSaver>());
    builder.Services.AddSingleton<AdminActionSaver>();
    builder.Services.AddSingleton<IAdminActionSink>(sp => sp.GetRequiredService<AdminActionSaver>());
    builder.Services.AddSingleton<AnomalySaver>();
    builder.Services.AddSingleton<IAnomalySink>(sp => sp.GetRequiredService<AnomalySaver>());

    // El stock de tiendas y el estado de granja se cargan una vez aquí, de forma
    // síncrona-bloqueante, igual que MigrationRunner.Run un poco más arriba: es el arranque del
    // proceso, no el tick — bloquear aquí no para nada que ya esté simulando (CLAUDE.md §4 habla
    // del tick, no de esto).
    var shopStockRows = new ShopStockRepository(new NpgsqlConnectionFactory(connectionString))
        .ListAllAsync().GetAwaiter().GetResult();
    builder.Services.AddSingleton(new ShopRuntime(shopCatalog, shopStockRows));

    var farmConnections = new NpgsqlConnectionFactory(connectionString);
    var farmPlotRows = new FarmPlotRepository(farmConnections).ListAllAsync().GetAwaiter().GetResult();
    var farmTileRows = new FarmTileRepository(farmConnections).ListAllAsync().GetAwaiter().GetResult();
    var farmLastDayIndex = new FarmCalendarRepository(farmConnections).GetLastDayIndexAsync().GetAwaiter().GetResult();
    builder.Services.AddSingleton(new FarmRuntime(farmPlotRows, farmTileRows, farmLastDayIndex));

    builder.Services.AddSingleton<GameWorld>();
    builder.Services.AddSingleton<SessionMessageHandler>();
    builder.Services.AddSingleton<SessionManager>();
    builder.Services.AddSingleton(sp => new GameLoop(
        options.TickRate,
        sp.GetRequiredService<GameWorld>(),
        sp.GetRequiredService<SessionManager>().OnTick,
        sp.GetRequiredService<ServerMetrics>()));

    // Las colas de guardado arrancan antes que el bucle y, por tanto, se paran después: así el
    // vaciado final del apagado todavía tiene quien lo escriba.
    builder.Services.AddHostedService(sp => sp.GetRequiredService<CharacterSaver>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<InventorySaver>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EconomySaver>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<FarmTileSaver>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<CombatLogSaver>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ChatLogSaver>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AdminActionSaver>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AnomalySaver>());
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

    // Detrás del proxy inverso (Fase 5, docs/fases/FASE-05-despliegue.md §D2),
    // Connection.RemoteIpAddress sería siempre 127.0.0.1 y el rate limit de login por IP
    // (AuthService, 5/min) dejaría de proteger a nadie. KnownProxies limitado al propio loopback:
    // nginx corre en esta misma máquina, así que sólo se confía en la cabecera si la conexión TCP
    // ya venía del loopback. El puerto de Kestrel además es sólo loopback (más abajo), así que
    // nadie de fuera puede alcanzarlo directamente para falsificar la cabecera.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor,
        KnownProxies = { IPAddress.Loopback, IPAddress.IPv6Loopback },
    });

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
            : path.Equals("/version", StringComparison.Ordinal)
                || path.Equals("/status", StringComparison.Ordinal)
                || path.Equals("/metrics", StringComparison.Ordinal);

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    });

    // Autenticación de los endpoints internos (FASE-13 §2 D2). Va aquí, en el pipeline, y no en
    // cada endpoint: así una ruta interna nueva nace protegida en vez de nacer abierta y
    // esperar a que alguien se acuerde.
    //
    // El hallazgo que lo motiva: /status estaba respondiendo 200 en internet
    // (https://<dominio>/status), filtrando jugadores conectados, colas de guardado y tiempos de
    // tick a cualquiera que probara la URL — nginx proxificaba `location /` entero al 5101 para
    // servir /version. Se cierra en dos capas independientes: esta y la de nginx.
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var isInternal = path.Equals("/status", StringComparison.Ordinal)
            || path.Equals("/metrics", StringComparison.Ordinal);

        if (isInternal && !IsAuthorized(context, options.MetricsToken))
        {
            // 404 y no 401: un 401 confirma que la ruta existe. Quien tenga el token sabe que
            // está ahí; para el resto no hay nada que descubrir.
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

    app.MapGet("/status", (
        SessionManager sessions, GameLoop loop, GameWorld world,
        CharacterSaver saver, InventorySaver invSaver, EconomySaver econSaver, FarmTileSaver farmSaver,
        CombatLogSaver combatSaver, ChatLogSaver chatSaver, AdminActionSaver adminSaver,
        AnomalySaver anomalySaver) =>
    {
        var stats = loop.Metrics.Snapshot();
        return Results.Json(new
        {
            uptimeMs = ServerClock.NowMs,
            sessions = sessions.Count,
            world = new
            {
                zones = world.Zones.Count,
                players = world.PlayerCount,
                entities = world.EntityCount,
                pendingSaves = saver.PendingCount,
                pendingInventorySaves = invSaver.PendingCount,
                pendingEconomySaves = econSaver.PendingCount,
                pendingFarmSaves = farmSaver.PendingCount,
                pendingCombatSaves = combatSaver.PendingCount,
                pendingChatSaves = chatSaver.PendingCount,
                pendingAdminSaves = adminSaver.PendingCount,
                pendingAnomalySaves = anomalySaver.PendingCount,
                monsters = world.MonsterCount,
            },
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

    app.MapGet("/metrics", (ServerMetrics serverMetrics) =>
        Results.Text(serverMetrics.Render(), "text/plain; version=0.0.4"));

    // Los gauges se enganchan aquí, ya construido el contenedor: leen del estado vivo en el
    // momento de exponerlos, así que no hay forma de que se queden desactualizados.
    {
        var world = app.Services.GetRequiredService<GameWorld>();
        var sessions = app.Services.GetRequiredService<SessionManager>();
        var savers = new Func<int>[]
        {
            () => app.Services.GetRequiredService<CharacterSaver>().PendingCount,
            () => app.Services.GetRequiredService<InventorySaver>().PendingCount,
            () => app.Services.GetRequiredService<EconomySaver>().PendingCount,
            () => app.Services.GetRequiredService<FarmTileSaver>().PendingCount,
            () => app.Services.GetRequiredService<CombatLogSaver>().PendingCount,
            () => app.Services.GetRequiredService<ChatLogSaver>().PendingCount,
            () => app.Services.GetRequiredService<AdminActionSaver>().PendingCount,
            () => app.Services.GetRequiredService<AnomalySaver>().PendingCount,
        };

        metrics.BindWorldSources(
            () => sessions.Count,
            () => world.PlayerCount,
            () => world.EntityCount,
            () => world.MonsterCount,
            () => savers.Sum(pending => pending()));
    }

    if (string.IsNullOrEmpty(options.MetricsToken))
    {
        Log.Warning(
            "Epimeteo:MetricsToken sin configurar: /status y /metrics responden 404. " +
            "Es el fallo seguro por defecto; configúralo para poder consultarlos.");
    }

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
public partial class Program
{
    /// <summary>
    /// Comprueba <c>Authorization: Bearer &lt;token&gt;</c> contra el configurado (FASE-13 §2 D2).
    /// <para>
    /// Token vacío devuelve falso siempre: sin configurar, cerrado. Y la comparación es en tiempo
    /// constante — comparar con <c>==</c> corta en el primer byte distinto y filtra el token por
    /// tiempo de respuesta, byte a byte. Aquí no cuesta nada evitarlo.
    /// </para>
    /// </summary>
    private static bool IsAuthorized(HttpContext context, string expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken))
        {
            return false;
        }

        var header = context.Request.Headers.Authorization.ToString();
        const string Prefix = "Bearer ";
        if (!header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(header[Prefix.Length..]),
            Encoding.UTF8.GetBytes(expectedToken));
    }
}

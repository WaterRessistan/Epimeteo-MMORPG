using System.Diagnostics;
using System.Text.Json;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.WorldBot;

/// <summary>
/// Verificación del netcode de la Fase 4 sin Godot: levanta clientes de verdad que ejecutan la
/// misma predicción y reconciliación que el juego, los mueve por guiones y comprueba las nueve
/// propiedades del criterio de aceptación (FASE-04 §10).
/// <code>
/// dotnet run --project tools/Epimeteo.WorldBot -- [ws://127.0.0.1:5100/ws] [--lag-ms 150] [--bots 2]
/// </code>
/// </summary>
internal static class Program
{
    private const int TickMs = SimulationConstants.TickDtMs;

    private static int _failures;
    private static int _checks;

    private static async Task<int> Main(string[] args)
    {
        var url = args.FirstOrDefault(a => a.StartsWith("ws", StringComparison.Ordinal)) ?? "ws://127.0.0.1:5100/ws";
        var statusUrl = Arg(args, "--status-url") ?? "http://127.0.0.1:5101/status";
        var lagMs = int.Parse(Arg(args, "--lag-ms") ?? "0");
        var bots = int.Parse(Arg(args, "--bots") ?? "2");

        var map = MapLoader.Load(Path.Combine(RepoRoot(), "content", "maps", "map.village.json"));
        var run = Guid.NewGuid().ToString("N")[..6];

        Console.WriteLine($"Servidor : {url}");
        Console.WriteLine($"Mapa     : {map.Key} {map.Width}×{map.Height}, hash {map.Hash:X8}");
        Console.WriteLine($"Latencia : {lagMs} ms simulados en cada sentido");
        Console.WriteLine($"Bots     : {bots}\n");

        if (bots > 4)
        {
            Console.WriteLine(
                "  [ ?? ] Más de 4 bots exigen subir Epimeteo:LoginAttemptsPerMinute en\n" +
                "         appsettings.Development.json: el cupo por IP es de 5 conexiones/minuto.\n");
        }

        var a = new Bot(url, $"bot_{run}_a", $"BotA_{run}", map, lagMs);
        var b = new Bot(url, $"bot_{run}_b", $"BotB_{run}", map, lagMs);
        Bot? vuelto = null;

        try
        {
            await a.ConnectAsync(register: true);
            await Task.Delay(200);
            await b.ConnectAsync(register: true);

            var flota = new List<Bot> { a, b };

            await Reposo(flota, a, b, lagMs);
            await CruzarLaMuralla(flota, a, b);
            await Volver(flota, a, b);
            await PasearEnCuadrado(flota, a, lagMs);
            await ChocarConElEdificio(flota, b, map);

            Check("Ningún bot fue expulsado en el juego normal", a.Kicked is null && b.Kicked is null);
            Check("La posición autoritativa nunca cayó dentro de un sólido",
                a.SolidViolations == 0 && b.SolidViolations == 0);

            // La sesión de A se sustituye por la que vuelve; la prueba de trampas va la última
            // porque termina con la sesión cerrada a propósito.
            vuelto = await Persistencia(flota, a, url, map, lagMs);
            await IntentarCorrerMas(flota, vuelto, b);

            if (bots > 2)
            {
                await Carga(url, map, lagMs, bots - 2, run, statusUrl);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  [MAL] La corrida se cortó: {ex.Message}");
            _failures++;
        }
        finally
        {
            await a.DisposeAsync();
            await b.DisposeAsync();

            if (vuelto is not null)
            {
                await vuelto.DisposeAsync();
            }
        }

        Console.WriteLine(_failures == 0
            ? $"\n{_checks}/{_checks} comprobaciones en verde."
            : $"\n{_failures} de {_checks} comprobaciones fallidas.");

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>Tres segundos parados: ritmo de snapshots, acuses de recibo y cero correcciones.</summary>
    private static async Task Reposo(List<Bot> flota, Bot a, Bot b, int lagMs)
    {
        Console.WriteLine("· Reposo (3 s)");
        Reset(flota, MovementPattern.Quieto);
        await Run(flota, 3000);

        // 3 s a 10 Hz son 30 snapshots; se admite un margen por el arranque y el retardo simulado.
        Check($"Snapshots a ~10 Hz (A: {a.Snapshots} en 3 s)", a.Snapshots is >= 22 and <= 36);
        Check($"El servidor acusa recibo de los inputs (seq {a.LastAckedSeq})", a.LastAckedSeq > 40);
        Check("Los dos bots se ven al empezar juntos",
            a.Spawned.Contains(b.MyEntityId) && b.Spawned.Contains(a.MyEntityId));

        var flags = a.ZoneUpdates.LastOrDefault();
        Check($"El spawn está en zona segura ({flags.Region})", flags.Flags.HasFlag(ZoneFlags.Safe));

        if (lagMs == 0)
        {
            Check($"Sin latencia y parado: 0 correcciones (A: {a.Corrections})", a.Corrections == 0);
        }
    }

    /// <summary>El bot A sube al campo del norte cruzando la puerta de un tile de la muralla.</summary>
    private static async Task CruzarLaMuralla(List<Bot> flota, Bot a, Bot b)
    {
        Console.WriteLine("· A cruza la muralla hacia el campo de PvP (13 s)");
        Reset(flota, MovementPattern.Quieto);
        a.Pattern = MovementPattern.Muralla;
        await Run(flota, 13_000);

        Check($"A pasó la puerta de un tile (y = {a.ServerPos.Y:F1})", a.ServerPos.Y < 44f);

        var pvp = a.ZoneUpdates.Any(update => update.Flags.HasFlag(ZoneFlags.Pvp));
        Check("Al cruzar la muralla llega ZoneFlagsUpdate con PvP", pvp);

        Check($"B deja de ver a A al alejarse ({b.Despawned.Count} despawn)",
            b.Despawned.Count(entry => entry.Id == a.MyEntityId && entry.Reason == DespawnReason.OutOfRange) == 1);
        Check("Y A deja de ver a B, exactamente una vez",
            a.Despawned.Count(entry => entry.Id == b.MyEntityId && entry.Reason == DespawnReason.OutOfRange) == 1);
    }

    /// <summary>Y vuelve al pueblo por el mismo sitio.</summary>
    private static async Task Volver(List<Bot> flota, Bot a, Bot b)
    {
        Console.WriteLine("· A vuelve al pueblo (13 s)");
        Reset(flota, MovementPattern.Quieto);
        a.Pattern = MovementPattern.MurallaVuelta;
        await Run(flota, 13_000);

        Check($"A vuelve al pueblo (y = {a.ServerPos.Y:F1})", a.ServerPos.Y > 52f);
        Check("Al volver a acercarse, B vuelve a ver a A exactamente una vez",
            b.Spawned.Count(id => id == a.MyEntityId) == 1);
        Check("Y A vuelve a ver a B exactamente una vez",
            a.Spawned.Count(id => id == b.MyEntityId) == 1);
        Check("Al volver al pueblo, ZoneFlagsUpdate vuelve a decir zona segura",
            a.ZoneUpdates.LastOrDefault().Flags.HasFlag(ZoneFlags.Safe));
    }

    /// <summary>Diez segundos dando vueltas: es la prueba de goma elástica.</summary>
    private static async Task PasearEnCuadrado(List<Bot> flota, Bot a, int lagMs)
    {
        Console.WriteLine("· A pasea en cuadrado (12 s)");
        Reset(flota, MovementPattern.Quieto);
        a.Pattern = MovementPattern.Circulo;
        var correccionesAntes = a.Corrections;
        await Run(flota, 12_000);

        var correcciones = a.Corrections - correccionesAntes;

        if (lagMs == 0)
        {
            Check($"Sin latencia: ni una corrección en 12 s de movimiento ({correcciones})", correcciones == 0);
        }
        else
        {
            Check($"Con {lagMs} ms: menos de una corrección por segundo ({correcciones} en 12 s)", correcciones < 12);
            Check($"Con {lagMs} ms: ninguna corrección mayor de 0,3 tiles ({a.MaxErrorTiles:F3})",
                a.MaxErrorTiles < 0.3f);
        }

        // La predicción va por delante de la última posición confirmada exactamente el tiempo que
        // tarda el viaje de ida y vuelta más la antigüedad del snapshot. Eso no es deriva: es lo
        // que se está compensando. Lo que no puede es crecer por encima de eso.
        var adelantoMs = (2 * lagMs) + SimulationConstants.InterpolationDelayMs + SimulationConstants.TickDtMs;
        var esperado = adelantoMs / 1000f * SimulationConstants.WalkSpeedTilesPerSec;
        var adelanto = MathF.Sqrt(Vec2.DistanceSquared(a.ServerPos, a.Prediction!.Predicted.Pos));

        Check(
            $"La predicción va por delante lo que dice la latencia y no más " +
            $"({adelanto:F2} tiles ≤ {esperado + 0.5f:F2} esperadas)",
            adelanto <= esperado + 0.5f);
    }

    /// <summary>El bot B empuja contra el edificio del este hasta quedarse pegado a la pared.</summary>
    private static async Task ChocarConElEdificio(List<Bot> flota, Bot b, GameMap map)
    {
        Console.WriteLine("· B empuja contra un edificio (6 s)");
        Reset(flota, MovementPattern.Quieto);
        b.Pattern = MovementPattern.Muro;
        await Run(flota, 6000);

        var tile = new Vec2(b.ServerPos.X + SimulationConstants.PlayerHalfWidth + 0.1f, b.ServerPos.Y).ToTile();
        Check($"B se queda pegado a la pared en x = {b.ServerPos.X:F3}", map.Collision.IsSolid(tile.X, tile.Y));
        Check("Y no la atraviesa",
            !map.Collision.IsBlocked(b.ServerPos, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight));
    }

    /// <summary>
    /// Un cliente parcheado manda el triple de inputs. El presupuesto de la cola descarta el
    /// exceso —así que no recorre más terreno— y, si insiste, la sesión se cierra. Va la última
    /// porque acaba con el bot expulsado a propósito.
    /// </summary>
    private static async Task IntentarCorrerMas(List<Bot> flota, Bot tramposo, Bot honesto)
    {
        Console.WriteLine("· El bot que volvió manda el triple de inputs (6 s)");
        Reset(flota, MovementPattern.Muralla);
        tramposo.InputsPerTick = 3;

        // Primer segundo: el presupuesto ya descarta el exceso pero aún no se ha llegado al
        // límite de strikes, así que se puede medir cuánto avanza cada uno con los dos dentro.
        await Run(flota, 1000);

        var ventaja = honesto.ServerDistance <= 0.01f
            ? 0f
            : (tramposo.ServerDistance / honesto.ServerDistance) - 1f;

        // La única ventaja posible es la ráfaga de arranque de la cola (6 fichas sobre 20/s, un
        // 30 % como mucho y una sola vez). No hay ventaja sostenida: el ritmo lo fija el cubo de
        // fichas y, en cuanto insiste, la comprobación siguiente demuestra que se le cierra.
        Check(
            $"Mandar 3× inputs sólo da la ráfaga de arranque ({tramposo.ServerDistance:F2} vs " +
            $"{honesto.ServerDistance:F2} tiles en 1 s, {ventaja * 100:F1} % ≤ 30 % teórico)",
            ventaja < 0.31f && tramposo.Kicked is null);

        // Y si insiste, se acaba yendo.
        await Run(flota, 5000);

        Check($"Insistir con 3× acaba en desconexión ({tramposo.Kicked})", tramposo.Kicked == KickReason.RateLimited);
        Check("El bot honesto sigue dentro", honesto.Kicked is null);
    }

    /// <summary>Se desconecta, vuelve a entrar y tiene que aparecer donde lo dejó.</summary>
    private static async Task<Bot> Persistencia(List<Bot> flota, Bot a, string url, GameMap map, int lagMs)
    {
        Console.WriteLine("· A se desconecta y vuelve a entrar");
        var antes = a.ServerPos;

        await a.DisconnectAsync();
        flota.Remove(a);
        await Task.Delay(1500);

        var vuelto = new Bot(url, a.Username, a.CharacterName, map, lagMs);
        await vuelto.ConnectAsync(register: false);
        flota.Add(vuelto);

        var enter = vuelto.WorldEnter!;
        var distancia = MathF.Sqrt(Vec2.DistanceSquared(antes, new Vec2(enter.SpawnX, enter.SpawnY)));

        Check(
            $"Vuelve donde lo dejó ({antes.X:F2},{antes.Y:F2}) → ({enter.SpawnX:F2},{enter.SpawnY:F2}), " +
            $"{distancia:F2} tiles",
            distancia < 0.3f);

        // Un par de segundos de juego normal con la sesión nueva antes de la prueba de trampas.
        Reset(flota, MovementPattern.Quieto);
        await Run(flota, 2000);
        Check("La sesión que vuelve se mantiene sana", vuelto.Kicked is null);

        return vuelto;
    }

    /// <summary>Bots extra para ver cómo aguanta el tick. Necesita el cupo de login subido.</summary>
    private static async Task Carga(string url, GameMap map, int lagMs, int extra, string run, string statusUrl)
    {
        Console.WriteLine($"· Carga con {extra} bots más (10 s)");
        var flota = new List<Bot>();

        try
        {
            for (var i = 0; i < extra; i++)
            {
                var bot = new Bot(url, $"bot_{run}_{i:D2}", $"BotC{i:D2}_{run}", map, lagMs);
                await bot.ConnectAsync(register: true);
                bot.Pattern = MovementPattern.Circulo;
                flota.Add(bot);
                await Task.Delay(150);
            }

            Reset(flota, MovementPattern.Circulo);
            await Run(flota, 10_000);

            Check("Ningún bot de carga fue expulsado", flota.All(bot => bot.Kicked is null));
            Check("Ninguno acabó dentro de un sólido", flota.All(bot => bot.SolidViolations == 0));
            await ComprobarTick(statusUrl);
        }
        finally
        {
            foreach (var bot in flota)
            {
                await bot.DisposeAsync();
            }
        }
    }

    private static async Task ComprobarTick(string statusUrl)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var document = JsonDocument.Parse(await http.GetStringAsync(statusUrl));
            var tick = document.RootElement.GetProperty("tick");
            var avgUs = tick.GetProperty("avgUs").GetInt64();
            var overruns = tick.GetProperty("overruns").GetInt64();

            Check($"El tick medio se mantiene por debajo de 5 ms ({avgUs / 1000.0:F2} ms)", avgUs < 5000);
            Check($"Sin ticks fuera de presupuesto ({overruns})", overruns == 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"  [ -- ] No se pudo leer {statusUrl}: {ex.Message}");
        }
    }

    private static void Reset(List<Bot> flota, MovementPattern pattern)
    {
        foreach (var bot in flota)
        {
            bot.ResetPhase();
            bot.Pattern = pattern;
            bot.PatternStartMs = Now();
        }
    }

    /// <summary>Hace correr a toda la flota a 20 Hz durante el tiempo pedido.</summary>
    private static async Task Run(List<Bot> flota, int durationMs)
    {
        var end = Now() + durationMs;
        var next = Now();

        while (Now() < end)
        {
            var now = Now();
            foreach (var bot in flota)
            {
                bot.Tick(now);
            }

            next += TickMs;
            var wait = (int)(next - Now());
            await Task.Delay(wait > 0 ? wait : 0);
        }
    }

    private static long Now() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    private static string? Arg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Epimeteo.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static void Check(string descripcion, bool ok)
    {
        _checks++;
        if (!ok)
        {
            _failures++;
        }

        Console.WriteLine($"  [{(ok ? "OK " : "MAL")}] {descripcion}");
    }
}

using System.Diagnostics;
using System.Text.Json;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
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

        // Flujo de tiendas de la Fase 7 (FASE-07-tiendas.md §9): aparte del guion de netcode de
        // arriba porque necesita dos corridas separadas con una manipulación de Postgres a mano
        // entre medias (bajar la durabilidad de un ítem — nada la desgasta todavía de verdad).
        if (args.Contains("--shops-buy"))
        {
            return await ShopsBuyPhase(url, map);
        }

        if (args.Contains("--shops-repair"))
        {
            var username = Arg(args, "--shops-repair") ?? throw new InvalidOperationException("--shops-repair necesita el username de la fase de compra.");
            return await ShopsRepairPhase(url, map, username);
        }

        // Flujo de granja de la Fase 8 (FASE-08-granja-cultivos.md §9): mismo motivo que el de
        // tiendas — dos corridas separadas con una manipulación de Postgres a mano entre medias,
        // esta vez para simular que pasaron 3 días de granja de verdad sin esperarlos.
        if (args.Contains("--farm-plant"))
        {
            return await FarmPlantPhase(url, map);
        }

        if (args.Contains("--farm-harvest"))
        {
            var username = Arg(args, "--farm-harvest") ?? throw new InvalidOperationException("--farm-harvest necesita el username de la fase de plantar.");
            return await FarmHarvestPhase(url, map, username);
        }

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

    /// <summary>
    /// Fase 1 del flujo de tiendas (FASE-07-tiendas.md §9): registrarse, comprar y vender de
    /// verdad contra el servidor. Termina imprimiendo el <c>username</c> para que
    /// <see cref="ShopsRepairPhase"/> —aparte, porque necesita que alguien baje la durabilidad de
    /// un ítem por SQL entre medias, ya que nada la desgasta todavía de verdad— pueda reconectar
    /// con el mismo personaje.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --shops-buy [url]</code>
    /// </summary>
    private static async Task<int> ShopsBuyPhase(string url, GameMap map)
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var bot = new Bot(url, $"shop_{run}", $"Shop_{run}", map, lagMs: 0);

        try
        {
            Console.WriteLine("· Fase 1: registro, compra y venta");
            await bot.ConnectAsync(register: true);
            await Run([bot], 300); // deja llegar InventoryFull/EquipmentUpdate/EntitySpawn del join

            var armory = bot.KnownEntities.Values.FirstOrDefault(e => e.DefKey == "shop.armory");
            Check("El NPC de la armería está visible al entrar (spawn cae en su celda de AOI)", armory is not null);
            if (armory is null)
            {
                return Summarize();
            }

            // Lejos todavía: falla por distancia antes de mover un solo paso (FASE-07 §2 D7).
            bot.SendNow(Opcode.ShopOpen, new C2SShopOpen { NpcEntityId = armory.Id });
            await Run([bot], 500);
            Check("ShopOpen lejos del NPC → TooFarAway",
                bot.ShopResults.Any(r => r is { Ok: false, Code: ResultCode.TooFarAway }));

            bot.WalkTarget = new Vec2(armory.X, armory.Y);
            await Run([bot], 6000);
            Check("El bot llegó de verdad junto al NPC caminando", bot.WalkTarget is null);

            bot.ShopResults.Clear();
            bot.SendNow(Opcode.ShopOpen, new C2SShopOpen { NpcEntityId = armory.Id });
            await Run([bot], 500);
            Check("ShopOpen cerca del NPC → ShopData", bot.LastShopData is { ShopKey: "shop.armory", CanRepair: true });

            if (bot.LastShopData is not { } shopData)
            {
                return Summarize();
            }

            var swordSlot = Array.FindIndex(shopData.Slots, s => s.DefKey == "item.iron_sword");
            Check("La armería vende espadas de hierro", swordSlot >= 0);
            if (swordSlot < 0)
            {
                return Summarize();
            }

            var swordPrice = shopData.Slots[swordSlot].PriceBuy;

            var goldBeforeWrongPrice = bot.Gold;
            bot.SendNow(Opcode.ShopBuy, new C2SShopBuy { ShopSlot = (byte)swordSlot, Quantity = 1, ExpectedPrice = 1 });
            await Run([bot], 500);
            Check("Comprar con precio equivocado → PriceChanged, oro intacto",
                bot.ShopResults.Any(r => r is { Ok: false, Code: ResultCode.PriceChanged }) && bot.Gold == goldBeforeWrongPrice);

            var goldBeforeBuy = bot.Gold;
            bot.SendNow(Opcode.ShopBuy, new C2SShopBuy { ShopSlot = (byte)swordSlot, Quantity = 1, ExpectedPrice = swordPrice });
            await Run([bot], 500);
            Check($"Comprar una espada baja el oro en {swordPrice} (de {goldBeforeBuy} a {bot.Gold})",
                bot.Gold == goldBeforeBuy - swordPrice);

            var shieldSlot = Array.FindIndex(shopData.Slots, s => s.DefKey == "item.wooden_shield");
            Check("La armería recompra escudos de madera", shieldSlot >= 0);
            if (shieldSlot >= 0)
            {
                var shieldSellPrice = shopData.Slots[shieldSlot].PriceSell;
                var goldBeforeSell = bot.Gold;

                // El escudo del kit inicial vive en WeaponBag/1 (content/classes/warrior.json:
                // espada, escudo, pociones, en ese orden — FASE-06 §2 D6).
                bot.SendNow(Opcode.ShopSell, new C2SShopSell
                {
                    Container = ContainerId.WeaponBag, Slot = 1, Quantity = 1, ExpectedPrice = shieldSellPrice,
                });
                await Run([bot], 500);
                Check($"Vender el escudo del kit inicial sube el oro en {shieldSellPrice}",
                    bot.Gold == goldBeforeSell + shieldSellPrice);
            }

            Console.WriteLine($"\nusername={bot.Username}");
            Console.WriteLine($"characterId={bot.CharacterId}");
            Console.WriteLine(
                "\nPara la fase de reparación: baja por SQL la durabilidad del arma del kit\n" +
                "inicial (WeaponBag/0) y vuelve a lanzar con --shops-repair <username>. P. ej.:\n" +
                $"  UPDATE item_instances SET durability = 40\n" +
                $"    WHERE owner_char_id = {bot.CharacterId} AND container = 1 AND slot = 0;\n");

            return Summarize();
        }
        finally
        {
            await bot.DisposeAsync();
        }
    }

    /// <summary>
    /// Fase 2 del flujo de tiendas: reconecta con el personaje de <see cref="ShopsBuyPhase"/> y
    /// repara el arma del kit inicial, que para entonces debería tener la durabilidad bajada a
    /// mano por SQL (nada la desgasta todavía de verdad — FASE-07-tiendas.md §1).
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --shops-repair &lt;username&gt; [url]</code>
    /// </summary>
    private static async Task<int> ShopsRepairPhase(string url, GameMap map, string username)
    {
        var bot = new Bot(url, username, "no-hace-falta", map, lagMs: 0);

        try
        {
            Console.WriteLine("· Fase 2: reparar en la armería");
            await bot.ConnectAsync(register: false);
            await Run([bot], 300);

            var armory = bot.KnownEntities.Values.FirstOrDefault(e => e.DefKey == "shop.armory");
            Check("El NPC de la armería sigue visible al reconectar", armory is not null);
            if (armory is null)
            {
                return Summarize();
            }

            bot.WalkTarget = new Vec2(armory.X, armory.Y);
            await Run([bot], 6000);
            Check("El bot llegó de verdad junto al NPC caminando", bot.WalkTarget is null);

            bot.SendNow(Opcode.ShopOpen, new C2SShopOpen { NpcEntityId = armory.Id });
            await Run([bot], 500);
            Check("ShopOpen tras reconectar → ShopData", bot.LastShopData is { ShopKey: "shop.armory" });

            var goldBeforeRepair = bot.Gold;
            bot.LastInventoryChanges.Clear();
            bot.SendNow(Opcode.ShopRepair, new C2SShopRepair { Container = ContainerId.WeaponBag, Slot = 0 });
            await Run([bot], 500);

            var repaired = bot.LastInventoryChanges.GetValueOrDefault((ContainerId.WeaponBag, (byte)0));
            Check($"Reparar restaura la durabilidad al máximo (quedó en {repaired?.Durability})",
                repaired is not null && repaired.Durability == repaired.DurabilityMax);
            Check($"Reparar cobra oro (de {goldBeforeRepair} a {bot.Gold})", bot.Gold < goldBeforeRepair);

            return Summarize();
        }
        finally
        {
            await bot.DisposeAsync();
        }
    }

    /// <summary>
    /// Fase 1 del flujo de granja (FASE-08-granja-cultivos.md §9): comprar azada, regadera y
    /// semilla en el almacén general, caminar a la parcela comunitaria, arar, plantar y regar.
    /// Termina imprimiendo el <c>username</c> para <see cref="FarmHarvestPhase"/>.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --farm-plant [url]</code>
    /// </summary>
    private static async Task<int> FarmPlantPhase(string url, GameMap map)
    {
        // Esquina de la parcela comunitaria sembrada por 0004_farm.sql (origin 6,82).
        var tileX = 6;
        var tileY = 82;
        var run = Guid.NewGuid().ToString("N")[..8];
        var bot = new Bot(url, $"farm_{run}", $"Farm_{run}", map, lagMs: 0);

        try
        {
            Console.WriteLine("· Fase 1: comprar herramientas, arar, plantar y regar");
            await bot.ConnectAsync(register: true);
            await Run([bot], 300);

            var store = bot.KnownEntities.Values.FirstOrDefault(e => e.DefKey == "shop.general_store");
            Check("El NPC del almacén general está visible al entrar", store is not null);
            if (store is null)
            {
                return Summarize();
            }

            bot.WalkTarget = new Vec2(store.X, store.Y);
            await Run([bot], 6000);
            Check("El bot llegó de verdad junto al NPC caminando", bot.WalkTarget is null);

            bot.SendNow(Opcode.ShopOpen, new C2SShopOpen { NpcEntityId = store.Id });
            await Run([bot], 500);
            Check("ShopOpen cerca del NPC → ShopData", bot.LastShopData is { ShopKey: "shop.general_store" });
            if (bot.LastShopData is not { } shopData)
            {
                return Summarize();
            }

            BuyOne(bot, shopData, "item.hoe");
            BuyOne(bot, shopData, "item.watering_can");
            BuyOne(bot, shopData, "item.wheat_seed");
            await Run([bot], 500);

            var hoeSlot = FindBagSlot(bot, "item.hoe");
            var wateringCanSlot = FindBagSlot(bot, "item.watering_can");
            var seedSlot = FindBagSlot(bot, "item.wheat_seed");
            Check("Se compraron azada, regadera y semilla", hoeSlot is not null && wateringCanSlot is not null && seedSlot is not null);
            if (hoeSlot is not { } hoe || wateringCanSlot is not { } wateringCan || seedSlot is not { } seed)
            {
                return Summarize();
            }

            var plotCenter = new Vec2(tileX + 0.5f, tileY + 0.5f);
            bot.WalkTarget = plotCenter;
            await Run([bot], 15000);
            Check("El bot llegó de verdad a la parcela caminando", bot.WalkTarget is null);

            bot.SendNow(Opcode.Equip, new C2SEquip { Container = hoe.Container, Slot = hoe.Slot, EquipSlot = EquipSlot.Tool });
            await Run([bot], 300);

            bot.SendNow(Opcode.FarmTill, new C2SFarmTill { TileX = tileX, TileY = tileY });
            await Run([bot], 500);
            Check("Arar deja el tile arado", bot.LastFarmTiles.GetValueOrDefault((tileX, tileY))?.State == FarmTileStatus.Tilled);

            bot.SendNow(Opcode.FarmPlant, new C2SFarmPlant
            {
                TileX = tileX, TileY = tileY, Container = seed.Container, Slot = seed.Slot,
            });
            await Run([bot], 500);
            var afterPlant = bot.LastFarmTiles.GetValueOrDefault((tileX, tileY));
            Check("Plantar deja el tile plantado con crop.wheat",
                afterPlant is { State: FarmTileStatus.Planted, CropKey: "crop.wheat" });

            bot.SendNow(Opcode.Equip, new C2SEquip
            {
                Container = wateringCan.Container, Slot = wateringCan.Slot, EquipSlot = EquipSlot.Tool,
            });
            await Run([bot], 300);

            bot.SendNow(Opcode.FarmWater, new C2SFarmWater { TileX = tileX, TileY = tileY });
            await Run([bot], 500);
            Check("Regar marca el tile como regado", bot.LastFarmTiles.GetValueOrDefault((tileX, tileY))?.Watered == true);

            Console.WriteLine($"\nusername={bot.Username}");
            Console.WriteLine(
                "\nPara la fase de cosecha: adelanta días de granja por SQL (nada los simula si nadie\n" +
                "mira, FASE-08 §2 D1). Se regó una sola vez (el día de hoy), así que a partir de ahí\n" +
                "el progreso sube 0,5/día, no 1,0: growthNeeded=3 necesita al menos 5 días en total\n" +
                "(1,0 del día regado + 0,5×4) para llegar a Ready — 6 deja margen, igual que el peor\n" +
                "caso 'abandonado' de docs/00 §7. Hace falta reiniciar el servicio para que relea\n" +
                "farm_calendar (el barrido sólo compara contra lo que ya tiene en memoria). P. ej.:\n" +
                "  UPDATE farm_calendar SET last_day_index = last_day_index - 6;\n" +
                "  sudo systemctl restart epimeteo\n");

            return Summarize();
        }
        finally
        {
            await bot.DisposeAsync();
        }
    }

    /// <summary>
    /// Fase 2 del flujo de granja: reconecta con el personaje de <see cref="FarmPlantPhase"/> y
    /// cosecha, tras el barrido diario del servidor real (no simulado por el bot) haber puesto el
    /// tile en <c>Ready</c>.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --farm-harvest &lt;username&gt; [url]</code>
    /// </summary>
    private static async Task<int> FarmHarvestPhase(string url, GameMap map, string username)
    {
        var tileX = 6;
        var tileY = 82;
        var bot = new Bot(url, username, "no-hace-falta", map, lagMs: 0);

        try
        {
            Console.WriteLine("· Fase 2: cosechar tras 3 días de granja");
            await bot.ConnectAsync(register: false);
            await Run([bot], 500);

            var tile = bot.LastFarmTiles.GetValueOrDefault((tileX, tileY));
            Check($"El tile ya está listo tras recuperar los días perdidos (estado: {tile?.State})",
                tile?.State == FarmTileStatus.Ready);

            bot.WalkTarget = new Vec2(tileX + 0.5f, tileY + 0.5f);
            await Run([bot], 15000);
            Check("El bot llegó de verdad a la parcela caminando", bot.WalkTarget is null);

            var goldBefore = bot.Gold;
            bot.LastInventoryChanges.Clear();
            bot.SendNow(Opcode.FarmHarvest, new C2SFarmHarvest { TileX = tileX, TileY = tileY });
            await Run([bot], 500);

            var harvested = bot.LastInventoryChanges.Values.FirstOrDefault(item => item?.DefKey == "item.wheat");
            Check($"Cosechar da trigo de verdad (cantidad {harvested?.Quantity}, calidad {harvested?.Quality})",
                harvested is { Quantity: > 0 });
            Check("Cosechar deja el tile arado, no virgen",
                bot.LastFarmTiles.GetValueOrDefault((tileX, tileY))?.State == FarmTileStatus.Tilled);
            Check("Cosechar no cuesta ni da oro", bot.Gold == goldBefore);

            return Summarize();
        }
        finally
        {
            await bot.DisposeAsync();
        }
    }

    private static void BuyOne(Bot bot, S2CShopData shop, string defKey)
    {
        var slot = Array.FindIndex(shop.Slots, s => s.DefKey == defKey);
        if (slot < 0)
        {
            Check($"La tienda vende '{defKey}'", false);
            return;
        }

        bot.SendNow(Opcode.ShopBuy, new C2SShopBuy { ShopSlot = (byte)slot, Quantity = 1, ExpectedPrice = shop.Slots[slot].PriceBuy });
    }

    private static (ContainerId Container, byte Slot)? FindBagSlot(Bot bot, string defKey)
    {
        foreach (var (key, item) in bot.LastInventoryChanges)
        {
            if (item?.DefKey == defKey)
            {
                return key;
            }
        }

        return null;
    }

    private static int Summarize()
    {
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

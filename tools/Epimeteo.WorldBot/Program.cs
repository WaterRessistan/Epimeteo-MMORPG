using System.Diagnostics;
using System.Text.Json;
using Epimeteo.Server.Security;
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

        // Flujo de PvP de la Fase 9 (FASE-09-combate-pvp.md §9): dos bots de verdad pegándose,
        // con el criterio de aceptación de docs/03 (no se puede atacar desde el borde de la plaza).
        if (args.Contains("--pvp"))
        {
            return await PvpPhase(url, map, lagMs);
        }

        // Flujo de progresión de la Fase 10 (FASE-10-progresion.md §9): a diferencia de
        // tiendas/granja, el nivel y la XP los produce el propio protocolo matando monstruos de
        // verdad, sin manipular nada por SQL — la única manipulación externa entre las dos
        // corridas es un reinicio real del servicio, para probar que sobrevive de verdad.
        if (args.Contains("--progression-grind"))
        {
            return await ProgressionGrindPhase(url, map);
        }

        if (args.Contains("--progression-verify"))
        {
            var username = Arg(args, "--progression-verify")
                ?? throw new InvalidOperationException("--progression-verify necesita el username de --progression-grind.");
            return await ProgressionVerifyPhase(url, map, username);
        }

        // Flujo de anticheat de la Fase 13 (FASE-13-observabilidad-anticheat.md §8): un bot que
        // insiste en algo inválido y otro que juega normal, para comprobar que la escalada
        // distingue a uno de otro. Una sola corrida: no hace falta manipular nada por SQL.
        if (args.Contains("--anticheat"))
        {
            return await AnticheatPhase(url, map);
        }

        // Flujo de chat de la Fase 11 (FASE-11-chat-social.md §9): los comandos de admin exigen
        // is_admin, y ninguna cuenta puede autopromocionarse (D6, a propósito) — hace falta la
        // misma manipulación manual por SQL entre dos corridas que tiendas/granja, sólo que aquí
        // es conceder el rol, no adelantar un reloj ni bajar una durabilidad.
        if (args.Contains("--chat-setup"))
        {
            return await ChatSetupPhase(url, map);
        }

        if (args.Contains("--chat-verify"))
        {
            var usernames = (Arg(args, "--chat-verify")
                ?? throw new InvalidOperationException("--chat-verify necesita \"admin,a,b\" de --chat-setup.")).Split(',');
            if (usernames.Length != 3)
            {
                throw new InvalidOperationException("--chat-verify necesita los tres usernames que imprimió --chat-setup: \"admin,a,b\".");
            }

            return await ChatVerifyPhase(url, map, usernames[0], usernames[1], usernames[2]);
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


    /// <summary>
    /// Verificación de PvP y combate (FASE-09-combate-pvp.md §9). Dos bots de verdad: uno pega y
    /// el otro encaja, en el campo y en la plaza, y con el caso del borde que pide el criterio de
    /// aceptación de <c>docs/03</c>.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --pvp [url] [--lag-ms 150]</code>
    /// </summary>
    private static async Task<int> PvpPhase(string url, GameMap map, int lagMs)
    {
        // La muralla del pueblo (y = 48) tiene una sola puerta, en x = 48: cualquier ruta entre la
        // plaza y campo_norte pasa por ahí, así que se va por waypoints y no en línea recta.
        var puertaSur = new Vec2(48.5f, 50.5f);
        var puertaNorte = new Vec2(48.5f, 46.5f);
        var campoA = new Vec2(47.6f, 43.5f);
        var campoB = new Vec2(48.4f, 43.5f);
        var plaza = new Vec2(48.5f, 60.5f);

        var run = Guid.NewGuid().ToString("N")[..8];
        var a = new Bot(url, $"pvp_{run}_a", $"PvpA_{run}", map, lagMs);
        var b = new Bot(url, $"pvp_{run}_b", $"PvpB_{run}", map, lagMs);
        var flota = new List<Bot> { a, b };

        try
        {
            Console.WriteLine($"· PvP con {lagMs} ms de latencia simulada por sentido");
            await a.ConnectAsync(register: true);
            await b.ConnectAsync(register: true);
            await Run(flota, 500);

            // ── 1. Los dos en zona PvP: pegar funciona ────────────────────
            await Rutas(flota, (a, [puertaSur, puertaNorte, campoA]), (b, [puertaSur, puertaNorte, campoB]));
            Check($"Los dos cruzaron a campo_norte (A en {a.ServerPos.Y:F1}, B en {b.ServerPos.Y:F1})",
                a.ServerPos.Y < 48 && b.ServerPos.Y < 48);
            Check("El servidor dice que la región de B es hostil",
                b.ZoneUpdates.Count > 0 && b.ZoneUpdates[^1].Flags.HasFlag(ZoneFlags.Pvp));

            var vidaAntes = b.EntityHp.GetValueOrDefault(b.MyEntityId).Hp;
            a.ResetPhase();
            b.ResetPhase();
            a.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = b.MyEntityId });
            await Run(flota, 800);

            var vidaDespues = b.EntityHp.GetValueOrDefault(b.MyEntityId).Hp;
            Check($"En zona PvP el golpe entra (vida de B: {vidaAntes} → {vidaDespues})", vidaDespues < vidaAntes);
            Check("El atacante recibe el CombatEvent", a.CombatEvents.Count > 0);
            Check("Los dos quedan marcados en combate", a.InCombat && b.InCombat);

            // ── 2. Matarlo de verdad: respawn, penalización y combat_log ──
            // B tiene que tener algo de XP para que la penalización sea observable, así que se le
            // deja matar un monstruo antes (5 % de 0 seguiría siendo 0).
            var monstruoParaB = b.KnownEntities.Values.FirstOrDefault(e => e.Type == EntityType.Monster);
            if (monstruoParaB is not null)
            {
                await Rutas(flota, 25_000, (b, [new Vec2(monstruoParaB.X, monstruoParaB.Y)]), (a, []));
                b.ResetPhase();

                for (var golpe = 0; golpe < 40 && !b.Deaths.Any(d => d.Id == monstruoParaB.Id); golpe++)
                {
                    b.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = monstruoParaB.Id });
                    await Run(flota, 900);
                }
            }

            Check($"B tiene experiencia que perder ({b.Xp})", b.Xp > 0);
            var xpDeBAntesDeMorir = b.Xp;

            // Los dos otra vez juntos en campo abierto, y A remata.
            await Rutas(flota, (a, [campoA]), (b, [campoB]));
            a.ResetPhase();
            b.ResetPhase();

            for (var golpe = 0; golpe < 60 && !a.Deaths.Any(d => d.Id == b.MyEntityId); golpe++)
            {
                a.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = b.MyEntityId });
                await Run(flota, 850);
            }

            Check("A consigue matar a B en zona PvP", a.Deaths.Any(d => d.Id == b.MyEntityId));
            // La penalización es el 5 % de la XP actual, en entero: con la XP de un monstruo (8)
            // el 5 % es 0,4 y se trunca a 0. No es un fallo — protege a quien acaba de empezar —,
            // así que lo que se comprueba es que nunca sube. El descuento con XP alta lo cubre el
            // test unitario, que puede fijar el número exacto.
            Check($"Morir en PvP nunca sube la experiencia ({xpDeBAntesDeMorir} → {b.Xp})", b.Xp <= xpDeBAntesDeMorir);
            await Run(flota, 600);
            Check($"B reaparece en el pueblo (y = {b.ServerPos.Y:F1})", b.ServerPos.Y > 52);
            Check($"B reaparece con vida ({b.EntityHp.GetValueOrDefault(b.MyEntityId).Hp})",
                b.EntityHp.GetValueOrDefault(b.MyEntityId).Hp > 0);

            // ── 3. El criterio de aceptación: atacar desde el borde ───────
            // A se queda en el último tile seguro (dentro de la puerta) y B justo al otro lado,
            // a 1,3 tiles: dentro del alcance y con la puerta abierta entre medias, así que lo
            // único que puede rechazar el golpe es la zona.
            await Rutas(flota, (a, [puertaNorte, new Vec2(48.5f, 49.6f)]), (b, [new Vec2(48.5f, 47.4f)]));

            // Por región y no por coordenada: entre la muralla (y = 48) y el pueblo (y ≥ 49) hay
            // una banda que no pertenece a ninguna región declarada, y el margen de llegada del
            // caminante del bot (0,3 tiles) puede dejarlo justo ahí.
            var zonaA = map.Regions.Resolve(a.ServerPos);
            var zonaB = map.Regions.Resolve(b.ServerPos);
            Check($"A está en zona segura ({zonaA.Name}) y B en zona hostil ({zonaB.Name})",
                zonaA.Flags.HasFlag(ZoneFlags.Safe) && zonaB.Flags.HasFlag(ZoneFlags.Pvp));

            var vidaBorde = b.EntityHp.GetValueOrDefault(b.MyEntityId).Hp;
            a.ResetPhase();
            b.ResetPhase();
            a.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = b.MyEntityId });
            await Run(flota, 800);

            Check($"Desde el borde de la plaza no se pega al de fuera (vida de B sigue en {vidaBorde})",
                b.EntityHp.GetValueOrDefault(b.MyEntityId).Hp == vidaBorde && a.CombatEvents.Count == 0);
            Check("...y el motivo es la zona segura del atacante",
                a.SystemMessages.Any(m => m.Key == "combat.SafeZone"));

            // ── 4. La víctima se refugia en la plaza ──────────────────────
            await Rutas(flota, (b, [puertaNorte, puertaSur, plaza]), (a, []));
            Check($"B se refugió en la plaza (y = {b.ServerPos.Y:F1})", b.ServerPos.Y > 52);

            await Rutas(flota, (a, [new Vec2(plaza.X + 0.8f, plaza.Y)]), (b, []));
            var vidaEnPlaza = b.EntityHp.GetValueOrDefault(b.MyEntityId).Hp;
            a.ResetPhase();
            b.ResetPhase();
            a.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = b.MyEntityId });
            await Run(flota, 800);

            Check("Dentro de la plaza no se pega",
                b.EntityHp.GetValueOrDefault(b.MyEntityId).Hp == vidaEnPlaza && a.CombatEvents.Count == 0);
            Check("...y el atacante recibe el motivo",
                a.SystemMessages.Any(m => m.Key is "combat.SafeZone" or "combat.TargetInSafeZone"));

            // ── 5. Monstruos: existen, se les pega y sueltan botín ────────
            a.ResetPhase();
            await Rutas(flota, (a, [puertaSur, puertaNorte, campoA]), (b, []));
            await Run(flota, 2000);

            var monstruo = a.KnownEntities.Values.FirstOrDefault(e => e.Type == EntityType.Monster);
            Check("Hay monstruos en el campo", monstruo is not null);

            if (monstruo is not null)
            {
                await Rutas(flota, 25_000, (a, [new Vec2(monstruo.X, monstruo.Y)]), (b, []));

                var xpAntes = a.Xp;
                a.ResetPhase();

                for (var golpe = 0; golpe < 40 && !a.Deaths.Any(d => d.Id == monstruo.Id); golpe++)
                {
                    a.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = monstruo.Id });
                    await Run(flota, 900);
                }

                Check("Se puede pegar a un monstruo en cualquier zona con monstruos",
                    a.CombatEvents.Any(e => e.TargetId == monstruo.Id));
                Check("El monstruo acaba muriendo", a.Deaths.Any(d => d.Id == monstruo.Id));
                Check($"Matarlo da experiencia ({xpAntes} → {a.Xp})", a.Xp > xpAntes);

                if (a.LootBags.Count > 0)
                {
                    var saco = a.LootBags.Values.First();
                    a.LastInventoryChanges.Clear();
                    a.SystemMessages.Clear();
                    a.SendNow(Opcode.LootTake, new C2SLootTake { LootEntityId = saco.EntityId, Slot = 0 });
                    await Run(flota, 800);

                    var motivo = a.SystemMessages.Count > 0 ? $" (dijo {a.SystemMessages[^1].Key})" : string.Empty;
                    Check($"Coger del saco mete el ítem en la bolsa{motivo}", a.LastInventoryChanges.Count > 0);
                }
                else
                {
                    Console.WriteLine("  [ -- ] El monstruo no soltó botín esta vez (la tabla es probabilística)");
                }
            }

            // ── 6. Flag de combate: salir en combate no saca del mundo ────
            // docs/00 §6.2 / FASE-09 §2 D11: si "me van a matar" se resolviera con Alt+F4, el PvP
            // no valdría de nada. B se desconecta recién golpeado y su entidad tiene que seguir en
            // el mundo — lo comprueba A, que la sigue viendo.
            await Rutas(flota, (a, [puertaSur, puertaNorte, campoA]), (b, [puertaSur, puertaNorte, campoB]));
            a.ResetPhase();
            b.ResetPhase();
            a.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = b.MyEntityId });
            await Run(flota, 800);

            Check("B queda en combate justo antes de desconectar", b.InCombat);

            var entidadDeB = b.MyEntityId;
            await b.DisconnectAsync();
            await Run([a], 2500);

            Check("Tras desconectar en combate, la entidad de B sigue en el mundo",
                !a.Despawned.Any(d => d.Id == entidadDeB));

            Check("Ningún bot fue expulsado", a.Kicked is null && b.Kicked is null);

            Console.WriteLine($"\nusername A = {a.Username} (personaje {a.CharacterId})");
            Console.WriteLine($"username B = {b.Username} (personaje {b.CharacterId})");

            return Summarize();
        }
        finally
        {
            await a.DisposeAsync();
            await b.DisposeAsync();
        }
    }

    // Números de class.warrior (content/classes/warrior.json), para comprobar de verdad que el
    // máximo escala con la fórmula de la Fase 10 (D3) y no un número cualquiera.
    private const int WarriorBaseHp = 120;
    private const int WarriorHpPerLevel = 14;
    private const int WarriorBaseMp = 20;
    private const int WarriorMpPerLevel = 3;
    private const int WarriorBaseVit = 6;

    /// <summary>
    /// Fase 1 del flujo de progresión (FASE-10-progresion.md §9): una habilidad bloqueada por
    /// nivel se rechaza, una desbloqueada gasta maná de verdad y tiene su propio cooldown (sin
    /// bloquear el ataque básico, D7), matar monstruos sube de nivel de verdad y reparte los
    /// puntos que tocan. Termina imprimiendo el <c>username</c> para <see cref="ProgressionVerifyPhase"/>,
    /// que comprueba que todo sobrevive a un reinicio real del servicio.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --progression-grind [url]</code>
    /// </summary>
    private static async Task<int> ProgressionGrindPhase(string url, GameMap map)
    {
        var puertaSur = new Vec2(48.5f, 50.5f);
        var puertaNorte = new Vec2(48.5f, 46.5f);
        var campo = new Vec2(48.5f, 44.5f);

        var run = Guid.NewGuid().ToString("N")[..8];
        var bot = new Bot(url, $"prog_{run}", $"Prog_{run}", map, lagMs: 0);
        var flota = new List<Bot> { bot };

        try
        {
            Console.WriteLine("· Fase 1: subir de nivel, repartir puntos y lanzar una habilidad");
            await bot.ConnectAsync(register: true);
            await Run(flota, 500);

            Check($"Empieza en nivel 1 sin experiencia ni puntos (Nv {bot.Level}, XP {bot.Xp}, pts {bot.StatPoints})",
                bot.Level == 1 && bot.Xp == 0 && bot.StatPoints == 0);

            // ── 1. Habilidad de otro nivel: se rechaza sin tocar nada ─────
            bot.SystemMessages.Clear();
            bot.SendNow(Opcode.SkillCast, new C2SSkillCast { SkillKey = "skill.warrior_cleave", TargetEntityId = 0 });
            await Run(flota, 500);
            Check("skill.warrior_cleave (nivel 4) con nivel 1 → SkillNotUnlocked",
                bot.SystemMessages.Any(m => m.Key == "combat.SkillNotUnlocked"));

            // ── 2. Cruzar al campo: hay monstruos de sobra para probar la habilidad ─
            await Rutas(flota, (bot, [puertaSur, puertaNorte, campo]));
            await Run(flota, 1500);
            Check("Hay un monstruo cerca para probar la habilidad", NearestMonster(bot) is not null);

            // ── 3. Golpe Poderoso: daño de verdad, cooldown propio, maná real ──
            // El objetivo vivo más cercano puede morir o perderse de vista entre un intento y el
            // siguiente (el campo tiene varios monstruos moviéndose y pegando de vuelta) — por eso
            // cada intento vuelve a elegir el más cercano en vez de aferrarse a uno fijo.
            // OnCooldown y NotEnoughMana los decide SkillSystem.ValidateCast antes incluso de
            // mirar el objetivo (nivel → maná → cooldown, y sólo entonces alcance/zona), así que
            // esas dos comprobaciones se mandan con objetivo 0 a propósito: no dependen de que
            // nada siga vivo. Entre medias se sigue atacando (cooldown propio, D7) en vez de
            // esperar de brazos cruzados encajando golpes gratis.
            var primerGolpe = false;
            for (var intento = 0; intento < 6 && !primerGolpe; intento++)
            {
                primerGolpe = await CastSkillOnNearest(flota, bot, "skill.warrior_power_strike");
                if (!primerGolpe)
                {
                    await Run(flota, 500);
                }
            }

            Check("Golpe Poderoso hace daño de verdad", primerGolpe);

            bot.SystemMessages.Clear();
            bot.SendNow(Opcode.SkillCast, new C2SSkillCast { SkillKey = "skill.warrior_power_strike", TargetEntityId = 0 });
            await Run(flota, 300);
            Check("Repetir enseguida → OnCooldown (cooldown propio, no el de Attack)",
                bot.SystemMessages.Any(m => m.Key == "combat.OnCooldown"));

            var cercanoParaAtacar = NearestMonster(bot);
            bot.CombatEvents.Clear();
            if (cercanoParaAtacar is not null)
            {
                bot.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = cercanoParaAtacar.Id });
            }

            await Run(flota, 800);
            Check("...pero el ataque básico entra igual: no comparten cooldown",
                bot.CombatEvents.Any(e => e.SkillKey is null));

            await AttackWhileWaitingNearest(flota, bot, 3300); // se cumple el cooldown de la habilidad (3000 ms) sin quedarse quieto

            var segundoGolpe = false;
            for (var intento = 0; intento < 6 && !segundoGolpe; intento++)
            {
                segundoGolpe = await CastSkillOnNearest(flota, bot, "skill.warrior_power_strike");
                if (!segundoGolpe)
                {
                    await Run(flota, 500);
                }
            }

            Check("Segundo Golpe Poderoso, ya sin cooldown, vuelve a entrar", segundoGolpe);

            // A partir de aquí no hace falta saber la aritmética exacta del maná (algún intento de
            // arriba pudo fallar por posición sin llegar a gastarlo — eso lo fija con estado
            // controlado SkillSystemTests): se sigue lanzando, esperando el cooldown entre medias,
            // hasta ver de verdad el rechazo por maná.
            var sinMana = false;
            for (var intento = 0; intento < 6 && !sinMana; intento++)
            {
                await AttackWhileWaitingNearest(flota, bot, 3300);
                bot.SystemMessages.Clear();
                var exito = await CastSkillOnNearest(flota, bot, "skill.warrior_power_strike");
                sinMana = !exito && bot.SystemMessages.Any(m => m.Key == "combat.NotEnoughMana");
            }

            Check("Lanzarla lo bastante seguido acaba topando con el maná (NotEnoughMana)", sinMana);

            // ── 4. Subir de nivel matando de verdad, sin tocar SQL ────────
            // Los lobos del campo están lejos de los limos (map.village.json §spawns): el
            // objetivo de la prueba de arriba se eligió por vida de sobra, no por cercanía a un
            // punto de reaparición denso. Para el grind de verdad conviene plantarse junto a un
            // punto de limos (respawn cada 30 s, dos a la vez) en vez de perseguir lo que ya se
            // conocía del lobo.
            //
            // Entre los dos hay un muro de un tile (x = 56, y = 18-29: map.village.json,
            // collision) que separa la zona de los lobos de la de los limos, sin hueco visible en
            // ese tramo — el caminante ingenuo se queda pegado a él si se manda en línea recta
            // desde donde haya acabado la prueba de la habilidad. Volver a pasar por la entrada
            // del campo (x = 48,5, ya al oeste del muro) evita el problema sin tener que buscarle
            // el hueco.
            var puntoDeCaza = new Vec2(20.5f, 20.5f);
            var rutaDesdeElPueblo = new[] { puertaSur, puertaNorte, puntoDeCaza };
            await Rutas(flota, 25_000, (bot, [campo, puntoDeCaza]));
            await Run(flota, 1500); // deja que el área de interés traiga los limos de ese punto
            await GrindForLevelUp(flota, bot, rutaDesdeElPueblo, maxKills: 60);
            Check($"Sube de nivel de verdad matando monstruos (Nv {bot.Level}, XP {bot.Xp})", bot.Level >= 2);
            if (bot.Level < 2)
            {
                return Summarize();
            }

            Check("El XpUpdate de la subida trae LeveledUp", bot.XpUpdates.Any(u => u.LeveledUp));

            var hpMaxEsperado = WarriorBaseHp + (WarriorHpPerLevel * (bot.Level - 1));
            var mpMaxEsperado = WarriorBaseMp + (WarriorMpPerLevel * (bot.Level - 1));
            var (hpTrasSubir, hpMaxTrasSubir) = bot.EntityHp.GetValueOrDefault(bot.MyEntityId);
            Check($"El HP máximo escala con el nivel ({hpMaxTrasSubir} == {hpMaxEsperado})", hpMaxTrasSubir == hpMaxEsperado);
            // Límite honesto (mismo criterio que FASE-09 §11): LevelingSystem.GrantXp cura del
            // todo en el mismo tick que sube de nivel —eso lo fija exacto LevelingSystemTests, con
            // estado controlado—, pero aquí sigue habiendo un campo lleno de monstruos que pueden
            // aterrizar un golpe en los ticks que tarda este mensaje en volver. Un margen del 10 %
            // separa "curó del todo y luego le pegaron" de "no curó".
            Check($"Subir de nivel cura del todo (HP {hpTrasSubir}/{hpMaxTrasSubir}, margen 10 % por combate de fondo)",
                hpTrasSubir >= hpMaxTrasSubir * 0.9);
            Check($"El MP máximo también escala ({bot.MpMax} == {mpMaxEsperado})", bot.MpMax == mpMaxEsperado);
            Check($"Concede los puntos de stat de la fase ({bot.StatPoints} pts)",
                bot.StatPoints == ProgressionConstants.StatPointsPerLevel);

            // ── 5. Repartir los puntos: sube el stat, baja lo que queda ───
            var vitAntes = bot.LastEquipmentUpdate?.VitEffective ?? WarriorBaseVit;
            var puntosIniciales = bot.StatPoints;

            for (var i = 0; i < puntosIniciales; i++)
            {
                var vitPrevia = bot.LastEquipmentUpdate?.VitEffective ?? WarriorBaseVit;
                var puntosPrevios = bot.StatPoints;
                bot.SendNow(Opcode.AllocateStatPoint, new C2SAllocateStatPoint { Stat = StatKind.Vit });
                await Run(flota, 400);

                Check($"Punto {i + 1}: VIT sube de {vitPrevia} a {bot.LastEquipmentUpdate?.VitEffective}",
                    bot.LastEquipmentUpdate?.VitEffective == vitPrevia + 1);
                Check($"Punto {i + 1}: quedan {bot.StatPoints} sin gastar (de {puntosPrevios})",
                    bot.StatPoints == puntosPrevios - 1);
            }

            Check($"Los {puntosIniciales} puntos de la subida fueron a VIT ({vitAntes} → {bot.LastEquipmentUpdate?.VitEffective})",
                bot.LastEquipmentUpdate?.VitEffective == vitAntes + puntosIniciales);
            Check("Sin puntos que quedaran, se acaban gastando todos", bot.StatPoints == 0);

            bot.SystemMessages.Clear();
            bot.SendNow(Opcode.AllocateStatPoint, new C2SAllocateStatPoint { Stat = StatKind.Str });
            await Run(flota, 400);
            Check("Sin puntos sin gastar, repartir uno más se rechaza",
                bot.SystemMessages.Any(m => m.Key == "progression.NoStatPointsAvailable") && bot.StatPoints == 0);

            Console.WriteLine($"\nusername={bot.Username}");
            Console.WriteLine($"characterId={bot.CharacterId}");
            Console.WriteLine(
                "\nPara comprobar que nivel, XP, stats y maná sobreviven de verdad (no sólo al\n" +
                "vaciado de la cola de guardado): reinicia el servicio y reconecta con el mismo\n" +
                "personaje.\n" +
                "  sudo systemctl restart epimeteo\n" +
                $"  dotnet run --project tools/Epimeteo.WorldBot -- --progression-verify {bot.Username}\n");

            return Summarize();
        }
        finally
        {
            await bot.DisposeAsync();
        }
    }

    /// <summary>
    /// Fase 2 del flujo de progresión: reconecta con el personaje de <see cref="ProgressionGrindPhase"/>
    /// tras un reinicio real del servicio y comprueba que nivel, XP, stats base y maná sobrevivieron
    /// — el criterio de aceptación de FASE-10-progresion.md §10.5.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --progression-verify &lt;username&gt; [url]</code>
    /// </summary>
    private static async Task<int> ProgressionVerifyPhase(string url, GameMap map, string username)
    {
        var bot = new Bot(url, username, "no-hace-falta", map, lagMs: 0);

        try
        {
            Console.WriteLine("· Fase 2: nivel, stats y maná tras el reinicio");
            await bot.ConnectAsync(register: false);
            await Run([bot], 500);

            var enter = bot.WorldEnter ?? throw new InvalidOperationException($"{username}: sin WorldEnter.");

            Check($"El nivel sobrevive al reinicio (Nv {enter.Stats.Level})", enter.Stats.Level >= 2);

            var hpMaxEsperado = WarriorBaseHp + (WarriorHpPerLevel * (enter.Stats.Level - 1));
            var mpMaxEsperado = WarriorBaseMp + (WarriorMpPerLevel * (enter.Stats.Level - 1));
            Check($"El maná máximo del nivel persistido cuadra ({bot.MpMax} == {mpMaxEsperado})", bot.MpMax == mpMaxEsperado);
            Check($"El maná guardado llegó lleno ({enter.Stats.Mp}/{mpMaxEsperado})", enter.Stats.Mp == mpMaxEsperado);
            // El maná se puede afirmar lleno porque nada lo gasta entre la subida y la
            // desconexión (repartir puntos no cuesta maná). El HP no: el campo seguía lleno de
            // monstruos mientras se repartían los puntos, así que sólo se afirma que sobrevivió
            // con un valor sano — el "cura del todo" exacto ya lo prueba LevelingSystemTests con
            // estado controlado (mismo límite honesto que el HP recién curado, más arriba).
            Check($"El HP guardado sobrevivió con un valor sano ({enter.Stats.Hp}/{hpMaxEsperado})",
                enter.Stats.Hp > 0 && enter.Stats.Hp <= hpMaxEsperado);

            Check($"Los puntos de stat se gastaron todos y siguen en 0 tras el reinicio ({enter.Stats.StatPoints})",
                enter.Stats.StatPoints == 0);
            Check($"VIT quedó en {WarriorBaseVit + ProgressionConstants.StatPointsPerLevel} " +
                $"(base {WarriorBaseVit} + los {ProgressionConstants.StatPointsPerLevel} puntos de la fase 1, VIT real {enter.Stats.Vit})",
                enter.Stats.Vit == WarriorBaseVit + ProgressionConstants.StatPointsPerLevel);
            Check($"La XP sigue por debajo del siguiente nivel, sin negativos (XP {enter.Stats.Xp})",
                enter.Stats.Xp >= 0 && enter.Stats.Xp < LevelingFormulas.XpRequiredForNextLevel(enter.Stats.Level));

            return Summarize();
        }
        finally
        {
            await bot.DisposeAsync();
        }
    }

    /// <summary>
    /// Fase 1 del flujo de chat (FASE-11-chat-social.md §9): tres bots — dos normales y uno que se
    /// promocionará a admin por SQL entre las dos corridas, porque ninguna cuenta puede
    /// autopromocionarse (D6, a propósito). Prueba todo lo que no necesita ser admin: los dos
    /// canales, el susurro, <c>/who</c>, <c>/help</c>, y que un comando de admin se rechaza sin
    /// serlo.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --chat-setup [url]</code>
    /// </summary>
    private static async Task<int> ChatSetupPhase(string url, GameMap map)
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var a = new Bot(url, $"chat_{run}_a", $"ChatA_{run}", map, lagMs: 0);
        var b = new Bot(url, $"chat_{run}_b", $"ChatB_{run}", map, lagMs: 0);
        var admin = new Bot(url, $"chat_{run}_admin", $"ChatAdmin_{run}", map, lagMs: 0);
        var flota = new List<Bot> { a, b, admin };

        try
        {
            Console.WriteLine("· Fase 1: canales, susurro, who/help y rechazo sin ser admin");
            await a.ConnectAsync(register: true);
            await b.ConnectAsync(register: true);
            await admin.ConnectAsync(register: true);
            await Run(flota, 500);

            // ── 1. Canal global: le llega a todos ──────────────────────
            a.SendNow(Opcode.ChatSend, new C2SChatSend { Channel = ChatChannel.Global, Text = "hola a todos" });
            await Run(flota, 500);

            var globalToB = b.ChatMessages.SingleOrDefault(m => m.Text == "hola a todos");
            Check("Un mensaje global le llega a otro jugador", globalToB is { Channel: ChatChannel.Global });
            Check("...con el nombre de quien lo mandó", globalToB?.SenderName == a.Name);

            // ── 2. Canal de zona: también le llega (mismo mapa) ────────
            b.ChatMessages.Clear();
            a.SendNow(Opcode.ChatSend, new C2SChatSend { Channel = ChatChannel.Zone, Text = "hola zona" });
            await Run(flota, 500);

            var zoneToB = b.ChatMessages.SingleOrDefault(m => m.Text == "hola zona");
            Check("Un mensaje de zona le llega a quien está en la misma zona", zoneToB is { Channel: ChatChannel.Zone });

            // ── 3. Susurro: sólo al destinatario, con eco al que lo manda ──
            a.ChatMessages.Clear();
            b.ChatMessages.Clear();
            admin.ChatMessages.Clear();
            a.SendNow(Opcode.ChatSend, new C2SChatSend { Channel = ChatChannel.Global, Text = $"/w {b.Name} esto es privado" });
            await Run(flota, 500);

            var whisperToB = b.ChatMessages.SingleOrDefault();
            Check("Un susurro le llega al destinatario", whisperToB is { Channel: ChatChannel.Whisper, Text: "esto es privado" });
            Check("...y de vuelta a quien lo mandó, como eco", a.ChatMessages.Any(m => m.Channel == ChatChannel.Whisper));
            Check("...pero no a un tercero", admin.ChatMessages.Count == 0);

            // ── 4. Susurro a un nombre inexistente se rechaza ──────────
            a.SystemMessages.Clear();
            a.SendNow(Opcode.ChatSend, new C2SChatSend { Channel = ChatChannel.Global, Text = "/w Fantasma hola" });
            await Run(flota, 500);
            Check("Susurrar a quien no existe se rechaza", a.SystemMessages.Any(m => m.Key == $"chat.{ResultCode.TargetNotFound}"));

            // ── 5. /who y /help ─────────────────────────────────────────
            a.SystemMessages.Clear();
            a.SendNow(Opcode.ChatSend, new C2SChatSend { Channel = ChatChannel.Global, Text = "/who" });
            a.SendNow(Opcode.ChatSend, new C2SChatSend { Channel = ChatChannel.Global, Text = "/help" });
            await Run(flota, 500);

            var who = a.SystemMessages.FirstOrDefault(m => m.Key == "chat.who");
            Check("/who lista a los conectados",
                who is not null && who.Args.Contains(a.Name) && who.Args.Contains(b.Name));
            Check("/help lista los comandos", a.SystemMessages.Any(m => m.Key == "chat.help" && m.Args.Length > 0));

            // ── 6. Comando de admin sin ser admin se rechaza ───────────
            a.SystemMessages.Clear();
            a.SendNow(Opcode.ChatSend, new C2SChatSend { Channel = ChatChannel.Global, Text = $"/kick {b.Name}" });
            await Run(flota, 500);
            Check("Un comando de admin sin serlo se rechaza", a.SystemMessages.Any(m => m.Key == $"chat.{ResultCode.NotAuthorized}"));
            Check("...y no expulsa a nadie", !b.Kicked.HasValue);

            Console.WriteLine($"\nusername de admin={admin.Username}");
            Console.WriteLine(
                "\nPara la fase de administración: concede el rol por SQL —ninguna cuenta puede\n" +
                "autopromocionarse (FASE-11 §2 D6)— y reconecta con --chat-verify, reutilizando\n" +
                "estas dos cuentas como objetivo de /teleport, /give y /kick (así la fase 2 sólo\n" +
                "necesita registrar una cuenta nueva, la de /ban, y no vuelve a topar con el cupo\n" +
                "de registros por IP que ya ha gastado esta fase 1).\n" +
                $"  UPDATE accounts SET is_admin = true WHERE username = '{admin.Username}';\n" +
                $"  dotnet run --project tools/Epimeteo.WorldBot -- --chat-verify {admin.Username},{a.Username},{b.Username}\n");

            return Summarize();
        }
        finally
        {
            await a.DisposeAsync();
            await b.DisposeAsync();
            await admin.DisposeAsync();
        }
    }

    /// <summary>
    /// Fase 2 del flujo de chat: reconecta con la cuenta ya promocionada a admin y con las dos de
    /// <see cref="ChatSetupPhase"/> (objetivo de <c>/teleport</c>, <c>/give</c> y <c>/kick</c>) y
    /// prueba los cuatro comandos de administrador. Sólo registra una cuenta nueva, la de
    /// <c>/ban</c> —y la primera, antes de que nadie más conecte—: si el cupo de registros por IP
    /// obliga a esperar (<c>AuthService</c>, 5/minuto), ningún otro bot se queda callado el
    /// tiempo suficiente para que el barrido de inactividad (30 s) lo desconecte solo.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --chat-verify admin,a,b [url]</code>
    /// </summary>
    private static async Task<int> ChatVerifyPhase(string url, GameMap map, string adminUsername, string aUsername, string bUsername)
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var forBan = new Bot(url, $"chat_{run}_ban", $"ChatBan_{run}", map, lagMs: 0);

        try
        {
            Console.WriteLine("· Fase 2: comandos de administrador");
            await forBan.ConnectAsync(register: true);

            var admin = new Bot(url, adminUsername, "no-hace-falta", map, lagMs: 0);
            var forTeleportAndGive = new Bot(url, aUsername, "no-hace-falta", map, lagMs: 0);
            var forKick = new Bot(url, bUsername, "no-hace-falta", map, lagMs: 0);
            var flota = new List<Bot> { admin, forTeleportAndGive, forKick, forBan };

            try
            {
                await admin.ConnectAsync(register: false);
                await forTeleportAndGive.ConnectAsync(register: false);
                await forKick.ConnectAsync(register: false);
                await Run(flota, 500);

                // ── 1. /teleport: mueve al admin junto al objetivo (D8) ────
                admin.SendNow(Opcode.ChatSend, new C2SChatSend
                {
                    Channel = ChatChannel.Global, Text = $"/teleport {forTeleportAndGive.Name}",
                });
                await Run(flota, 800);

                var distancia = MathF.Sqrt(Vec2.DistanceSquared(admin.ServerPos, forTeleportAndGive.ServerPos));
                Check($"/teleport mueve al admin junto al objetivo ({distancia:F2} tiles)", distancia < 0.5f);

                // ── 2. /give: mete un ítem de verdad en la bolsa del objetivo ──
                forTeleportAndGive.LastInventoryChanges.Clear();
                admin.SendNow(Opcode.ChatSend, new C2SChatSend
                {
                    Channel = ChatChannel.Global, Text = $"/give {forTeleportAndGive.Name} item.health_potion 2",
                });
                await Run(flota, 800);

                var given = forTeleportAndGive.LastInventoryChanges.Values.FirstOrDefault(item => item?.DefKey == "item.health_potion");
                Check($"/give mete el ítem de verdad en la bolsa del objetivo (cantidad {given?.Quantity})", given is { Quantity: >= 2 });

                // ── 3. /kick: expulsa sin banear ────────────────────────────
                admin.SendNow(Opcode.ChatSend, new C2SChatSend
                {
                    Channel = ChatChannel.Global, Text = $"/kick {forKick.Name} de prueba",
                });
                await Run(flota, 800);
                Check($"/kick expulsa al objetivo ({forKick.Kicked})", forKick.Kicked == KickReason.Banned);

                // ── 4. /ban: expulsa y deja la cuenta baneada de verdad ─────
                admin.SendNow(Opcode.ChatSend, new C2SChatSend
                {
                    Channel = ChatChannel.Global, Text = $"/ban {forBan.Name} 1 de prueba",
                });
                await Run(flota, 800);
                Check($"/ban expulsa al objetivo ({forBan.Kicked})", forBan.Kicked == KickReason.Banned);

                await Run(flota, 500); // deja que el cierre del socket de forBan termine de verdad

                var reconectando = new Bot(url, forBan.Username, "no-hace-falta", map, lagMs: 0);
                var seRechazo = false;
                try
                {
                    await reconectando.ConnectAsync(register: false);
                }
                catch (InvalidOperationException)
                {
                    seRechazo = true;
                }
                finally
                {
                    await reconectando.DisposeAsync();
                }

                Check("Tras el ban, la cuenta no puede volver a entrar", seRechazo);

                Console.WriteLine(
                    "\nPara comprobar la auditoría (los cuatro comandos de arriba dejaron fila):\n" +
                    "  SELECT action, admin_name, target_name FROM admin_action_log ORDER BY at DESC LIMIT 4;\n");

                return Summarize();
            }
            finally
            {
                await admin.DisposeAsync();
                await forTeleportAndGive.DisposeAsync();
                await forKick.DisposeAsync();
            }
        }
        finally
        {
            await forBan.DisposeAsync();
        }
    }

    /// <summary>
    /// Verificación del anticheat agregado de la Fase 13 (§8). Dos bots a la vez:
    /// <list type="bullet">
    /// <item>el <b>tramposo</b> insiste en atacar a una entidad que no existe — el servidor
    /// responde <c>TargetNotFound</c>, que <c>AnomalyMapping</c> cuenta como
    /// <c>OutOfRange</c>— hasta cruzar los dos umbrales;</item>
    /// <item>el <b>honesto</b> se queda quieto en la misma corrida, para demostrar que la
    /// escalada es por sesión y no un corte global.</item>
    /// </list>
    /// El ritmo es de 10 ataques por segundo, la mitad del cupo de la familia <c>Combat</c>
    /// (20/s): así lo que desconecta es la agregación de anomalías y no el rate limiter, que
    /// tiene su propio camino y ya estaba probado desde la Fase 1.
    /// <code>dotnet run --project tools/Epimeteo.WorldBot -- --anticheat [url]</code>
    /// </summary>
    private static async Task<int> AnticheatPhase(string url, GameMap map)
    {
        const int InexistentTarget = 999_999;

        var run = Guid.NewGuid().ToString("N")[..8];
        var tramposo = new Bot(url, $"cheat_{run}", $"Cheat_{run}", map, lagMs: 0);
        var honesto = new Bot(url, $"fair_{run}", $"Fair_{run}", map, lagMs: 0);
        var flota = new List<Bot> { tramposo, honesto };

        try
        {
            Console.WriteLine("· Anticheat: escalada por anomalías agregadas");
            await tramposo.ConnectAsync(register: true);
            await honesto.ConnectAsync(register: true);
            await Run(flota, 500);

            var (warn, kick) = AnomalyThresholds.For(AnomalyKind.OutOfRange);
            Console.WriteLine($"  … umbrales de OutOfRange: aviso a {warn}, desconexión a {kick}");

            // ── 1. Por debajo del umbral de aviso no pasa nada ──────────
            for (var i = 0; i < warn - 5; i++)
            {
                tramposo.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = InexistentTarget });
                await Run(flota, 100);
            }

            Check($"Con {warn - 5} rechazos (por debajo del aviso) la sesión sigue viva", tramposo.Kicked is null);

            // ── 2. Insistir hasta cruzar el umbral duro desconecta ──────
            for (var i = 0; i < kick && tramposo.Kicked is null; i++)
            {
                tramposo.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = InexistentTarget });
                await Run(flota, 100);
            }

            Check($"Insistir hasta {kick} rechazos acaba desconectando ({tramposo.Kicked})",
                tramposo.Kicked == KickReason.RateLimited);

            // ── 3. El honesto, en la misma corrida, no lo paga ──────────
            // Se drena primero: el S2CKick del tramposo y cualquier mensaje pendiente del honesto
            // pueden estar todavía en vuelo, y comprobar antes de leerlos daría un verde por pura
            // suerte de temporización en vez de por lo que se quiere demostrar.
            await Run([honesto], 1500);

            // La afirmación es precisa a propósito: que **la escalada de anomalías** no lo tocó.
            // Comprobar `Kicked is null` a secas sería frágil — el arnés de bots tiene una
            // inundación de la cola de salida preexistente (KickReason.InternalError, ver §11 de
            // la fase) que nada tiene que ver con el anticheat y haría fallar este test por un
            // motivo ajeno.
            Check($"La escalada no alcanzó al bot honesto de la misma corrida ({honesto.Kicked?.ToString() ?? "sigue dentro"})",
                honesto.Kicked != KickReason.RateLimited);

            Check("...y llegó a jugar con normalidad", honesto.Snapshots > 0);

            Console.WriteLine($"\nusername del tramposo={tramposo.Username} (personaje {tramposo.CharacterId})");
            Console.WriteLine(
                "\nPara comprobar la auditoría y las métricas:\n" +
                "  SELECT kind, count_in_window, action_taken, remote_address\n" +
                "    FROM anomaly_log ORDER BY at DESC LIMIT 5;\n" +
                "  curl -H \"Authorization: Bearer $TOKEN\" http://127.0.0.1:5101/metrics | grep anomalies\n");

            return Summarize();
        }
        finally
        {
            await tramposo.DisposeAsync();
            await honesto.DisposeAsync();
        }
    }

    /// <summary>La posición más fresca conocida de una entidad: la del último <c>Snapshot</c> si ha llegado alguno, si no la del <c>EntitySpawn</c>.</summary>
    private static Vec2 LivePos(Bot bot, EntitySpawnInfo entity) =>
        bot.LivePositions.GetValueOrDefault(entity.Id, new Vec2(entity.X, entity.Y));

    /// <summary>El monstruo vivo conocido más cercano al bot, o <c>null</c> si no hay ninguno a la vista.</summary>
    private static EntitySpawnInfo? NearestMonster(Bot bot) =>
        bot.KnownEntities.Values
            .Where(e => e.Type == EntityType.Monster)
            .OrderBy(e => Vec2.DistanceSquared(bot.ServerPos, LivePos(bot, e)))
            .FirstOrDefault();

    /// <summary>
    /// Se acerca a un monstruo que puede seguir moviéndose mientras se acerca (patrulla o
    /// persigue al bot): en vez de una única caminata larga a un punto fijo —que un
    /// <c>KnownEntities</c> desactualizado dejaría apuntando a donde estaba al aparecer, no a
    /// donde está ahora (Fase 10)—, va en tramos cortos apuntando cada vez a la posición más
    /// fresca conocida (<see cref="LivePos"/>), hasta entrar en rango o agotar el tiempo.
    /// </summary>
    private static async Task ApproachMonster(List<Bot> flota, Bot bot, int monsterId, float rangeTiles, int timeoutMs)
    {
        for (var restante = timeoutMs; restante > 0; restante -= 1500)
        {
            if (!bot.KnownEntities.TryGetValue(monsterId, out var monster))
            {
                return;
            }

            var pos = LivePos(bot, monster);
            if (Vec2.DistanceSquared(bot.ServerPos, pos) <= rangeTiles * rangeTiles)
            {
                return;
            }

            bot.WalkTarget = pos;
            await Run(flota, 1500);
        }
    }

    /// <summary>
    /// Se acerca al monstruo vivo más cercano y le lanza la habilidad. Vuelve a mirar cuál es el
    /// más cercano justo antes de acercarse y de lanzar (no el capturado al principio): en un
    /// campo con varios monstruos moviéndose y pegando de vuelta, el que estaba más cerca hace un
    /// momento puede haber muerto o alejado. Devuelve si el <c>CombatEvent</c> de la habilidad
    /// llegó de verdad.
    /// </summary>
    private static async Task<bool> CastSkillOnNearest(List<Bot> flota, Bot bot, string skillKey)
    {
        var monster = NearestMonster(bot);
        if (monster is null)
        {
            return false;
        }

        await ApproachMonster(flota, bot, monster.Id, CombatConstants.MeleeRangeTiles, 15_000);

        bot.CombatEvents.Clear();
        bot.SendNow(Opcode.SkillCast, new C2SSkillCast { SkillKey = skillKey, TargetEntityId = monster.Id });
        await Run(flota, 500);
        return bot.CombatEvents.Any(e => e.SkillKey == skillKey);
    }

    /// <summary>
    /// Manda ataques básicos al monstruo más cercano cada 900 ms durante la espera del cooldown de
    /// una habilidad, en vez de quedarse quieto encajando golpes gratis: el ataque básico no
    /// comparte cooldown con la habilidad (D7), así que no cuesta nada seguir pegando mientras se
    /// cumple — y de paso evita acabar muerto por quedarse parado.
    /// </summary>
    private static async Task AttackWhileWaitingNearest(List<Bot> flota, Bot bot, int ms)
    {
        for (var restante = ms; restante > 0; restante -= 900)
        {
            var monster = NearestMonster(bot);
            if (monster is not null)
            {
                bot.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = monster.Id });
            }

            await Run(flota, 900);
        }
    }

    /// <summary>
    /// Ataca al monstruo vivo más cercano una y otra vez, cambiando de objetivo cuando hace falta,
    /// hasta que el nivel sube de verdad o se agota el número de muertes permitidas. Sin manipular
    /// nada por SQL: la XP y el nivel los produce el protocolo real (FASE-10-progresion.md §9).
    /// <para>
    /// Morir contra un monstruo no cuesta XP (a diferencia del PvP, FASE-09 §2 D3) y reaparece de
    /// inmediato en el pueblo (<c>Respawn</c> no tiene retardo) — así que si el bot muere a manos
    /// de algo que le ganó el intercambio, lo único que hace falta es volver al punto de caza y
    /// seguir, no abortar la corrida entera.
    /// </para>
    /// </summary>
    private static async Task GrindForLevelUp(List<Bot> flota, Bot bot, Vec2[] returnRoute, int maxKills)
    {
        var startLevel = bot.Level;

        // Alguno de los "más cercanos por distancia recta" puede estar al otro lado del muro
        // interno del campo (x = 56, y = 18-29 — el mismo que separa a los lobos de los limos) y
        // no ser alcanzable nunca sin rodear. Sin lista negra, NearestMonster lo volvería a elegir
        // en cuanto se soltara, y el grind se quedaría girando sobre el mismo imposible para
        // siempre en vez de darle una oportunidad a otro.
        var inalcanzables = new HashSet<int>();

        for (var kill = 0; kill < maxKills && bot.Level == startLevel; kill++)
        {
            var (hpAhora, hpMaxAhora) = bot.EntityHp.GetValueOrDefault(bot.MyEntityId);
            Console.WriteLine($"  … [{kill}] Nv {bot.Level} XP {bot.Xp} HP {hpAhora}/{hpMaxAhora} en {bot.ServerPos.X:F1},{bot.ServerPos.Y:F1}");

            if (bot.Deaths.Any(d => d.Id == bot.MyEntityId))
            {
                // Reaparece en el pueblo, al otro lado de la muralla: hace falta volver a pasar
                // por la puerta (un solo tile, FASE-09 §9), no ir en línea recta al punto de caza.
                Console.WriteLine("  … el bot murió a manos de un monstruo; vuelve al punto de caza tras reaparecer");
                bot.Deaths.RemoveAll(d => d.Id == bot.MyEntityId);
                await Run(flota, 1000); // deja que el Respawn (instantáneo en el servidor) llegue por snapshot
                await Rutas(flota, 25_000, (bot, returnRoute));
                continue;
            }

            var monster = bot.KnownEntities.Values
                .Where(e => e.Type == EntityType.Monster && !inalcanzables.Contains(e.Id))
                .OrderBy(e => Vec2.DistanceSquared(bot.ServerPos, LivePos(bot, e)))
                .FirstOrDefault();

            if (monster is null)
            {
                Console.WriteLine($"  … sin monstruos alcanzables a la vista (conocidos {bot.KnownEntities.Count}, en lista negra {inalcanzables.Count})");
                // Nada vivo a la vista ahora mismo: esperar a que un respawn lo vuelva a anunciar,
                // y darle otra oportunidad a los que se descartaron —a lo mejor ya no aplica, p.
                // ej. porque el bot se ha movido a otra parte del campo tras reaparecer.
                inalcanzables.Clear();
                await Run(flota, 3000);
                continue;
            }

            await ApproachMonster(flota, bot, monster.Id, CombatConstants.MeleeRangeTiles, 20_000);

            // Si tras el acercamiento sigue fuera de rango, es que patrulla más rápido de lo que
            // se le persigue o está al otro lado de un muro: mejor descartarlo que insistir 40
            // ataques (36 s) contra alguien al que nunca se llega.
            if (!bot.KnownEntities.TryGetValue(monster.Id, out var stillThere) ||
                Vec2.DistanceSquared(bot.ServerPos, LivePos(bot, stillThere)) > CombatConstants.MeleeRangeTiles * CombatConstants.MeleeRangeTiles)
            {
                inalcanzables.Add(monster.Id);
                continue;
            }

            bot.SystemMessages.Clear();
            for (var golpe = 0; golpe < 40 && bot.KnownEntities.ContainsKey(monster.Id) &&
                bot.Level == startLevel && !bot.Deaths.Any(d => d.Id == bot.MyEntityId); golpe++)
            {
                bot.SendNow(Opcode.Attack, new C2SAttack { TargetEntityId = monster.Id });
                await Run(flota, 900);
            }

            Console.WriteLine($"  … tras el combate: {(bot.KnownEntities.ContainsKey(monster.Id) ? "sigue vivo" : "muerto/despawn")}, " +
                $"XP {bot.Xp}, último aviso: {bot.SystemMessages.LastOrDefault()?.Key ?? "ninguno"}");
        }
    }

    /// <summary>
    /// Lleva a varios bots por sus rutas a la vez, waypoint a waypoint. Hace falta ruta y no
    /// destino porque el caminante del bot es ingenuo (signo de la diferencia) y la muralla del
    /// pueblo tiene una sola puerta: en línea recta se quedaría pegado al muro.
    /// </summary>
    private static async Task Rutas(List<Bot> flota, params (Bot Bot, Vec2[] Ruta)[] rutas)
        => await Rutas(flota, 30_000, rutas);

    private static async Task Rutas(List<Bot> flota, int ms, params (Bot Bot, Vec2[] Ruta)[] rutas)
    {
        var indices = new int[rutas.Length];

        for (var i = 0; i < rutas.Length; i++)
        {
            if (rutas[i].Ruta.Length > 0)
            {
                rutas[i].Bot.WalkTarget = rutas[i].Ruta[0];
                indices[i] = 1;
            }
        }

        for (var restante = ms; restante > 0; restante -= 400)
        {
            var pendiente = false;

            for (var i = 0; i < rutas.Length; i++)
            {
                var (bot, ruta) = rutas[i];

                if (bot.WalkTarget is null && indices[i] < ruta.Length)
                {
                    bot.WalkTarget = ruta[indices[i]];
                    indices[i]++;
                }

                pendiente |= bot.WalkTarget is not null;
            }

            if (!pendiente)
            {
                return;
            }

            await Run(flota, 400);
        }
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

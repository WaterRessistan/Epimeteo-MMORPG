using System.Net.WebSockets;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Time;

namespace Epimeteo.SmokeClient;

/// <summary>
/// Comprueba de punta a punta el handshake y las reglas de protocolo del servidor sin necesidad
/// de abrir Godot. Es la verificación automática de los criterios de aceptación de la Fase 1.
/// <code>dotnet run --project tools/Epimeteo.SmokeClient [ws://127.0.0.1:5100/ws]</code>
/// </summary>
internal static class Program
{
    private static int _failures;

    private static async Task<int> Main(string[] args)
    {
        var url = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "ws://127.0.0.1:5100/ws";
        Console.WriteLine($"Servidor: {url}\n");

        await HandshakeYPing(url);
        await VersionIncorrecta(url);
        await OpcodeFueraDeEstado(url);
        await OpcodeDesconocido(url);
        await FrameDemasiadoGrande(url);
        await FrameDeTexto(url);
        await RegistroYLogin(url);
        await LoginConCredencialesIncorrectas(url);
        await RegistroDuplicado(url);
        await RateLimitDeLogin(url);

        // Las pruebas de arriba ya consumen los 5 intentos/minuto por IP a propósito (la última
        // los agota del todo). Las de personajes de abajo necesitan Register/Login reales sin
        // RateLimited de por medio, así que se espera a que la ventana deslizante de un minuto
        // quede libre otra vez antes de arrancarlas.
        Console.WriteLine("\n  ...esperando 65 s a que se libere el cupo de 5 intentos/minuto por IP");
        await Task.Delay(TimeSpan.FromSeconds(65));

        await PersonajesFlujoCompleto(url);
        await BorradoDePersonaje(url);
        await PersonajesPersistenEntreConexiones(url);
        await InventarioFlujoCompleto(url);

        if (args.Contains("--lento"))
        {
            await SinSaludar(url);
        }
        else
        {
            Console.WriteLine("  [ -- ] Timeout sin Hello: omitido (pásale --lento para probarlo)");
        }

        Console.WriteLine(_failures == 0 ? "\nTodo correcto." : $"\n{_failures} comprobación(es) fallida(s).");
        return _failures == 0 ? 0 : 1;
    }

    private static async Task HandshakeYPing(string url)
    {
        using var ws = await Open(url);
        await Send(ws, Opcode.Hello, new C2SHello { ProtocolVersion = ProtocolVersion.Current, ClientBuild = "smoke" });

        var (opcode, frame) = await Receive(ws);
        Check("HelloAck recibido", opcode == Opcode.HelloAck);

        if (!FrameCodec.TryDecodePayload<S2CHelloAck>(frame, out var ack) || ack is null)
        {
            Check("HelloAck legible", false);
            return;
        }

        Check($"Protocolo v{ack.ServerProtocolVersion}", ack.ServerProtocolVersion == ProtocolVersion.Current);
        Check($"Ritmos {ack.TickRate}/{ack.SnapshotRate} Hz", ack is { TickRate: 20, SnapshotRate: 10 });

        for (var i = 0; i < 3; i++)
        {
            var sent = ServerClock.NowMs;
            await Send(ws, Opcode.Ping, new C2SPing { ClientTimeMs = sent });

            var (pongOpcode, pongFrame) = await Receive(ws);
            if (pongOpcode != Opcode.Pong || !FrameCodec.TryDecodePayload<S2CPong>(pongFrame, out var pong) || pong is null)
            {
                Check("Pong recibido", false);
                return;
            }

            Check($"Pong {i + 1}: eco correcto, RTT {ServerClock.NowMs - pong.ClientTimeMs} ms", pong.ClientTimeMs == sent);
            await Task.Delay(50);
        }
    }

    private static async Task VersionIncorrecta(string url)
    {
        using var ws = await Open(url);
        await Send(ws, Opcode.Hello, new C2SHello { ProtocolVersion = ProtocolVersion.Current + 999, ClientBuild = "smoke" });

        var kick = await ReceiveKick(ws);
        Check("Versión incorrecta → Kick(VersionMismatch)",
            kick is { Reason: KickReason.VersionMismatch, ServerProtocolVersion: ProtocolVersion.Current });
    }

    private static async Task OpcodeFueraDeEstado(string url)
    {
        using var ws = await Open(url);

        // CharListRequest sólo es legal estando autenticado.
        await Send(ws, Opcode.CharListRequest, new C2SPing());

        var kick = await ReceiveKick(ws);
        Check("Opcode fuera de estado → Kick(InvalidState)", kick?.Reason == KickReason.InvalidState);
    }

    private static async Task OpcodeDesconocido(string url)
    {
        using var ws = await Open(url);
        await Send(ws, (Opcode)0x7FFF, new C2SPing());

        var kick = await ReceiveKick(ws);
        Check("Opcode desconocido → Kick(ProtocolError)", kick?.Reason == KickReason.ProtocolError);
    }

    private static async Task FrameDemasiadoGrande(string url)
    {
        using var ws = await Open(url);
        var enorme = new byte[FrameCodec.MaxFrameBytes + 1024];
        await ws.SendAsync(enorme, WebSocketMessageType.Binary, true, CancellationToken.None);

        var kick = await ReceiveKick(ws);
        Check("Frame de 17 KB → Kick(ProtocolError)", kick?.Reason == KickReason.ProtocolError);
    }

    private static async Task FrameDeTexto(string url)
    {
        using var ws = await Open(url);
        await ws.SendAsync("hola"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);

        var kick = await ReceiveKick(ws);
        Check("Frame de texto → Kick(ProtocolError)", kick?.Reason == KickReason.ProtocolError);
    }

    /// <summary>
    /// Fase 2: registrar una cuenta nueva, cerrar la conexión y volver a entrar con las mismas
    /// credenciales en una conexión distinta — el criterio de aceptación de FASE-02-persistencia.
    /// </summary>
    private static async Task RegistroYLogin(string url)
    {
        var username = $"smoke_{Guid.NewGuid():N}"[..20];

        using (var ws = await Open(url))
        {
            await Greet(ws);
            var registerResult = await Auth(ws, Opcode.Register,
                new C2SRegister { Username = username, Email = null, Password = "clave-de-prueba-123" });

            Check("Registro nuevo → Ok", registerResult is { Ok: true, AccountId: > 0 });
            Check("Registro nuevo → SessionToken presente", !string.IsNullOrEmpty(registerResult?.SessionToken));
        }

        using (var ws = await Open(url))
        {
            await Greet(ws);
            var loginResult = await Auth(ws, Opcode.Login,
                new C2SLogin { Username = username, Password = "clave-de-prueba-123" });

            Check("Login con las mismas credenciales (otra conexión) → Ok", loginResult is { Ok: true });
        }
    }

    private static async Task LoginConCredencialesIncorrectas(string url)
    {
        using var ws = await Open(url);
        await Greet(ws);

        var result = await Auth(ws, Opcode.Login,
            new C2SLogin { Username = $"no_existe_{Guid.NewGuid():N}"[..20], Password = "cualquiera123" });

        Check("Login con usuario inexistente → InvalidCredentials",
            result is { Ok: false, Code: ResultCode.InvalidCredentials });
    }

    private static async Task RegistroDuplicado(string url)
    {
        var username = $"smoke_{Guid.NewGuid():N}"[..20];

        using (var ws = await Open(url))
        {
            await Greet(ws);
            await Auth(ws, Opcode.Register, new C2SRegister { Username = username, Email = null, Password = "clave-de-prueba-123" });
        }

        using var ws2 = await Open(url);
        await Greet(ws2);
        var result = await Auth(ws2, Opcode.Register,
            new C2SRegister { Username = username, Email = null, Password = "otra-clave-456" });

        Check("Registro con username repetido → AccountAlreadyExists",
            result is { Ok: false, Code: ResultCode.AccountAlreadyExists });
    }

    /// <summary>
    /// 5/minuto por IP (docs/01-protocolo.md), persistido en <c>login_attempts</c> y compartido
    /// por todas las pruebas anteriores (misma IP 127.0.0.1 en local): no importa exactamente en
    /// qué intento se dispara, sólo que tras varios ya está activo y se queda así el resto del
    /// minuto — de ahí que sólo se compruebe el último de la tanda.
    /// </summary>
    private static async Task RateLimitDeLogin(string url)
    {
        var username = $"nunca_existe_{Guid.NewGuid():N}"[..20];
        S2CAuthResult? last = null;

        for (var i = 0; i < 6; i++)
        {
            using var ws = await Open(url);
            await Greet(ws);
            last = await Auth(ws, Opcode.Login, new C2SLogin { Username = username, Password = "cualquiera123" });
        }

        Check("Tras varios intentos fallidos en <1 min → RateLimited", last is { Ok: false, Code: ResultCode.RateLimited });
    }

    /// <summary>
    /// Fase 3: registro → lista vacía → crear (con choques de nombre/slot) → seleccionar →
    /// <c>WorldEnter</c> → <c>WorldReady</c> sin que la sesión se caiga. Equivalente automático
    /// del criterio de aceptación de FASE-03-personajes.md §9 (no hay Godot en este servidor).
    /// </summary>
    private static async Task PersonajesFlujoCompleto(string url)
    {
        var username = $"smoke_{Guid.NewGuid():N}"[..20];

        using var ws = await Open(url);
        await Greet(ws);
        var register = await Auth(ws, Opcode.Register,
            new C2SRegister { Username = username, Email = null, Password = "clave-de-prueba-123" });
        Check("Fase 3: registro para el flujo de personajes → Ok", register is { Ok: true });

        var emptyList = await SendReceive<C2SCharListRequest, S2CCharList>(
            ws, Opcode.CharListRequest, new C2SCharListRequest(), Opcode.CharList);
        Check("CharList inicial → 0 personajes", emptyList is { Characters.Length: 0 });

        var name = $"Heroe_{Guid.NewGuid():N}"[..12];
        var create = await SendReceive<C2SCharCreate, S2CCharCreateResult>(
            ws, Opcode.CharCreate,
            new C2SCharCreate { Name = name, ClassKey = "class.warrior", Slot = 0, PaletteIndex = 1 },
            Opcode.CharCreateResult);
        Check("CharCreate en slot vacío → Ok", create is { Ok: true } && create.Character is { Slot: 0, ClassKey: "class.warrior" });

        var dupName = await SendReceive<C2SCharCreate, S2CCharCreateResult>(
            ws, Opcode.CharCreate,
            new C2SCharCreate { Name = name, ClassKey = "class.mage", Slot = 1, PaletteIndex = 0 },
            Opcode.CharCreateResult);
        Check("CharCreate con nombre repetido → NameTaken", dupName is { Ok: false, Code: ResultCode.NameTaken });

        var slotTaken = await SendReceive<C2SCharCreate, S2CCharCreateResult>(
            ws, Opcode.CharCreate,
            new C2SCharCreate { Name = $"Otro_{Guid.NewGuid():N}"[..10], ClassKey = "class.mage", Slot = 0, PaletteIndex = 0 },
            Opcode.CharCreateResult);
        Check("CharCreate en slot ya ocupado → SlotOccupied", slotTaken is { Ok: false, Code: ResultCode.SlotOccupied });

        var listAfterCreate = await SendReceive<C2SCharListRequest, S2CCharList>(
            ws, Opcode.CharListRequest, new C2SCharListRequest(), Opcode.CharList);
        Check("CharList tras crear → 1 personaje", listAfterCreate is { Characters.Length: 1 });

        var characterId = create?.Character?.Id ?? 0;
        var worldEnter = await SendReceive<C2SCharSelect, S2CWorldEnter>(
            ws, Opcode.CharSelect, new C2SCharSelect { CharacterId = characterId }, Opcode.WorldEnter);
        Check("CharSelect → WorldEnter con el mapa inicial y stats de guerrero",
            worldEnter is { MapKey: "map.village" } && worldEnter.Stats.Level == 1 && worldEnter.Stats.Hp == 120);

        await Send(ws, Opcode.WorldReady, new C2SWorldReady());
        await Send(ws, Opcode.Ping, new C2SPing { ClientTimeMs = ServerClock.NowMs });
        var (pongOpcode, _) = await Receive(ws);
        Check("WorldReady no cierra la sesión (Ping sigue respondiendo tras InWorld)", pongOpcode == Opcode.Pong);
    }

    /// <summary>Confirma, y libera, y vuelve a admitir un personaje en el mismo slot.</summary>
    private static async Task BorradoDePersonaje(string url)
    {
        var username = $"smoke_{Guid.NewGuid():N}"[..20];

        using var ws = await Open(url);
        await Greet(ws);
        await Auth(ws, Opcode.Register, new C2SRegister { Username = username, Email = null, Password = "clave-de-prueba-123" });

        var name = $"Borrable_{Guid.NewGuid():N}"[..12];
        var create = await SendReceive<C2SCharCreate, S2CCharCreateResult>(
            ws, Opcode.CharCreate,
            new C2SCharCreate { Name = name, ClassKey = "class.hybrid", Slot = 3, PaletteIndex = 2 },
            Opcode.CharCreateResult);
        Check("Personaje creado para probar el borrado", create is { Ok: true });

        var characterId = create?.Character?.Id ?? 0;
        var deleteWithoutConfirm = await SendReceive<C2SCharDelete, S2CCharDeleteResult>(
            ws, Opcode.CharDelete, new C2SCharDelete { CharacterId = characterId, Confirm = false }, Opcode.CharDeleteResult);
        Check("CharDelete sin confirmar → no borra", deleteWithoutConfirm is { Ok: false });

        var delete = await SendReceive<C2SCharDelete, S2CCharDeleteResult>(
            ws, Opcode.CharDelete, new C2SCharDelete { CharacterId = characterId, Confirm = true }, Opcode.CharDeleteResult);
        Check("CharDelete confirmado → Ok", delete is { Ok: true });

        var listAfterDelete = await SendReceive<C2SCharListRequest, S2CCharList>(
            ws, Opcode.CharListRequest, new C2SCharListRequest(), Opcode.CharList);
        Check("Slot liberado tras borrar → 0 personajes", listAfterDelete is { Characters.Length: 0 });

        var recreate = await SendReceive<C2SCharCreate, S2CCharCreateResult>(
            ws, Opcode.CharCreate,
            new C2SCharCreate { Name = $"Nuevo_{Guid.NewGuid():N}"[..10], ClassKey = "class.hybrid", Slot = 3, PaletteIndex = 0 },
            Opcode.CharCreateResult);
        Check("El slot liberado admite un personaje nuevo", recreate is { Ok: true });
    }

    /// <summary>Cerrar la conexión, reabrir y entrar con las mismas credenciales: el personaje sigue ahí.</summary>
    private static async Task PersonajesPersistenEntreConexiones(string url)
    {
        var username = $"smoke_{Guid.NewGuid():N}"[..20];
        var name = $"Persist_{Guid.NewGuid():N}"[..12];

        using (var ws = await Open(url))
        {
            await Greet(ws);
            await Auth(ws, Opcode.Register, new C2SRegister { Username = username, Email = null, Password = "clave-de-prueba-123" });

            var create = await SendReceive<C2SCharCreate, S2CCharCreateResult>(
                ws, Opcode.CharCreate,
                new C2SCharCreate { Name = name, ClassKey = "class.warrior", Slot = 4, PaletteIndex = 3 },
                Opcode.CharCreateResult);
            Check("Personaje creado antes de cerrar la conexión", create is { Ok: true });
        }

        using (var ws2 = await Open(url))
        {
            await Greet(ws2);
            var login = await Auth(ws2, Opcode.Login, new C2SLogin { Username = username, Password = "clave-de-prueba-123" });
            Check("Login tras reabrir la conexión → Ok", login is { Ok: true });

            var list = await SendReceive<C2SCharListRequest, S2CCharList>(
                ws2, Opcode.CharListRequest, new C2SCharListRequest(), Opcode.CharList);
            Check("El personaje sigue ahí tras reconectar", list is { Characters.Length: 1 } && list.Characters[0].Name == name);
        }
    }

    /// <summary>
    /// Flujo completo de inventario (FASE-06 §9): kit inicial, mover, equipar (con sus stats
    /// derivados), un intento inválido que no debe cambiar nada, usar y tirar — y que todo
    /// sobreviva a una reconexión.
    /// </summary>
    private static async Task InventarioFlujoCompleto(string url)
    {
        var username = $"smoke_{Guid.NewGuid():N}"[..20];
        var name = $"Inv_{Guid.NewGuid():N}"[..10];

        using var ws = await Open(url);
        await Greet(ws);
        var registered = await AuthWithRetry(ws, Opcode.Register,
            new C2SRegister { Username = username, Email = null, Password = "clave-de-prueba-123" });
        Check("Fase 6: registro para el flujo de inventario → Ok", registered is { Ok: true });

        var create = await SendReceive<C2SCharCreate, S2CCharCreateResult>(
            ws, Opcode.CharCreate,
            new C2SCharCreate { Name = name, ClassKey = "class.warrior", Slot = 0, PaletteIndex = 0 },
            Opcode.CharCreateResult);
        Check("Fase 6: personaje guerrero creado para el flujo de inventario", create is { Ok: true });

        await SendReceive<C2SCharSelect, S2CWorldEnter>(
            ws, Opcode.CharSelect, new C2SCharSelect { CharacterId = create!.Character!.Id }, Opcode.WorldEnter);
        await Send(ws, Opcode.WorldReady, new C2SWorldReady());

        // ── Kit inicial (content/classes/warrior.json): espada+escudo en la bolsa de armas,
        // 2 pociones en el general. Nada equipado todavía.
        var full = await ReceiveUntil<S2CInventoryFull>(ws, Opcode.InventoryFull);
        Check("InventoryFull trae los 3 stacks del kit inicial", full?.Items.Length == 3);
        Check("La espada está en la bolsa de armas, slot 0",
            full?.Items.Any(i => i.DefKey == "item.iron_sword" && i.Container == ContainerId.WeaponBag && i.Slot == 0) == true);
        Check("El escudo está en la bolsa de armas, slot 1",
            full?.Items.Any(i => i.DefKey == "item.wooden_shield" && i.Container == ContainerId.WeaponBag && i.Slot == 1) == true);
        Check("Las 2 pociones están en el general, slot 0",
            full?.Items.Any(i => i.DefKey == "item.health_potion" && i.Container == ContainerId.General && i.Slot == 0 && i.Quantity == 2) == true);

        var equip0 = await ReceiveUntil<S2CEquipmentUpdate>(ws, Opcode.EquipmentUpdate);
        Check("Sin equipo puesto, los stats derivados son los de base del guerrero",
            equip0 is { Equipped.Length: 0, HpMax: 120, MpMax: 20, StrEffective: 8 });

        // ── Mover la poción a otro slot del general.
        await Send(ws, Opcode.InvMove, new C2SInvMove
        {
            FromContainer = ContainerId.General, FromSlot = 0, ToContainer = ContainerId.General, ToSlot = 5, Quantity = 2,
        });
        var moved = await ReceiveUntil<S2CInventoryDelta>(ws, Opcode.InventoryDelta);
        Check("Mover la poción: el slot 0 queda vacío y el 5 la tiene",
            moved?.Changes.Any(c => c.Container == ContainerId.General && c.Slot == 0 && c.Item is null) == true &&
            moved?.Changes.Any(c => c.Container == ContainerId.General && c.Slot == 5 && c.Item?.Quantity == 2) == true);

        // ── Equipar la espada en MainHand: sube la fuerza efectiva en el bono del ítem.
        await Send(ws, Opcode.Equip, new C2SEquip { Container = ContainerId.WeaponBag, Slot = 0, EquipSlot = EquipSlot.MainHand });
        var swordMoved = await ReceiveUntil<S2CInventoryDelta>(ws, Opcode.InventoryDelta);
        Check("Equipar la espada vacía su slot en la bolsa de armas",
            swordMoved?.Changes.Any(c => c.Container == ContainerId.WeaponBag && c.Slot == 0 && c.Item is null) == true);

        var equip1 = await ReceiveUntil<S2CEquipmentUpdate>(ws, Opcode.EquipmentUpdate);
        Check("La espada equipada sube la fuerza efectiva (bonusStr +2)",
            equip1 is { StrEffective: 10, HpMax: 120 } &&
            equip1.Equipped.Any(i => i.DefKey == "item.iron_sword" && i.Slot == (byte)EquipSlot.MainHand));

        // ── Intentar equipar el escudo en Head: categoría equivocada, nada debe cambiar.
        await Send(ws, Opcode.Equip, new C2SEquip { Container = ContainerId.WeaponBag, Slot = 1, EquipSlot = EquipSlot.Head });
        var rejected = await ReceiveUntil<S2CSystemMessage>(ws, Opcode.SystemMessage);
        Check("Equipar el escudo en Head se rechaza con SystemMessage", rejected?.Key == "inventory.NotEquippable");

        // ── Usar una poción: la cantidad baja de 2 a 1.
        await Send(ws, Opcode.InvUse, new C2SInvUse { Container = ContainerId.General, Slot = 5 });
        var used = await ReceiveUntil<S2CInventoryDelta>(ws, Opcode.InventoryDelta);
        Check("Usar una poción deja 1 en el stack",
            used?.Changes.Any(c => c.Container == ContainerId.General && c.Slot == 5 && c.Item?.Quantity == 1) == true);

        // ── Tirar la última poción: el hueco queda vacío.
        await Send(ws, Opcode.InvDrop, new C2SInvDrop { Container = ContainerId.General, Slot = 5, Quantity = 1 });
        var dropped = await ReceiveUntil<S2CInventoryDelta>(ws, Opcode.InventoryDelta);
        Check("Tirar la última poción vacía el hueco",
            dropped?.Changes.Any(c => c.Container == ContainerId.General && c.Slot == 5 && c.Item is null) == true);

        // ── Reconectar: el estado final (espada puesta, escudo en la bolsa, sin pociones) tiene
        // que sobrevivir — es la persistencia de la Fase 6 (FASE-06 §10.5), sin esperar a un
        // reinicio del servidor.
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        // El guardado de inventario es asíncrono (InventorySaver, FASE-06 §2 D2), igual que el
        // de posición en la Fase 4: sin este margen, reconectar de inmediato leería Postgres
        // antes de que la cola hubiera drenado el último guardado.
        await Task.Delay(1500);

        using var ws2 = await Open(url);
        await Greet(ws2);
        var loggedIn = await AuthWithRetry(ws2, Opcode.Login, new C2SLogin { Username = username, Password = "clave-de-prueba-123" });
        Check("Fase 6: login al reconectar → Ok", loggedIn is { Ok: true });
        await SendReceive<C2SCharSelect, S2CWorldEnter>(
            ws2, Opcode.CharSelect, new C2SCharSelect { CharacterId = create.Character.Id }, Opcode.WorldEnter);
        await Send(ws2, Opcode.WorldReady, new C2SWorldReady());

        var fullAfter = await ReceiveUntil<S2CInventoryFull>(ws2, Opcode.InventoryFull);
        Check("Tras reconectar: sólo queda el escudo en la bolsa (la espada está equipada)",
            fullAfter?.Items.Length == 1 &&
            fullAfter.Items[0] is { DefKey: "item.wooden_shield", Container: ContainerId.WeaponBag });

        var equipAfter = await ReceiveUntil<S2CEquipmentUpdate>(ws2, Opcode.EquipmentUpdate);
        Check("Tras reconectar: la espada sigue equipada y la fuerza efectiva se mantiene",
            equipAfter is { StrEffective: 10 } &&
            equipAfter.Equipped.Any(i => i.DefKey == "item.iron_sword"));
    }

    private static async Task Greet(ClientWebSocket ws)
    {
        await Send(ws, Opcode.Hello, new C2SHello { ProtocolVersion = ProtocolVersion.Current, ClientBuild = "smoke" });
        await Receive(ws); // HelloAck; ya se comprueba en HandshakeYPing.
    }

    private static async Task<S2CAuthResult?> Auth<T>(ClientWebSocket ws, Opcode opcode, T payload)
    {
        await Send(ws, opcode, payload);
        var (responseOpcode, frame) = await Receive(ws);

        if (responseOpcode != Opcode.AuthResult)
        {
            return null;
        }

        return FrameCodec.TryDecodePayload<S2CAuthResult>(frame, out var result) ? result : null;
    }

    /// <summary>
    /// Como <see cref="Auth{T}"/>, pero si el cupo de 5 intentos/minuto por IP ya lo ha agotado
    /// otra prueba de esta misma corrida, espera a que se libere la ventana y reintenta — igual
    /// que ya hace <c>tools/Epimeteo.WorldBot/Bot.cs</c> desde la Fase 4/5. Todo el conjunto de
    /// pruebas comparte IP (127.0.0.1): a medida que se añaden más flujos que reconectan, el cupo
    /// fijo de "esperar 65 s una vez al principio" deja de bastar.
    /// </summary>
    private static async Task<S2CAuthResult?> AuthWithRetry<T>(ClientWebSocket ws, Opcode opcode, T payload)
    {
        var result = await Auth(ws, opcode, payload);
        if (result is not { Ok: false, Code: ResultCode.RateLimited })
        {
            return result;
        }

        Console.WriteLine("  ...cupo de intentos por IP agotado por otra prueba; espera 65 s");

        // Con Ping de por medio, no en un solo Task.Delay: 65 s de silencio superan el
        // IdleTimeoutMs de 30 s de la sesión (docs/01 § Ritmos) y el servidor cerraría la
        // conexión antes de que diera tiempo a reintentar — mismo fallo que ya se encontró y se
        // corrigió en tools/Epimeteo.WorldBot/Bot.cs.
        const int PingEveryMs = 10_000;
        for (var esperado = 0; esperado < 65_000; esperado += PingEveryMs)
        {
            await Task.Delay(Math.Min(PingEveryMs, 65_000 - esperado));
            await Send(ws, Opcode.Ping, new C2SPing { ClientTimeMs = ServerClock.NowMs });
            await Receive(ws, TimeSpan.FromSeconds(2)); // Pong; se descarta, sólo sostiene la sesión.
        }

        return await Auth(ws, opcode, payload);
    }

    /// <summary>Manda <typeparamref name="TReq"/> y decodifica la respuesta si llega con el opcode esperado.</summary>
    private static async Task<TResp?> SendReceive<TReq, TResp>(
        ClientWebSocket ws, Opcode requestOpcode, TReq payload, Opcode expectedResponseOpcode)
    {
        await Send(ws, requestOpcode, payload);
        var (responseOpcode, frame) = await Receive(ws);

        if (responseOpcode != expectedResponseOpcode)
        {
            return default;
        }

        return FrameCodec.TryDecodePayload<TResp>(frame, out var result) ? result : default;
    }

    /// <summary>
    /// Como <see cref="Receive"/>, pero descarta lo que no sea <paramref name="expectedOpcode"/>.
    /// Hace falta para los mensajes de inventario: una vez <c>InWorld</c> el tick manda
    /// <c>Snapshot</c>/<c>ZoneFlagsUpdate</c> de fondo, mezclados con lo que se está esperando.
    /// </summary>
    private static async Task<T?> ReceiveUntil<T>(ClientWebSocket ws, Opcode expectedOpcode, TimeSpan? espera = null)
    {
        var deadline = ServerClock.NowMs + (long)(espera ?? TimeSpan.FromSeconds(5)).TotalMilliseconds;

        while (ServerClock.NowMs < deadline)
        {
            var (opcode, frame) = await Receive(ws, TimeSpan.FromMilliseconds(500));
            if (opcode == Opcode.None)
            {
                continue; // Timeout corto de esta lectura; se reintenta hasta el plazo grande.
            }

            if (opcode == expectedOpcode)
            {
                return FrameCodec.TryDecodePayload<T>(frame, out var result) ? result : default;
            }
        }

        return default;
    }

    /// <summary>
    /// Una conexión que se queda callada tiene que caerse sola: el barrido de timeouts corre
    /// dentro del bucle de tick, así que esto además confirma que el bucle está vivo.
    /// </summary>
    private static async Task SinSaludar(string url)
    {
        using var ws = await Open(url);
        Console.WriteLine("  ...esperando el timeout de Hello (5 s)");

        var inicio = ServerClock.NowMs;
        var kick = await ReceiveKick(ws, TimeSpan.FromSeconds(10));
        var transcurrido = ServerClock.NowMs - inicio;

        Check($"Sin Hello → Kick(Timeout) a los {transcurrido} ms", kick?.Reason == KickReason.Timeout);
    }

    private static async Task<ClientWebSocket> Open(string url)
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(url), CancellationToken.None);
        return ws;
    }

    private static async Task Send<T>(ClientWebSocket ws, Opcode opcode, T payload)
        => await ws.SendAsync(FrameCodec.Encode(opcode, payload), WebSocketMessageType.Binary, true, CancellationToken.None);

    private static async Task<(Opcode Opcode, byte[] Frame)> Receive(ClientWebSocket ws, TimeSpan? espera = null)
    {
        var buffer = new byte[FrameCodec.MaxFrameBytes];
        using var timeout = new CancellationTokenSource(espera ?? TimeSpan.FromSeconds(5));

        try
        {
            var result = await ws.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (Opcode.None, []);
            }

            var frame = buffer[..result.Count];
            return FrameCodec.TryReadOpcode(frame, out var opcode) ? (opcode, frame) : (Opcode.None, frame);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            return (Opcode.None, []);
        }
    }

    private static async Task<S2CKick?> ReceiveKick(ClientWebSocket ws, TimeSpan? espera = null)
    {
        var (opcode, frame) = await Receive(ws, espera);
        if (opcode != Opcode.Kick)
        {
            return null;
        }

        return FrameCodec.TryDecodePayload<S2CKick>(frame, out var kick) ? kick : null;
    }

    private static void Check(string descripcion, bool ok)
    {
        if (!ok)
        {
            _failures++;
        }

        Console.WriteLine($"  [{(ok ? "OK " : "MAL")}] {descripcion}");
    }
}

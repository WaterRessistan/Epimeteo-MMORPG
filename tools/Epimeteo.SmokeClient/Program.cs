using System.Net.WebSockets;
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

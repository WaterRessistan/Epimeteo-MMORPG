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

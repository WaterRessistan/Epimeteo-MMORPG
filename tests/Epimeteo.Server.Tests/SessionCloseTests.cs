using Epimeteo.Server.Net;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// El cierre de una sesión expulsada. Parece un detalle de fontanería, pero es lo que decide si el
/// jugador ve "te han expulsado por X" o simplemente se le cae el juego sin explicación.
/// </summary>
public sealed class SessionCloseTests
{
    /// <summary>
    /// Regresión: la sesión seguía leyendo del socket <b>después</b> de encolar el
    /// <see cref="S2CKick"/>, para que el frame llegue.
    /// <para>
    /// Antes, el bucle de lectura cortaba en cuanto había un cierre en marcha. Con un cliente que
    /// aún estaba enviando —el caso típico, porque a quien se expulsa por inundar de inputs está
    /// inundando de inputs justo en ese instante— dejar de leer cortaba la conexión de golpe y el
    /// <c>Kick</c> ya enviado se perdía antes de que el cliente lo leyera. Se veía como una
    /// expulsión sin motivo, y sólo a veces.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ExpulsarMandaElMotivoYSigueDrenandoAlClienteQueAunEnvia()
    {
        var socket = new FakeWebSocket();
        var session = NewSession(socket);
        var run = session.RunAsync(CancellationToken.None);

        session.Kick(KickReason.RateLimited, ResultCode.RateLimited);

        // El cliente todavía tenía inputs en vuelo cuando lo echamos.
        for (var i = 0; i < 5; i++)
        {
            socket.ClientSends(FrameCodec.Encode(Opcode.Ping, new C2SPing { ClientTimeMs = i }));
        }

        socket.ClientCloses();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        var enviados = socket.Sent
            .Where(frame => FrameCodec.TryReadOpcode(frame, out var opcode) && opcode == Opcode.Kick)
            .ToArray();

        Assert.Single(enviados);
        Assert.True(FrameCodec.TryDecodePayload<S2CKick>(enviados[0], out var kick));
        Assert.Equal(KickReason.RateLimited, kick!.Reason);

        // Los cinco inputs en vuelo se leyeron y se tiraron: ni un Pong salió después del Kick.
        Assert.DoesNotContain(
            socket.Sent,
            frame => FrameCodec.TryReadOpcode(frame, out var opcode) && opcode == Opcode.Pong);

        Assert.Equal(1, socket.CloseOutputCalls);
    }

    /// <summary>
    /// Y el drenaje no es eterno: si el cliente nunca contesta al cierre, la sesión termina sola.
    /// Sin esto, un cliente que se quedara mudo dejaría la sesión colgada para siempre.
    /// </summary>
    [Fact]
    public async Task SiElClienteNoContestaAlCierreLaSesionTerminaIgual()
    {
        var socket = new FakeWebSocket();
        var session = NewSession(socket);
        var run = session.RunAsync(CancellationToken.None);

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        session.Kick(KickReason.ProtocolError);

        // El cliente no manda nada más, ni siquiera su frame de cierre.
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        reloj.Stop();

        Assert.True(session.IsClosing);
        Assert.Equal(1, socket.CloseOutputCalls);

        // Ni corta de golpe (esperó a que el cliente pudiera contestar) ni se queda colgada.
        Assert.InRange(reloj.ElapsedMilliseconds, 1_000, 5_000);
    }

    private static Session NewSession(FakeWebSocket socket) =>
        // El despachador nunca llega a usarse: en estos tests la sesión se expulsa antes de
        // atender un solo frame, que es justo el camino que se está comprobando.
        new(id: 1, socket, "127.0.0.1", handler: null!, outboundCapacity: 32);
}

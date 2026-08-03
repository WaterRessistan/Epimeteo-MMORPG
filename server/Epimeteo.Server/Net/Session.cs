using System.Buffers;
using System.Net.WebSockets;
using System.Threading.Channels;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Time;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Epimeteo.Server.Net;

/// <summary>
/// Una conexión de cliente: su WebSocket, su estado en la máquina de sesión y sus dos bucles
/// (lectura y escritura).
/// <para>
/// Reglas de hilos: <see cref="State"/> y el rate limiter los toca <b>sólo</b> el bucle de
/// lectura. <see cref="Send{T}"/> y <see cref="Kick"/> son seguros desde cualquier hilo porque
/// se limitan a escribir en un <see cref="Channel{T}"/>. Nunca se llama a
/// <c>WebSocket.SendAsync</c> desde fuera del bucle de escritura: no admite envíos concurrentes.
/// </para>
/// </summary>
public sealed class Session
{
    private readonly WebSocket _socket;
    private readonly SessionMessageHandler _handler;
    private readonly Channel<byte[]> _outbound;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ILogger _log;

    private long _lastInboundMs;
    private int _kicked;

    internal Session(int id, WebSocket socket, string remoteAddress, SessionMessageHandler handler, int outboundCapacity)
    {
        Id = id;
        RemoteAddress = remoteAddress;
        ConnectedAtMs = ServerClock.NowMs;
        State = SessionState.Connecting;
        _socket = socket;
        _handler = handler;
        _lastInboundMs = ConnectedAtMs;
        _log = Log.ForContext<Session>().ForContext("SessionId", id);
        _outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(outboundCapacity)
        {
            // Wait (y no DropWrite) porque con DropWrite TryWrite devuelve true y descarta en
            // silencio: preferimos enterarnos y cerrar la sesión atascada.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Identificador de sesión, único mientras el proceso viva.</summary>
    public int Id { get; }

    /// <summary>IP del cliente tal como la ve el servidor (loopback si viene del proxy).</summary>
    public string RemoteAddress { get; }

    /// <summary>Instante monotónico en que se aceptó la conexión.</summary>
    public long ConnectedAtMs { get; }

    /// <summary>Estado en la máquina de sesión. Sólo lo modifica el bucle de lectura.</summary>
    public SessionState State { get; internal set; }

    /// <summary>Build declarada por el cliente en su <c>Hello</c>. Informativa.</summary>
    public string ClientBuild { get; internal set; } = string.Empty;

    /// <summary>Cuenta autenticada. 0 hasta que <c>Login</c>/<c>Register</c> tienen éxito.</summary>
    public long AccountId { get; internal set; }

    /// <summary>Rate limiter por familia de opcode. Sólo lo usa el bucle de lectura.</summary>
    internal SessionRateLimiter RateLimiter { get; } = new();

    /// <summary>Instante monotónico del último frame recibido, para el timeout de inactividad.</summary>
    public long LastInboundMs => Interlocked.Read(ref _lastInboundMs);

    /// <summary>Verdadero si ya se pidió el cierre de esta sesión.</summary>
    public bool IsClosing => Volatile.Read(ref _kicked) != 0;

    /// <summary>Encola un mensaje. Seguro desde cualquier hilo.</summary>
    public void Send<T>(Opcode opcode, T payload)
    {
        if (IsClosing && opcode != Opcode.Kick)
        {
            return;
        }

        if (!_outbound.Writer.TryWrite(FrameCodec.Encode(opcode, payload)))
        {
            _log.Warning("Cola de salida llena en la sesión {SessionId}; se cierra", Id);
            Kick(KickReason.InternalError);
        }
    }

    /// <summary>
    /// Manda <see cref="S2CKick"/> y cierra la conexión. Idempotente y seguro desde cualquier hilo.
    /// </summary>
    public void Kick(KickReason reason, ResultCode detail = ResultCode.Ok)
    {
        if (Interlocked.Exchange(ref _kicked, 1) != 0)
        {
            return;
        }

        _log.Information("Sesión {SessionId} expulsada: {Reason} ({Detail})", Id, reason, detail);

        _outbound.Writer.TryWrite(FrameCodec.Encode(Opcode.Kick, new S2CKick
        {
            Reason = reason,
            Detail = detail,
            ServerProtocolVersion = ProtocolVersion.Current,
        }));

        // Cerrar el escritor hace que el bucle de escritura vacíe lo pendiente, mande el frame
        // de cierre del WebSocket y luego cancele el bucle de lectura.
        _outbound.Writer.TryComplete();
    }

    /// <summary>Ejecuta los dos bucles hasta que la conexión termina. Nunca lanza.</summary>
    public async Task RunAsync(CancellationToken hostToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostToken, _lifetime.Token);
        var sendLoop = SendLoopAsync();

        try
        {
            await ReceiveLoopAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cierre normal: o para el servidor, o el bucle de escritura ya cerró el socket.
        }
        catch (WebSocketException ex)
        {
            _log.Debug(ex, "Sesión {SessionId} cortada por el cliente", Id);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Fallo inesperado en la sesión {SessionId}", Id);
        }
        finally
        {
            State = SessionState.Closing;
            _outbound.Writer.TryComplete();
            await sendLoop.ConfigureAwait(false);
            _lifetime.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        // Un byte de más para poder detectar el frame que se pasa del límite sin quedarnos
        // sin sitio donde recibirlo.
        var buffer = ArrayPool<byte>.Shared.Rent(FrameCodec.MaxFrameBytes + 1);
        try
        {
            while (!token.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var total = 0;
                ValueWebSocketReceiveResult result;

                do
                {
                    result = await _socket
                        .ReceiveAsync(buffer.AsMemory(total, buffer.Length - total), token)
                        .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // El protocolo es binario. Un frame de texto es un cliente que no es
                        // el nuestro, o uno manipulado.
                        Kick(KickReason.ProtocolError);
                        return;
                    }

                    total += result.Count;

                    if (total > FrameCodec.MaxFrameBytes)
                    {
                        _log.Warning("Frame de {Bytes} B en la sesión {SessionId}, por encima del límite", total, Id);
                        Kick(KickReason.ProtocolError);
                        return;
                    }
                }
                while (!result.EndOfMessage);

                Interlocked.Exchange(ref _lastInboundMs, ServerClock.NowMs);
                await HandleFrameAsync(buffer.AsMemory(0, total)).ConfigureAwait(false);

                if (IsClosing)
                {
                    return;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task HandleFrameAsync(ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryReadOpcode(frame.Span, out var opcode))
        {
            _log.Warning("Frame de {Bytes} B sin cabecera en la sesión {SessionId}", frame.Length, Id);
            Kick(KickReason.ProtocolError);
            return;
        }

        if (!OpcodeTable.TryGet(opcode, out var spec) || !spec.IsClientToServer)
        {
            _log.Warning("Opcode desconocido o de servidor {Opcode} en la sesión {SessionId}", opcode, Id);
            Kick(KickReason.ProtocolError);
            return;
        }

        if ((spec.LegalStates & State) == 0)
        {
            _log.Warning("Opcode {Opcode} recibido en estado {State} en la sesión {SessionId}", opcode, State, Id);
            Kick(KickReason.InvalidState, ResultCode.InvalidState);
            return;
        }

        if (!RateLimiter.TryAcquire(spec.Family, ServerClock.NowMs, out var disconnect))
        {
            _log.Warning("Rate limit de {Family} superado en la sesión {SessionId} (strike {Strikes})",
                spec.Family, Id, RateLimiter.Strikes);

            if (disconnect)
            {
                Kick(KickReason.RateLimited, ResultCode.RateLimited);
            }

            return;
        }

        // Login/Register cruzan a Postgres de forma async, awaited aquí mismo: esta sesión no
        // vuelve a leer el socket hasta que responden, pero no bloquea ni el tick de mundo (regla
        // de CLAUDE.md §4) ni las demás sesiones, que corren en su propia tarea.
        await _handler.HandleAsync(this, opcode, frame).ConfigureAwait(false);
    }

    private async Task SendLoopAsync()
    {
        try
        {
            while (await _outbound.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_outbound.Reader.TryRead(out var frame))
                {
                    if (_socket.State != WebSocketState.Open)
                    {
                        return;
                    }

                    await _socket
                        .SendAsync(frame.AsMemory(), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (WebSocketException ex)
        {
            _log.Debug(ex, "Envío cortado en la sesión {SessionId}", Id);
        }
        catch (OperationCanceledException)
        {
            // Cierre en curso.
        }
        finally
        {
            await CloseSocketAsync().ConfigureAwait(false);
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task CloseSocketAsync()
    {
        if (_socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _socket
                .CloseOutputAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // El cliente ya se fue; no hay nada que negociar.
        }
    }
}

using System.Net.WebSockets;
using System.Threading.Channels;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Un <see cref="WebSocket"/> de mentira gobernado por el test: el test decide qué frames "manda
/// el cliente" y cuándo, y puede leer todo lo que la sesión ha enviado.
/// </summary>
internal sealed class FakeWebSocket : WebSocket
{
    private readonly Channel<(WebSocketMessageType Type, byte[] Payload)> _inbound =
        Channel.CreateUnbounded<(WebSocketMessageType, byte[])>();

    private readonly List<byte[]> _sent = [];
    private readonly object _sentLock = new();

    private WebSocketState _state = WebSocketState.Open;

    public override WebSocketCloseStatus? CloseStatus => null;

    public override string? CloseStatusDescription => null;

    public override string? SubProtocol => null;

    public override WebSocketState State => _state;

    /// <summary>Cuántas veces ha llamado la sesión a <see cref="CloseOutputAsync"/>.</summary>
    public int CloseOutputCalls { get; private set; }

    /// <summary>Frames que la sesión ha escrito en el socket, en orden.</summary>
    public IReadOnlyList<byte[]> Sent
    {
        get
        {
            lock (_sentLock)
            {
                return _sent.ToArray();
            }
        }
    }

    /// <summary>Encola un frame binario "del cliente".</summary>
    public void ClientSends(byte[] frame) => _inbound.Writer.TryWrite((WebSocketMessageType.Binary, frame));

    /// <summary>Encola el frame de cierre del cliente, que cierra el apretón de manos.</summary>
    public void ClientCloses() => _inbound.Writer.TryWrite((WebSocketMessageType.Close, []));

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var (type, payload) = await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (type == WebSocketMessageType.Close)
        {
            _state = _state == WebSocketState.CloseSent ? WebSocketState.Closed : WebSocketState.CloseReceived;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true);
        }

        payload.CopyTo(buffer.AsSpan());
        return new WebSocketReceiveResult(payload.Length, WebSocketMessageType.Binary, endOfMessage: true);
    }

    public override Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        lock (_sentLock)
        {
            _sent.Add(buffer.ToArray());
        }

        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        CloseOutputCalls++;
        _state = _state == WebSocketState.CloseReceived ? WebSocketState.Closed : WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

    public override void Abort() => _state = WebSocketState.Aborted;

    public override void Dispose() => _inbound.Writer.TryComplete();
}

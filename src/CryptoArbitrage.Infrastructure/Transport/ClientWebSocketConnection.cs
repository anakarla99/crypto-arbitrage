using System.Net.WebSockets;
using System.Text;

namespace CryptoArbitrage.Infrastructure.Transport;

public sealed class ClientWebSocketConnectionFactory : IWebSocketConnectionFactory
{
    public async Task<IWebSocketConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || !string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("WebSocket endpoint must be an absolute wss URI.", nameof(endpoint));
        }

        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new ClientWebSocketConnection(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

public sealed class ClientWebSocketConnection : IWebSocketConnection
{
    private readonly ClientWebSocket _socket;

    public ClientWebSocketConnection(ClientWebSocket socket)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    public Task SendTextAsync(string payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return _socket.SendAsync(
            Encoding.UTF8.GetBytes(payload),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    public async Task<ReceivedFrame> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var segment = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return new ReceivedFrame(ReadOnlyMemory<byte>.Empty, IsRemoteClose: true);
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new WebSocketException("Only text WebSocket frames are supported.");
            }

            buffer.Write(segment, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return new ReceivedFrame(buffer.ToArray());
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}

using System.Net.WebSockets;
using System.Text;

namespace Hatch.Api.Services.Chat;

/// <summary>
/// The real <see cref="ISocketConnector"/>, over <see cref="ClientWebSocket"/>.
/// A thin wrapper with no test: a test of it would open a real socket.
/// </summary>
public sealed class ClientWebSocketConnector : ISocketConnector
{
    /// <summary>The longest message read, in bytes.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    /// <summary>How often a ping goes out, and how long to wait for its pong, so a connection that goes silent fails.</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);

    public static void Configure(ClientWebSocketOptions options)
    {
        options.KeepAliveInterval = KeepAlive;
        options.KeepAliveTimeout = KeepAlive;
    }

    public async Task<ISocketConnection> ConnectAsync(Uri address, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        Configure(socket.Options);

        try
        {
            await socket.ConnectAsync(address, ct);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new Connection(socket);
    }

    private sealed class Connection(ClientWebSocket socket) : ISocketConnection
    {
        private static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(2);

        public async Task<string?> ReceiveTextAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var message = new MemoryStream();
            var isText = true;

            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close) return null;

                if (message.Length == 0) isText = result.MessageType == WebSocketMessageType.Text;

                if (message.Length + result.Count > MaxMessageBytes)
                {
                    await CloseAsync();
                    throw new SocketMessageTooLargeException();
                }

                if (isText) message.Write(buffer, 0, result.Count);

                if (!result.EndOfMessage) continue;

                if (!isText)
                {
                    // A binary message is none of ours: skip it and read the next.
                    message.SetLength(0);
                    isText = true;
                    continue;
                }

                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }

        public Task SendTextAsync(string text, CancellationToken ct) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, ct);

        public async Task CloseAsync()
        {
            try
            {
                using var limit = new CancellationTokenSource(CloseLimit);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, limit.Token);
            }
            catch
            {
                // Too slow, or already gone: stop waiting. Nothing about why is kept.
                socket.Abort();
            }
        }

        public ValueTask DisposeAsync()
        {
            socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

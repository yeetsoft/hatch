namespace Hatch.Api.Services.Chat;

/// <summary>
/// Opens a WebSocket. A seam so the listener is proved against a fake and no
/// test opens a real connection. A failure to connect throws; the address is
/// the caller's to keep out of anything it writes down.
/// </summary>
public interface ISocketConnector
{
    Task<ISocketConnection> ConnectAsync(Uri address, CancellationToken ct);
}

/// <summary>One open WebSocket, read and written a whole text message at a time.</summary>
public interface ISocketConnection : IAsyncDisposable
{
    /// <summary>
    /// The next whole text message, or null when the other side closed or the
    /// connection ended. Binary messages are skipped. Throws
    /// <see cref="SocketMessageTooLargeException"/> (after closing) for a
    /// message past <see cref="ClientWebSocketConnector.MaxMessageBytes"/>, and
    /// <see cref="System.Net.WebSockets.WebSocketException"/> when the connection fails.
    /// </summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);

    Task SendTextAsync(string text, CancellationToken ct);

    /// <summary>Closes our side politely, waiting no longer than two seconds, then aborts. Never throws.</summary>
    Task CloseAsync();
}

/// <summary>A message was longer than the connection allows. Carries no part of it.</summary>
public sealed class SocketMessageTooLargeException() : Exception("A message was too large");

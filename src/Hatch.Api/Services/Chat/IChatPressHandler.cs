namespace Hatch.Api.Services.Chat;

/// <summary>
/// What is done with a button press once a provider has read it. Declared
/// here and registered by nothing yet: the listener that calls it resolves it.
/// </summary>
public interface IChatPressHandler
{
    Task HandleAsync(int workspaceId, ChatPress press, CancellationToken ct);
}

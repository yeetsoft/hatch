using Hatch.Api.Modules.Hatch;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Services.Chat;

/// <summary>
/// Links back into Hatch for a chat message, built from the install's canonical
/// address (<see cref="HatchOptions.PublicBaseUrl"/>). Where that is unset or
/// unusable there is no honest link to give, so each method returns null and
/// the message shows plain text.
/// </summary>
public sealed class ChatLinks(IOptions<HatchOptions> options)
{
    private string? Origin => HatchOptions.NormalizeBaseUrl(options.Value.PublicBaseUrl);

    /// <summary>True when links can be built at all.</summary>
    public bool On => Origin is not null;

    /// <summary>The page for an issue, or null.</summary>
    public string? Issue(string key) => Origin is { } origin ? $"{origin}/issues/{key}" : null;

    /// <summary>The page where questions are answered, or null.</summary>
    public string? Ballot() => Origin is { } origin ? $"{origin}/ballot" : null;

    /// <summary>
    /// Where a person confirms a Slack link: Settings, with the secret in the
    /// query, which the page reads and posts. Null when there is no address.
    /// </summary>
    public string? LinkBack(string secret) =>
        Origin is { } origin ? $"{origin}/settings?chatLink={Uri.EscapeDataString(secret)}" : null;

    /// <summary>The goose's page, where its findings are read, or null.</summary>
    public string? Goose() => Origin is { } origin ? $"{origin}/goose" : null;
}

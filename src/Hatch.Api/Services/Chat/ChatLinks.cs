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
}

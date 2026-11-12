using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Who may change a playbook: a person at a browser, and nobody else.
/// </summary>
/// <remarks>
/// <see cref="RequireRoleAttribute"/> already refuses a key on a route that accepts
/// none, but a keyless runner and a plain script reach the controller as a
/// person with the wall off. A real browser's fetch sets <c>Sec-Fetch-Mode</c>
/// on every request it makes, navigation or <c>fetch()</c>, so its absence
/// is what a program looks like, and the check needs no branch on the wall.
/// </remarks>
public static class PlaybookWrites
{
    /// <summary>What a refused caller is told, and where to go instead.</summary>
    public const string Sentence =
        "a playbook is changed by a person at the browser - a session proposes the change instead: POST /api/playbook-proposals";

    /// <summary>The refusal for a caller that is not a person at a browser, or null for one that is.</summary>
    public static async Task<ObjectResult?> Refusal(ICallerIdentity caller, HttpRequest request, CancellationToken ct)
    {
        if (await caller.IsProgramAsync(ct)) return Refuse();
        if (!request.Headers.ContainsKey(AuthChallenge.SecFetchMode)) return Refuse();
        return null;
    }

    private static ObjectResult Refuse() => new(Sentence) { StatusCode = StatusCodes.Status403Forbidden };
}

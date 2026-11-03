using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Whether a story's or a bug's tasks share its branch and one pull request -
/// its own say, over its project's default. See
/// <see cref="EfHatchIssue.OnePullRequest"/>.
/// </summary>
/// <remarks>
/// <para>Its own controller, and open to a key where its neighbours
/// (<see cref="IssueExpediteController"/>, <see cref="IssuePreApprovalController"/>)
/// are not. Priority and pre-approval decide which tickets the loop reaches for
/// and which gates it may pass unattended, so a key that could set them could
/// advance its own ticket. This decides only how a story is branched, which is
/// the author of the story's call and gives a key nothing it could not already
/// do - it is the project's default, which changes how every story and bug is worked,
/// that is an owner's (<see cref="ProjectsController.PutLanding"/>).</para>
///
/// <para>Locked once any work under the story has started. A task begun on its
/// own branch cannot be moved onto the story's, and a task begun on the story's
/// cannot be given a pull request of its own, so changing the setting under
/// either would leave work that no longer agrees with the story it is part of.
/// "Started" is a descendant standing in the implementation column or any
/// column to its right, a terminal one included; a shelved descendant has not
/// started. Setting the value the story already holds is never refused.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueOnePrController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    public const string On = "on";
    public const string Off = "off";
    public const string Inherit = "inherit";

    /// <summary>
    /// Set the override by name - <c>on</c>, <c>off</c> or <c>inherit</c> (the
    /// project's default) - see <see cref="OnePrRequest"/>. Stories and bugs only.
    /// </summary>
    /// <remarks>
    /// Setting the value the story already holds writes nothing and does not
    /// move <c>UpdatedAt</c>, the same as every other edit in Hatch - so a
    /// control pressed twice leaves one trail entry and not two.
    /// </remarks>
    [HttpPut("{key}/one-pr")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IssueDto>> PutIssueOnePr(string key, OnePrRequest request, CancellationToken ct)
    {
        if (!TryParse(request.Value, out var value))
            return BadRequest(UnknownValue(request.Value));

        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (issue.Type is not ("story" or "bug"))
            return BadRequest(NotAStoryOrBug($"{key} is a {issue.Type}"));

        if (value != issue.OnePullRequest)
        {
            if (await OnePrLock.FirstStartedAsync(db, issue, ct) is { } started)
                return Conflict(
                    $"{key} has work under it that has already started ({started}), so whether its tasks share one " +
                    "pull request can no longer be changed - a task begun on one branch cannot be moved onto the other");

            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.OnePrChanged,
                Payload = JsonSerializer.Serialize(new { from = Name(issue.OnePullRequest), to = Name(value) }),
                At = now,
            });

            issue.OnePullRequest = value;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    internal static bool TryParse(string? text, out bool? value)
    {
        value = null;
        switch (text?.Trim().ToLowerInvariant())
        {
            case On: value = true; return true;
            case Off: value = false; return true;
            case Inherit: return true;
            default: return false;
        }
    }

    internal static string Name(bool? value) => value switch { true => On, false => Off, null => Inherit };

    /// <summary>
    /// The refusal for a value that is none of the three, shared with the
    /// create endpoint so the two cannot drift.
    /// </summary>
    internal static string UnknownValue(string? value) =>
        $"\"{value}\" is not a one-PR setting - {On}, {Off} or {Inherit}";

    /// <summary>
    /// The refusal for an issue that is neither a story nor a bug; <paramref name="what"/>
    /// names it - by key where one exists, "this" where it is still being filed.
    /// </summary>
    internal static string NotAStoryOrBug(string what) =>
        $"only a story or a bug carries an override for sharing one pull request across its tasks - {what}";
}

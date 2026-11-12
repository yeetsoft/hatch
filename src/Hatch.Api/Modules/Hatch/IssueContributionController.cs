using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// That an issue came from outside. See <see cref="EfHatchContribution"/>.
/// </summary>
/// <remarks>
/// Its own controller, and open to a key the same way <see
/// cref="IssueNoCodeController"/> is: a runner is the one that sees a pull
/// request arrive, and filing it gives a key no power over the board. `PATCH`,
/// create and the bulk endpoint carry nothing for it; this route is the only
/// way in.
/// </remarks>
[ApiController]
[Route("api/issues")]
public class IssueContributionController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Record that the issue came from outside. Write-once: a write whose pull
    /// request URL is the one already recorded (null is equal to null) returns
    /// the issue and writes nothing, whatever else the body says; a different
    /// URL on an issue that has a record is a <c>409</c>, as is a URL that
    /// another issue's record holds, and an issue whose own pull request is a
    /// different one. Where the issue has no pull request of its own, the
    /// record's becomes it, in the same write.
    /// </summary>
    [HttpPut("{key}/contribution")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IssueDto>> PutIssueContribution(string key, ContributionRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        if (!TryRead(request, out var read, out var error)) return BadRequest(error);

        var issue = await db.Issues
            .Include(i => i.Contribution)
            .Include(i => i.Project)
            .WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (issue.Contribution is { } existing)
        {
            if (existing.PullRequestUrl != read.PullRequestUrl)
                return Conflict($"{key} already has a contribution record, for {existing.PullRequestUrl ?? "no pull request"} - a record is written once");

            return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
        }

        // By query, with the unique index as the backstop: the in-memory
        // provider the tests run on does not enforce an index.
        if (read.PullRequestUrl is { } url)
        {
            var holder = await db.Contributions.AsNoTracking()
                .Where(c => c.PullRequestUrl == url && c.IssueId != issue.Id)
                .Select(c => new { c.Issue!.Project!.Key, c.Issue.Number })
                .FirstOrDefaultAsync(ct);
            if (holder is not null)
                return Conflict($"{url} is already the contribution of {IssueKey.Format(holder.Key, holder.Number)}");

            if (!string.IsNullOrWhiteSpace(issue.PullRequestUrl) && issue.PullRequestUrl != url)
                return Conflict($"{key} already has a different pull request recorded ({issue.PullRequestUrl})");
        }

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var record = new EfHatchContribution
        {
            IssueId = issue.Id,
            PullRequestUrl = read.PullRequestUrl,
            HeadBranch = read.HeadBranch,
            AuthorLogin = read.AuthorLogin,
            AuthorAssociation = read.AuthorAssociation,
            FiledAt = now,
            FiledBy = actor,
            OriginInstance = read.OriginInstance,
            OriginProjectKey = read.OriginProjectKey,
            OriginKey = read.OriginKey,
            EnvelopeVersion = read.EnvelopeVersion,
            EnvelopeNote = read.EnvelopeNote,
        };
        issue.Contribution = record;

        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.ContributionFiled,
            Payload = JsonSerializer.Serialize(IssueEventPayload.ContributionFiled(record)),
            At = now,
        });

        if (read.PullRequestUrl is { } recorded && string.IsNullOrWhiteSpace(issue.PullRequestUrl))
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.PullRequestChanged,
                Payload = JsonSerializer.Serialize(new { from = issue.PullRequestUrl, to = recorded }),
                At = now,
            });

            // A pull request is a branch: the issue did not finish with no code.
            if (NoCode.Declared(issue))
                issue.Events.Add(NoCode.SetAside(issue, new { pullRequest = recorded }, actor, now));

            issue.PullRequestUrl = recorded;
        }

        issue.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (read.PullRequestUrl is not null)
        {
            // The unique index on the URL: somebody filed it between the read
            // above and this write.
            return Conflict($"{read.PullRequestUrl} is already the contribution of another issue");
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, now, ct);
    }

    /// <summary>The request after the checks and the tidying a write needs.</summary>
    private sealed record Read(
        string? PullRequestUrl, string? HeadBranch, string AuthorLogin, string AuthorAssociation,
        string? OriginInstance, string? OriginProjectKey, string? OriginKey,
        int? EnvelopeVersion, string? EnvelopeNote);

    private static bool TryRead(ContributionRequest request, out Read read, out string? error)
    {
        read = null!;

        var login = request.AuthorLogin?.Trim();
        if (string.IsNullOrEmpty(login)) { error = "authorLogin is required"; return false; }
        var association = request.AuthorAssociation?.Trim();
        if (string.IsNullOrEmpty(association)) { error = "authorAssociation is required"; return false; }

        if (!IssuesController.ReadPullRequestUrl(request.PullRequestUrl, out var url, out error)) return false;

        var branch = Blank(request.HeadBranch);
        var instance = Blank(request.OriginInstance) is { } i ? IssueContributions.NormalizeInstance(i) : null;
        var originProject = Blank(request.OriginProjectKey);
        var originKey = Blank(request.OriginKey);
        var note = Blank(request.EnvelopeNote);

        if ((instance is null) != (originKey is null))
        {
            error = "originInstance and originKey come together or not at all";
            return false;
        }

        if (!Within(login, ContributionRequest.MaxAuthorLoginLength, "authorLogin", out error)
            || !Within(association, ContributionRequest.MaxAuthorAssociationLength, "authorAssociation", out error)
            || !Within(branch, ContributionRequest.MaxHeadBranchLength, "headBranch", out error)
            || !Within(instance, ContributionRequest.MaxOriginInstanceLength, "originInstance", out error)
            || !Within(originProject, ContributionRequest.MaxOriginProjectKeyLength, "originProjectKey", out error)
            || !Within(originKey, ContributionRequest.MaxOriginKeyLength, "originKey", out error)
            || !Within(note, ContributionRequest.MaxEnvelopeNoteLength, "envelopeNote", out error))
            return false;

        read = new Read(url, branch, login, association, instance, originProject, originKey, request.EnvelopeVersion, note);
        error = null;
        return true;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool Within(string? text, int max, string name, out string? error)
    {
        error = text is not null && text.Length > max ? $"{name} is at most {max} characters" : null;
        return error is null;
    }
}

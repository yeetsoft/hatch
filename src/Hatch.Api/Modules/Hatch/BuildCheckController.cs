using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where a runner writes down whether the build on an issue's branch passed -
/// see <see cref="EfHatchBuildCheck"/> for what a verdict is and why there is
/// one per repository.
///
/// A key may write one, for the reason <see cref="MergeCheckController"/>
/// gives: the caller is a runner, and a verdict only a person could enter would
/// be one nobody ever entered. It is a fact about a sha on origin that any
/// runner reads the same way, and a wrong one costs a question.
/// </summary>
/// <remarks>
/// <para>There is no <c>GET</c>: the verdicts ride <see cref="IssueDto"/> and
/// <see cref="ReviewCheckDto"/>, where every client needs them.</para>
///
/// <para>Nothing here looks at the issue's column. A build increment pushes,
/// and says so, before anybody has moved the ticket.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues/{key}/build-check")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class BuildCheckController(
    HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Replaces the verdict for this issue and repository, and says what it now
    /// holds.
    /// </summary>
    /// <remarks>
    /// <para>An event is written when the verdict, the sha or the set of
    /// failing names differs from what was stored; the first verdict for a
    /// repository counts. A verdict that repeats the stored one refreshes the
    /// time and the runner and writes nothing, so a poll that looks every
    /// interval leaves one entry rather than a hundred. The mark alone writes
    /// none either.</para>
    ///
    /// <para>The sha decides what carries over. On the same sha
    /// <see cref="EfHatchBuildCheck.ShaSince"/> is kept and
    /// <see cref="EfHatchBuildCheck.PushedByIncrement"/> is the stored value or
    /// the request's - it never falls back to false. On a different sha both
    /// start again, the flag from the request.</para>
    ///
    /// <para>A mark never lowers a concluded verdict. The runner that pushed
    /// can arrive after another runner's poll has already read the build, so a
    /// mark of <c>pending</c> on the sha the row holds, over <c>passed</c> or
    /// <c>failed</c>, takes the flag and keeps the verdict. Any other request
    /// stores what it says: a poll's <c>pending</c> after a <c>failed</c> is a
    /// re-run.</para>
    ///
    /// <para>The failed-again question goes up when a save moves the row into
    /// <c>failed</c> and marked - whichever of the two arrived last - unless a
    /// question is already open on the issue, which is already the flag. It is
    /// written in the same save as the row.</para>
    /// </remarks>
    [HttpPut]
    public async Task<ActionResult<BuildCheckDto>> PutBuildCheck(
        string key, BuildCheckRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var remote = request.Remote?.Trim() ?? "";
        var (canonical, error) = RemoteIdentity.Canonical(remote);
        if (canonical is null) return BadRequest(error);
        if (remote.Length > EfHatchMergeCheck.MaxRemoteLength || canonical.Length > EfHatchMergeCheck.MaxRemoteLength)
            return BadRequest($"a remote is at most {EfHatchMergeCheck.MaxRemoteLength} characters");

        var verdict = request.Verdict?.Trim() ?? "";
        if (!BuildVerdicts.All.Contains(verdict))
            return BadRequest($"a verdict is one of {string.Join(", ", BuildVerdicts.All)}");

        var branch = request.Branch?.Trim() ?? "";
        if (branch.Length == 0) return BadRequest("a verdict names the branch it was taken on");
        if (branch.Length > EfHatchMergeCheck.MaxRefLength)
            return BadRequest($"a branch is at most {EfHatchMergeCheck.MaxRefLength} characters");

        var sha = request.Sha?.Trim() ?? "";
        if (sha.Length == 0) return BadRequest("a verdict names the sha the branch stood at");
        if (sha.Length > EfHatchMergeCheck.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchMergeCheck.MaxShaLength} characters");

        // Deduplicated by name and sorted, so that the same failure listed in
        // another order by another runner is the same verdict and not a change.
        var failing = new List<FailingCheckDto>();
        if (verdict == BuildVerdicts.Failed)
        {
            var named = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var check in request.Failing ?? [])
            {
                var name = check?.Name?.Trim() ?? "";
                if (name.Length == 0) return BadRequest("a failing check has a name");
                if (name.Length > EfHatchBuildCheck.MaxNameLength)
                    return BadRequest($"a failing check's name is at most {EfHatchBuildCheck.MaxNameLength} characters");
                if (name.Contains('\n') || name.Contains('\r'))
                    return BadRequest("a failing check's name has no line break in it");

                var url = check!.Url?.Trim();
                if (url is { Length: > EfHatchBuildCheck.MaxUrlLength })
                    return BadRequest($"a failing check's url is at most {EfHatchBuildCheck.MaxUrlLength} characters");

                // A key writes these and the issue page links them, so only a
                // web address is kept: anything else is stored as nothing
                // rather than refused, because the check still failed.
                var safe = SafeUrl(url);
                if (!named.TryGetValue(name, out var kept) || kept is null) named[name] = safe;
            }

            if (named.Count == 0) return BadRequest("a failed verdict names the checks that failed");
            if (named.Count > EfHatchBuildCheck.MaxFailing)
                return BadRequest($"a verdict names at most {EfHatchBuildCheck.MaxFailing} failing checks");

            failing = named.OrderBy(n => n.Key, StringComparer.Ordinal)
                .Select(n => new FailingCheckDto(n.Key, n.Value)).ToList();
        }

        var runner = request.Runner?.Trim() ?? "";
        if (runner.Length == 0) return BadRequest("a verdict names the runner that took it");
        if (runner.Length > ClaimRequest.MaxRunnerLength)
            return BadRequest($"a runner is at most {ClaimRequest.MaxRunnerLength} characters");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var row = await db.BuildChecks.FirstOrDefaultAsync(b => b.IssueId == issue.Id && b.Canonical == canonical, ct);

        var sameSha = row is not null && row.Sha == sha;
        var storedFailing = row is null ? [] : EfHatchBuildCheck.ReadFailing(row.Failing);

        // A mark that arrives after the poll already concluded: keep what the
        // poll read, take the flag.
        if (sameSha && request.PushedByIncrement
            && verdict == BuildVerdicts.Pending
            && row!.Verdict is BuildVerdicts.Passed or BuildVerdicts.Failed)
        {
            verdict = row.Verdict;
            failing = storedFailing.ToList();
        }

        var flagged = sameSha ? row!.PushedByIncrement || request.PushedByIncrement : request.PushedByIncrement;
        var wasFailedAndFlagged = sameSha && row!.Verdict == BuildVerdicts.Failed && row.PushedByIncrement;

        var changed = row is null
            || row.Sha != sha
            || row.Verdict != verdict
            || !storedFailing.Select(f => f.Name).SequenceEqual(failing.Select(f => f.Name), StringComparer.Ordinal);
        var from = row is null ? null : Side(row.Verdict, storedFailing);

        if (row is null)
        {
            row = new EfHatchBuildCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical,
                Branch = branch,
                Sha = sha,
                ShaSince = now,
                Verdict = verdict,
                CheckedAt = now,
                Runner = runner,
            };
            db.BuildChecks.Add(row);
        }

        if (!sameSha) row.ShaSince = now;

        row.Remote = remote;
        row.Branch = branch;
        row.Sha = sha;
        row.Verdict = verdict;
        row.Failing = EfHatchBuildCheck.WriteFailing(failing);
        row.PushedByIncrement = flagged;
        row.CheckedAt = now;
        row.Runner = runner;

        if (changed)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.BuildCheckChanged,

                // The remote in its canonical form, as the merge check's is.
                Payload = JsonSerializer.Serialize(new { remote = canonical, sha, from, to = Side(verdict, failing) }),
                At = now,
            });
        }

        if (verdict == BuildVerdicts.Failed && flagged && !wasFailedAndFlagged
            && !await Questions.Open(db).AnyAsync(c => c.IssueId == issue.Id, ct))
        {
            db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id,
                Author = actor,
                Body = QuestionBody(issue.Number, projectKey, branch, sha, failing),
                Kind = EfHatchComment.Question,
                Options = Questions.WriteOptions(StallQuestion.Options),
                CreatedAt = now,
            });

            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = actor,
                Kind = EfHatchIssueEvent.Asked,
                At = now,
            });
        }

        await db.SaveChangesAsync(ct);

        return IssueBuildChecks.Project(row);
    }

    private static string QuestionBody(
        int number, string projectKey, string branch, string sha, IReadOnlyList<FailingCheckDto> failing) =>
        $"A build increment pushed {sha} to {branch}, and the build on it failed: "
        + $"{string.Join(", ", failing.Select(f => f.Name))}. "
        + $"What should happen to {IssueKey.Format(projectKey, number)} now?";

    /// <summary>The address if it is an absolute http or https one - the test a pull request url is held to - and null otherwise.</summary>
    private static string? SafeUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;

    /// <summary>One side of a change: the verdict, and the names it failed. Spelled out rather than handed a record, as every payload in this module is - a record would serialise with capitals.</summary>
    private static object Side(string verdict, IReadOnlyList<FailingCheckDto> failing) =>
        new { verdict, failing = failing.Select(f => f.Name).ToList() };
}

/// <summary>The stored verdict, as a client reads it.</summary>
public static class IssueBuildChecks
{
    public static BuildCheckDto Project(EfHatchBuildCheck b) => new(
        b.Remote, b.Canonical, b.Branch, b.Sha, b.Verdict,
        EfHatchBuildCheck.ReadFailing(b.Failing), b.Runner, b.PushedByIncrement, b.ShaSince, b.CheckedAt);
}

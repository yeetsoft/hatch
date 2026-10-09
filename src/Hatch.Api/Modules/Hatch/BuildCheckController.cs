using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where a runner writes down what the build on the tip of an issue's branch
/// came to - see <see cref="EfHatchBuildCheck"/> for what a verdict is and why
/// there is one per repository.
///
/// A key may write one, for the reason it may write a merge check: the caller is
/// a runner, and a verdict only a person could enter would be one nobody ever
/// entered. It is a fact about a sha that any runner reads the same way, and a
/// wrong one costs a stall, which is a question.
/// </summary>
/// <remarks>
/// <para>There is no <c>GET</c>: the verdicts ride <see cref="IssueDto"/> and
/// <see cref="ReviewCheckDto"/>, as the merge checks do.</para>
///
/// <para>Nothing here looks at the issue's column. A build increment reports the
/// tip it pushed before anybody has moved the ticket, and the rule that only an
/// issue in review is <em>dispatched</em> on a verdict belongs to
/// <see cref="WorkController"/>.</para>
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
    /// <para>Four rules decide what is stored, and they hold whichever order two
    /// runners' calls arrive in. Another runner's poll can read the build
    /// between an increment's push and the increment marking it, so the mark can
    /// arrive after <c>failed</c>.</para>
    ///
    /// <list type="number">
    /// <item>The flag is <c>stored || request</c> for the same sha and the
    /// request's for a new one: it never falls back to false on the same
    /// sha.</item>
    /// <item>A mark never lowers a concluded verdict. A request that says it was
    /// pushed by an increment, on the sha the row holds, with <c>pending</c>
    /// while the row holds <c>passed</c> or <c>failed</c>, takes the flag and
    /// keeps the verdict. Every other request stores what it says: a poll's
    /// <c>pending</c> after a <c>failed</c> is a re-run.</item>
    /// <item><see cref="EfHatchBuildCheck.ShaSince"/> is kept for the same sha
    /// and is now for a new one.</item>
    /// <item>The question opens when a save moves the row into <em>failed and
    /// flagged</em>. That covers <c>pending</c> to <c>failed</c> on a marked
    /// sha and <c>failed</c> read first and marked after, and it writes one
    /// question however often the verdict repeats.</item>
    /// </list>
    ///
    /// <para>The event is written when the verdict, the sha or the set of
    /// failing names changes, and the first verdict for a repository counts. The
    /// flag alone writes none.</para>
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
        if (remote.Length > EfHatchBuildCheck.MaxRemoteLength || canonical.Length > EfHatchBuildCheck.MaxRemoteLength)
            return BadRequest($"a remote is at most {EfHatchBuildCheck.MaxRemoteLength} characters");

        var verdict = request.Verdict?.Trim() ?? "";
        if (!BuildVerdicts.All.Contains(verdict))
            return BadRequest($"a verdict is one of {string.Join(", ", BuildVerdicts.All)}");

        // A build is about a branch and a sha, for every verdict: there is no
        // none-without-a-branch, because none is "no check ran on this sha".
        var branch = request.Branch?.Trim() ?? "";
        if (branch.Length == 0) return BadRequest("a verdict names the branch it is about");
        if (branch.Length > EfHatchBuildCheck.MaxRefLength)
            return BadRequest($"a branch is at most {EfHatchBuildCheck.MaxRefLength} characters");

        var sha = request.Sha?.Trim() ?? "";
        if (sha.Length == 0) return BadRequest("a verdict names the sha it is about");
        if (sha.Length > EfHatchBuildCheck.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchBuildCheck.MaxShaLength} characters");

        // Failing checks on a verdict that is not failed or pending are ignored
        // rather than refused. Deduplicated by name and sorted, so the same
        // failure listed in another order by another runner is the same verdict
        // and not a change. Only a failed verdict must name at least one - a
        // pending verdict with nothing failed yet is legitimate.
        List<FailingCheckDto> failing = [];
        if (verdict is BuildVerdicts.Failed or BuildVerdicts.Pending)
        {
            var named = request.Failing ?? [];
            if (named.Count > EfHatchBuildCheck.MaxFailing)
                return BadRequest($"a verdict names at most {EfHatchBuildCheck.MaxFailing} failing checks");
            if (named.Any(f => f is null))
                return BadRequest("a failed verdict names the checks that failed");
            if (verdict == BuildVerdicts.Failed && named.Count == 0)
                return BadRequest("a failed verdict names the checks that failed");
            if (named.Any(f => (f.Name?.Trim() ?? "").Length is 0 or > EfHatchBuildCheck.MaxCheckNameLength
                    || f.Name!.Contains('\n') || f.Name.Contains('\r')))
                return BadRequest($"a check's name is 1 to {EfHatchBuildCheck.MaxCheckNameLength} characters with no line break in it");
            if (named.Any(f => f.Url is { Length: > EfHatchBuildCheck.MaxCheckUrlLength }))
                return BadRequest($"a check's url is at most {EfHatchBuildCheck.MaxCheckUrlLength} characters");

            failing = Failing(named);
        }

        var runner = request.Runner?.Trim() ?? "";
        if (runner.Length == 0) return BadRequest("a verdict names the runner that took it");
        if (runner.Length > ClaimRequest.MaxRunnerLength)
            return BadRequest($"a runner is at most {ClaimRequest.MaxRunnerLength} characters");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var row = await db.BuildChecks.FirstOrDefaultAsync(b => b.IssueId == issue.Id && b.Canonical == canonical, ct);

        var sameSha = row is not null && row.Sha == sha;
        var wasFailedAndFlagged = sameSha && row!.Verdict == BuildVerdicts.Failed && row.PushedByIncrement;

        // Rule two: a mark does not lower what has concluded on the same sha.
        var keepStored = sameSha && request.PushedByIncrement
            && verdict == BuildVerdicts.Pending
            && row!.Verdict is BuildVerdicts.Passed or BuildVerdicts.Failed;
        if (keepStored)
        {
            verdict = row!.Verdict;
            failing = EfHatchBuildCheck.ReadFailing(row.Failing).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        }

        var storedFailing = row is null ? [] : EfHatchBuildCheck.ReadFailing(row.Failing);
        var changed = row is null || row.Verdict != verdict || row.Sha != sha
            || !storedFailing.Select(f => f.Name).SequenceEqual(failing.Select(f => f.Name));
        var from = row is null ? null : Side(row.Verdict, row.Sha, storedFailing);

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
                CheckedBy = actor,
            };
            db.BuildChecks.Add(row);
        }

        row.PushedByIncrement = sameSha ? row.PushedByIncrement || request.PushedByIncrement : request.PushedByIncrement;
        if (!sameSha) row.ShaSince = now;

        row.Remote = remote;
        row.Branch = branch;
        row.Sha = sha;
        row.Verdict = verdict;
        row.Failing = EfHatchBuildCheck.WriteFailing(failing);
        row.CheckedAt = now;
        row.Runner = runner;
        row.CheckedBy = actor;

        if (changed)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.BuildCheckChanged,

                // The canonical remote, for the reason the merge check's is.
                Payload = JsonSerializer.Serialize(new { remote = canonical, from, to = Side(verdict, sha, failing) }),
                At = now,
            });
        }

        // Rule four. Skipped when a question is already open: that is already
        // the flag, and a second under it says no more than the first. Reads
        // Open rather than Waiting on purpose - this is deciding whether to ask
        // a fresh one, not whether to keep counting an old one, and narrowing it
        // would start asking stall questions again on a ticket that has already
        // shipped.
        var failedAndFlagged = row.Verdict == BuildVerdicts.Failed && row.PushedByIncrement;
        if (failedAndFlagged && !wasFailedAndFlagged
            && !await Questions.Open(db).AnyAsync(c => c.IssueId == issue.Id, ct))
        {
            db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id,
                Author = actor,
                Kind = EfHatchComment.Question,
                Body = QuestionBody(key, sha, failing),
                Options = Questions.WriteOptions(StallAnswers.Options()),
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

    private static string QuestionBody(string key, string sha, IReadOnlyList<FailingCheckDto> failing) =>
        $"""
        An unattended build increment pushed {Short(sha)} to fix this branch's failing build, and the
        build on that push has failed again. The checks that still fail:

        {string.Join('\n', failing.Select(f => $"- {f.Name}"))}

        The board does not send another agent at a build that failed on an agent's own fix.
        Nothing further will be dispatched at {key} until somebody answers this.
        """;

    private static string Short(string sha) => sha.Length > 10 ? sha[..10] : sha;

    /// <summary>The names, trimmed, deduplicated and sorted, each with its link only where it is a web address a page may draw.</summary>
    private static List<FailingCheckDto> Failing(IReadOnlyList<FailingCheckDto>? failing) =>
        (failing ?? [])
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => new FailingCheckDto(f.Name.Trim(), SafeUrl(f.Url)))
            .GroupBy(f => f.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// A key writes these and the issue page draws them as links, so a value that
    /// is not an absolute http or https address is stored as nothing rather than
    /// as something a browser would run.
    /// </summary>
    private static string? SafeUrl(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > EfHatchBuildCheck.MaxCheckUrlLength) return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }

    /// <summary>One side of a change, spelled out as every payload in this module is.</summary>
    private static object Side(string verdict, string sha, IReadOnlyList<FailingCheckDto> failing) =>
        new { verdict, sha, failing = failing.Select(f => f.Name).ToList() };
}

/// <summary>The stored verdict, as a client reads it.</summary>
public static class IssueBuildChecks
{
    public static BuildCheckDto Project(EfHatchBuildCheck b) => new(
        b.Remote, b.Canonical, b.Branch, b.Sha, b.ShaSince, b.Verdict,
        EfHatchBuildCheck.ReadFailing(b.Failing), b.PushedByIncrement, b.CheckedAt, b.Runner, b.CheckedBy);
}

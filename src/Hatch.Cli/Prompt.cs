using System.Globalization;
using System.Text;

namespace Hatch.Cli;

/// <summary>
/// The whole instruction handed to a spawned session: the playbook first,
/// because it says what kind of job this is, then the ticket it is a job about.
/// </summary>
/// <remarks>
/// Composed here rather than stored whole in the database so that a playbook
/// stays a method - one row that reads sensibly for every ticket it will ever
/// be applied to.
/// </remarks>
public static class Prompt
{
    public static string Compose(
        WorkDto work,
        IReadOnlyList<Checkouts.RepositoryLine>? repositories = null,
        IReadOnlyList<BranchEntry>? branches = null,
        Rechecked? conflict = null,
        BuildFound? build = null)
    {
        var issue = work.Issue;
        var key = issue.Key;
        var to = work.ToStatus?.Name ?? "?";

        // A conflict dispatch starts and ends in one column, so an arrow to it
        // would read "In Review -> In Review". Its trunk is the fresh verdict's
        // where the runner took one, and the board's where it did not.
        var isConflict = work.Kind == WorkKinds.Conflicts;
        var isBuild = work.Kind == WorkKinds.Build;
        var trunk = conflict?.Trunk ?? Conflicts.Trunk(issue);
        var lines = new List<string>
        {
            work.Playbook?.Prompt ?? "",
            "",
            "---",
            "",
            "## The ticket",
            "",
            $"{key}  [{issue.Type}]  {issue.Title}",
            isConflict ? $"moving:   {work.FromStatus.Name} (resolving conflicts with {trunk})"
            : isBuild ? $"moving:   {work.FromStatus.Name} (fixing its failing build)"
            : $"moving:   {work.FromStatus.Name} -> {to}",
        };

        if (issue.ParentKey is { Length: > 0 }) lines.Add($"parent:   {issue.ParentKey}");
        if (issue.ReadyAt is { Length: > 0 }) lines.Add($"ready:    {issue.ReadyAt}");
        if (issue.DueAt is { Length: > 0 }) lines.Add($"due:      {issue.DueAt}");

        lines.Add("");
        lines.Add(issue.Description.Length > 0
            ? issue.Description
            : "_No description. That is itself worth noting on the ticket._");
        lines.Add("");

        if (work.NextBriefLimit is { } limit)
        {
            lines.Add("## How long the brief may be");
            lines.Add("");
            lines.Add($"The next increment reads a brief of at most {limit.ToString("N0", CultureInfo.InvariantCulture)} characters; this one is {issue.Description.Length.ToString("N0", CultureInfo.InvariantCulture)}.");
            lines.Add("The board sends a longer description back for another analysis session, so write");
            lines.Add("the brief under that - and where the plan will not fit, cut it into child tasks");
            lines.Add("that each do.");
            lines.Add("");
        }

        if (repositories is { Count: > 0 })
        {
            lines.Add("## Repositories");
            lines.Add("");
            lines.AddRange(repositories.Select(r => r.Path is { } path
                ? $"{path}  {r.Remote}{(r.Primary ? "  (primary)" : "")}"
                : $"{r.Remote}  (no checkout here)"));
            lines.Add("");
        }

        // After the repositories, which it refers to, and before the branch,
        // which says what state the tree is in and does not repeat this.
        if (isConflict) lines.AddRange(Conflict(Conflicts.Facts(work, conflict), trunk));
        if (isBuild) lines.AddRange(FailingBuilds(Builds.Facts(work, build)));
        if (work.LastCheckFindings is { Length: > 0 } findings) lines.AddRange(LastCheck(findings));

        var sharedStory = Branches.IsShared(work) ? work.BranchKey : null;
        if (branches is { Count: > 0 }) lines.AddRange(Branch(branches, sharedStory));

        if (work.Children.Count > 0)
        {
            lines.Add("## Its children");
            lines.Add("");
            lines.AddRange(work.Children.Select(c =>
            {
                var column = work.Statuses?.FirstOrDefault(s => s.Id == c.StatusId)?.Name ?? c.StatusId.ToString();
                return $"- {c.Key}  [{c.Type}]  {c.Title}  ({column})";
            }));
            lines.Add("");
        }

        // A ticket one or more increments in a row have already spent money on
        // and left where it was. Said before the decisions, which are the
        // newer and more specific of the two: this is the older, more general
        // context a session should read first.
        if (work.LetGo > 0)
        {
            lines.Add("## This ticket was let go");
            lines.Add("");
            lines.Add($"The last {work.LetGo} increment(s) on this ticket ended without moving it, and were");
            lines.Add("let go rather than flagged. The comments on the ticket say why, and what each one");
            lines.Add("left behind - read them, and continue that work rather than starting over.");
            lines.Add("");
        }

        // A ticket a person marked themselves, rather than an increment
        // spending itself with nothing to show - handed over directly, in
        // place of a question and an answer nobody typed in the comment
        // thread.
        if (issue.StalledWhy is { Length: > 0 } stalledWhy)
        {
            lines.Add("## This ticket stalled");
            lines.Add("");
            lines.Add(stalledWhy);
            lines.Add("");
            lines.Add(work.LetGo > 1
                ? $"It has stalled {work.LetGo} times in a row."
                : "The loop folded it rather than redispatching blindly, and is handing it back now.");
            lines.Add("");
        }

        // The newer and more specific of the three server-side "why you're
        // here" facts - said after LetGo and StalledWhy, and before the
        // decisions, which are newer still.
        if (work.SentBack is { } sentBack)
        {
            lines.Add("## This ticket was sent back");
            lines.Add("");
            lines.Add($"Its brief was {sentBack.Length} characters, over the {sentBack.Limit} a move");
            lines.Add("here is spawned on, and it was sent back rather than analysed.");
            lines.Add(sentBack.CanTakeChildren
                ? "Cut it into child tasks, each under the limit, rather than trimming it in place."
                : $"A {issue.Type} takes no children, so trim this description to the slice it keeps");
            if (!sentBack.CanTakeChildren) lines.Add("and file the excess as new sibling tasks under the same parent.");
            lines.Add("");
        }

        // Decisions that were asked for and given, on this ticket, before now.
        // Carried into the prompt rather than left for the agent to find in the
        // comment thread, because the one thing a session must not do is reopen
        // a question somebody has already answered.
        var answered = work.Questions.Where(q => q.Answers.Count > 0).ToList();
        if (answered.Count > 0)
        {
            lines.Add("## Decisions already made");
            lines.Add("");
            lines.Add("These were asked on this ticket and answered. They are settled: build on");
            lines.Add("them, and do not ask again.");
            lines.Add("");

            for (var i = 0; i < answered.Count; i++)
            {
                if (i > 0)
                {
                    lines.Add("");
                    lines.Add("---");
                    lines.Add("");
                }

                lines.Add($"**Asked ({answered[i].AskedBy}):** {answered[i].Body}");

                foreach (var answer in answered[i].Answers)
                {
                    lines.Add("");
                    lines.Add($"**Answered ({answer.Author}):** {answer.Body}");
                }
            }

            lines.Add("");
        }

        // What was said to the agent on this ticket while nobody was working
        // it. A message sent mid-run reaches that run by its hooks; one that
        // nothing read is carried here instead, so it is neither lost nor
        // delivered twice - the runner marks exactly these read as it spawns.
        if (work.Messages is { Count: > 0 } said)
        {
            lines.Add("## Said to you since the last session");
            lines.Add("");

            for (var i = 0; i < said.Count; i++)
            {
                if (i > 0)
                {
                    lines.Add("");
                    lines.Add("---");
                    lines.Add("");
                }

                lines.Add(Message(key, said[i], whileWorking: false));
            }

            lines.Add("");
        }

        var trunkLanding = work.Landing == Landings.Trunk;

        lines.AddRange(Tail(
            key, to, work.IssueUrl, repositories, issue.PreApproval, sharedStory,
            sharesPullRequest: issue.Type == "story" && issue.OnePr,
            isCheck: work.Playbook?.IsCheck == true, failStatus: work.FailStatusName,
            trunkLanding: trunkLanding,
            asksReleaseNote: work.AsksReleaseNote, issueType: issue.Type,
            startsHere: BoundForImplementation(work)));
        return string.Join('\n', lines);
    }

    /// <summary>
    /// What a session is told, once, the first time its tokens cross its
    /// playbook's budget - delivered the same way <see cref="Message"/> is, by
    /// the hook that noticed. Says to wrap up, not to ask: an open question is a
    /// full stop on the next dispatch, and crossing a budget is not a decision
    /// for somebody else to make.
    /// </summary>
    public static string WrapUp(string key) =>
        string.Join('\n',
            $"This session has crossed its playbook's budget on {key}. Wrap up rather than",
            "start anything new:",
            "",
            "Finish the edit already in hand. Commit what is green at the nearest seam. Push",
            "it to the issue's branch. File whatever is left as tasks under the ticket - set",
            "their parentKey to it, the shape docs/hatch-planning.md describes. Comment on the",
            "ticket what landed and what did not. Then stop.");

    /// <summary>
    /// One message to the agent, in the words both ways of delivering it use:
    /// the hook that puts it in front of a running session, and the prompt that
    /// carries it into the next. One method, so the two cannot come to say
    /// different things.
    /// </summary>
    /// <remarks>
    /// The last sentence is there because nothing structural can prove a model
    /// acted on what it was handed. Asking it to say so on the ticket is the
    /// nearest a person watching the ticket gets to knowing.
    /// </remarks>
    public static string Message(string key, CommentDto message, bool whileWorking) =>
        string.Join('\n',
            $"{message.Author} sent this to you on {key} at {Format.Stamp(message.CreatedAt)}" +
                (whileWorking ? ", while you were working:" : ", after the last session on it ended:"),
            "",
            message.Body.ReplaceLineEndings("\n"),
            "",
            "It was sent to change what you are doing now. Apply it, and if it changes your plan," +
                $" say so on the ticket (hatch comment {key} \"...\").");

    /// <summary>
    /// The facts of the conflict the playbook is about: the branch and the trunk
    /// with both shas, and the files. The playbook says what to do with them;
    /// this only says what they are - and the branch section that follows says
    /// what state the tree is in, so it is not said twice.
    /// </summary>
    private static IEnumerable<string> Conflict(IReadOnlyList<ConflictFacts> conflicts, string trunk)
    {
        yield return "## The conflict";
        yield return "";

        if (conflicts.Count == 0)
        {
            yield return $"The board says this issue's branch no longer merges with {trunk}, and holds no";
            yield return "verdict that names the files.";
            yield return "";
            yield break;
        }

        yield return "This issue's branch on origin no longer merges with the trunk:";
        yield return "";

        foreach (var c in conflicts)
        {
            var where = conflicts.Count > 1 ? $"{c.Where}: " : "";
            yield return $"- {where}`{c.Branch}` at {c.BranchSha} does not merge with `{c.Trunk}` at {c.TrunkSha}. Conflicted files:";
            foreach (var file in c.Files) yield return $"  - {file}";
        }

        yield return "";
    }

    /// <summary>The most bytes the whole failing-build section spends on log excerpts.</summary>
    public const int MaxBuildSectionBytes = 40 * 1024;

    /// <summary>The most bytes the last-check section spends on findings.</summary>
    public const int MaxCheckFindingsSectionBytes = 40 * 1024;

    /// <summary>
    /// The facts of the failing build the playbook is about: for each repository,
    /// the branch and the sha; for each failing check, its name and link, and the
    /// lines of its log that lead up to the failure. The playbook says what to do
    /// with them; this only says what they are.
    /// </summary>
    /// <remarks>
    /// The excerpts are what makes a failure fixable that does not reproduce
    /// locally - tests that only run against a database are skipped on a machine
    /// without one, and the log as CI reported it is then the only account of the
    /// failure there is. The section is capped, and past the cap a check's log is
    /// left out and says the command that prints it.
    /// </remarks>
    private static IEnumerable<string> FailingBuilds(IReadOnlyList<BuildFacts> builds)
    {
        yield return "## The failing build";
        yield return "";

        if (builds.Count == 0)
        {
            yield return "The board says the build on this issue's branch failed, and holds no verdict that";
            yield return "names the checks.";
            yield return "";
            yield break;
        }

        yield return "The build on this issue's branch on origin failed:";
        yield return "";

        var spent = 0;
        foreach (var b in builds)
        {
            var where = builds.Count > 1 ? $"{b.Where}: " : "";
            yield return $"- {where}`{b.Branch}` at {b.Sha}. Failing checks:";

            foreach (var check in b.Failing)
            {
                yield return check.Url is { Length: > 0 } url ? $"  - {check.Name}: {url}" : $"  - {check.Name}";

                var command = check.JobId is { } job ? $"gh run view --job {job} --log-failed" : null;

                if (check.Excerpt is not { Length: > 0 } excerpt)
                {
                    yield return "    _no log_";
                    continue;
                }

                var size = System.Text.Encoding.UTF8.GetByteCount(excerpt);
                if (spent + size > MaxBuildSectionBytes)
                {
                    yield return command is null
                        ? "    _log left out: this section is at its size cap_"
                        : $"    _log left out: this section is at its size cap - `{command}` prints it_";
                    continue;
                }

                spent += size;

                // A fence longer than any run of backticks in the excerpt, so a
                // log that contains a fence cannot end the block early.
                var fence = new string('`', Math.Max(3, LongestBackticks(excerpt) + 1));
                yield return $"    {fence}";
                foreach (var line in excerpt.ReplaceLineEndings("\n").Split('\n')) yield return $"    {line}";
                yield return $"    {fence}";

                if (command is not null)
                    yield return $"    This is the lines before its last error. The whole log: `{command}`";
            }
        }

        yield return "";
    }

    /// <summary>
    /// The findings of the last check on this issue, carried forward into a
    /// dispatch standing in the implementation column after that check failed.
    /// The playbook says what to do with them; this only says what they are.
    /// </summary>
    private static IEnumerable<string> LastCheck(string findings)
    {
        yield return "## The last check";
        yield return "";
        yield return "The last check on this issue failed. These are its findings:";
        yield return "";

        var size = System.Text.Encoding.UTF8.GetByteCount(findings);
        if (size > MaxCheckFindingsSectionBytes)
        {
            yield return "_findings left out: this section is at its size cap_";
            yield return "";
            yield break;
        }

        var fence = new string('`', Math.Max(3, LongestBackticks(findings) + 1));
        yield return fence;
        foreach (var line in findings.ReplaceLineEndings("\n").Split('\n')) yield return line;
        yield return fence;
        yield return "";
    }

    private static int LongestBackticks(string text)
    {
        int longest = 0, run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            if (run > longest) longest = run;
        }

        return longest;
    }

    /// <summary>
    /// The git state the session starts in, as the runner left it. Written as
    /// fact rather than instruction wherever it can be, and the one instruction
    /// it does give - that it overrides the playbook - is there because a stock
    /// playbook still tells a session to cut a branch from the trunk.
    /// </summary>
    private static IEnumerable<string> Branch(IReadOnlyList<BranchEntry> entries, string? sharedStory)
    {
        yield return "## The branch";
        yield return "";
        yield return "This is the state the runner left the tree in. It overrides any instruction about";
        yield return "which branch to start from or whether to cut one in the playbook above.";
        yield return "";

        // Said first, because everything below is about a branch that is not
        // named for this ticket, and a session that read "origin has no branch
        // for this issue" without it would take that for a problem.
        if (sharedStory is not null)
        {
            yield return $"**This ticket's work goes on {sharedStory}'s branch,** the one every task of {sharedStory}";
            yield return $"pushes to - not on a branch named for this ticket. {sharedStory} opens the one pull";
            yield return "request for all of them once its tasks are pushed. Do not open one, and do not";
            yield return "record one on this ticket.";
            yield return "";
        }

        foreach (var entry in entries)
        {
            var where = entries.Count > 1 ? $"{entry.Path}: " : "";

            switch (entry.Kind)
            {
                case BranchKind.Entered:
                    yield return $"- {where}on `{entry.Branch}` at {entry.Sha}, {entry.Ahead} commit(s) ahead of the trunk.";
                    foreach (var line in Merge(entry)) yield return $"  {line}";
                    if (entry.LocalAhead)
                        yield return "  The copy on this machine was ahead of origin, and is the one you are on.";
                    if (entry.KeptAs.Length > 0)
                        yield return $"  This machine's copy had diverged from origin's. Its commits are kept on `{entry.KeptAs}`; the branch is origin's.";
                    yield return "  Continue on it: do not cut a new branch, and push to it, so the pull request follows.";
                    if (entry.TrunkLanding)
                        yield return $"  This project lands on the trunk: finish on this branch, then merge it into `{entry.Trunk}`, push `{entry.Trunk}`, delete the branch on origin, and record the landing.";
                    break;

                case BranchKind.None:
                    if (entry.TrunkLanding)
                    {
                        yield return $"- {where}this project lands its work on the trunk. origin has no branch for this issue, and none is wanted: the tree is on `{entry.Trunk}` at origin's tip - commit here, and cut nothing.";
                        break;
                    }
                    yield return $"- {where}origin has no branch for this issue. The tree is on the trunk at origin's tip; cut `{entry.Cut}` from it.";
                    yield return sharedStory is null
                        ? "  Push to it as the work goes, and open a pull request on it before this increment ends, so there is something for a person to review."
                        : "  Push to it as the work goes, so the next task builds on it. Do not open a pull request.";
                    break;

                case BranchKind.AlreadyMerged:
                    if (entry.TrunkLanding)
                    {
                        yield return $"- {where}this project lands its work on the trunk. origin's {string.Join(", ", entry.Merged.Select(m => $"`{m}`"))} has already merged into the trunk, and no branch is wanted: the tree is on `{entry.Trunk}` at origin's tip - commit here, and cut nothing.";
                        break;
                    }
                    yield return $"- {where}origin's {string.Join(", ", entry.Merged.Select(m => $"`{m}`"))} has already merged into the trunk. "
                        + $"The tree is on the trunk at origin's tip; cut `{entry.Cut}`, which is not on origin.";
                    break;

                case BranchKind.Several:
                    yield return $"- {where}origin has {entry.Candidates.Count} unmerged branches for this issue: "
                        + string.Join(", ", entry.Candidates.Select(c => $"`{c}`"))
                        + ". The tree is on the trunk. The answer under \"Decisions already made\" below says which to use.";
                    break;

                default:
                    yield return $"- {where}the runner could not enter the issue's branch ({entry.Problem}). The tree is on the trunk.";
                    break;
            }
        }

        yield return "";
    }

    private static IEnumerable<string> Merge(BranchEntry entry)
    {
        switch (entry.Merge)
        {
            case MergeOutcome.NoOp:
                yield return "The trunk is already in it: nothing was merged.";
                break;

            case MergeOutcome.Clean:
                yield return $"The trunk was merged in cleanly and committed, so the branch is now at {entry.Head}. That merge is not pushed.";
                break;

            case MergeOutcome.Conflicted:
                yield return "**The merge of the trunk is in progress and has conflicts.** Conflicted files:";
                foreach (var file in entry.Conflicted) yield return $"  - {file}";
                yield return "Resolve them and commit the merge before you do anything else.";
                break;

            case MergeOutcome.Failed:
                yield return $"The trunk would not merge in ({entry.Problem}). Merge it yourself before you do anything else.";
                break;
        }
    }

    /// <summary>
    /// Whether this increment's destination is the board's implementation
    /// column - the only increment that is told to <c>hatch start</c>, since
    /// <c>start</c> moves a ticket into that column. The stored
    /// <c>isImplementation</c> flag is not read: on a stock board nothing is
    /// ticked and the column is measured, which the dispatch has already done
    /// in <see cref="WorkDto.FailStatusName"/>.
    /// </summary>
    internal static bool BoundForImplementation(WorkDto work) =>
        work.ToStatus is { } destination && work.FailStatusName is { } implementation
        && string.Equals(destination.Name, implementation, StringComparison.Ordinal);

    /// <summary>
    /// The part that is the same for every ticket: how to reach Hatch, what to
    /// do with a decision that is not the implementer's, and where the
    /// increment ends.
    /// </summary>
    internal static IEnumerable<string> Tail(
        string key, string to, string? issueUrl, IReadOnlyList<Checkouts.RepositoryLine>? repositories,
        string preApproval, string? sharedStory = null, bool sharesPullRequest = false,
        bool isCheck = false, string? failStatus = null, bool trunkLanding = false,
        bool asksReleaseNote = false, string? issueType = null, bool startsHere = false) =>
    [
        "## Reaching Hatch",
        "",
        $"Run these from the repository root{WhichRoot(repositories)}. The key is already in the environment.",
        "The ticket above is the brief exactly as it stands - `hatch show` would only print it again.",
        "",
        "```",
        $"hatch comments {key}          the comments alone, since the brief above is already current",
        ..(isCheck
            ? new[] { $"hatch verdict {key} pass|fail [\"findings\"]   record this check's own verdict" }
            : startsHere
                ? new[]
                {
                    $"hatch start {key}             move it to in progress",
                    $"hatch move {key} <column>     move it anywhere non-terminal",
                }
                : new[] { $"hatch move {key} <column>     move it anywhere non-terminal" }),
        $"hatch comment {key} \"...\"     write on the ticket",
        $"hatch ask {key} \"...\"         ask for a decision, and stop",
        $"hatch api GET /api/hatch/issues?parentKey={key}",
        "```",
        "",
        ..(isCheck || startsHere ? Array.Empty<string>() : new[]
        {
            "Claiming is the marker that you have started. The ticket moves once, at the end,",
            "to the column under \"Where this increment ends\".",
            "",
        }),
        "Filing issues, editing descriptions and dates all go through `api` -",
        "docs/hatch-planning.md has the shapes. Read it only if this increment",
        "files or reshapes work.",
        "",
        ..(trunkLanding ? Landing(key)
            : sharedStory is null ? PullRequest(key, issueUrl, sharesPullRequest)
            : SharedPullRequest(key, sharedStory, to)),
        "## When you cannot decide",
        "",
        "Some things are not yours to choose: a product call, a name that will be",
        "lived with for years, a tradeoff with no technically correct side. When you",
        "reach one, do not guess, and do not quietly pick whichever option is easiest",
        "to build.",
        "",
        "```",
        $"hatch ask {key} \"the question, in one sentence\" \\",
        "    --recommend \"The one you would take: what it means, and what it costs\" \\",
        "    --option \"The alternative: what it means, and what it costs\"",
        "```",
        "",
        "**Name the choices.** Nearly every decision worth asking about is a choice",
        "between two or three things you can already name, and each `--option` becomes",
        "something the operator presses, rather than a paragraph they have to read",
        "twice and compose a reply to. The body is the question alone; the tradeoffs",
        "go inside the options they belong to; `--recommend` is the one you would",
        "take, of which there may be one. A label is short enough to press and reads",
        "as a decision on its own - `child-weighted`, not \"weight each child",
        "equally\" - because it becomes the answer's own text. Ask in prose only when",
        "the answer is genuinely open-ended.",
        "",
        "One call per question, so each can be answered on its own. Then stop. An open",
        "question blocks this ticket from being dispatched at all, so nothing further",
        "will be spawned at it until somebody answers - and anything built past an",
        "unanswered question is built on a guess.",
        "",
        "What the repository can answer, answer by reading the repository. A question",
        "the code already settles is a round trip through a person for nothing.",
        "",
        ..PreApproval(preApproval),
        "## Where this increment ends",
        "",
        isCheck
            ? $"{key}'s session ends with a verdict, not a move: a pass sends it to \"{to}\", and a fail sends it to \"{failStatus}\"."
            : $"{key} should be in \"{to}\" when you stop, and no further.",
        "Only the operator moves work into a terminal column. An increment that",
        "leaves this ticket where it found it is recorded as having done nothing -",
        "so move it, or ask, before you stop.",
        "",
        "Do not edit playbooks. The API refuses it, and the refusal is deliberate:",
        "an agent that could widen its own instructions and its own budget is a loop",
        "with no end. If a playbook is wrong, say so on the ticket and stop.",
        "",
        ..(asksReleaseNote ? ReleaseNote(key, issueType) : Array.Empty<string>()),
        "Last of all, say what you did, in a fenced block:",
        "",
        "```work-log",
        "A title naming what this session did",
        "",
        "The summary, under 100 words.",
        "```",
        "",
        $"That block becomes the work log row for this session on {key}, beside",
        "what the session cost. A run that writes none still gets a row marked as",
        "never having said what it did. Write it last, and write it once.",
    ];

    /// <summary>
    /// The section told to a session on an express or pre-approved ticket, or
    /// nothing at all - every ticket on a stock board. Express names the one
    /// thing that follows from it; pre-approved names the one thing that
    /// follows from that, past what express already says.
    /// </summary>
    private static IEnumerable<string> PreApproval(string level) => level switch
    {
        PreApprovalLevels.ExpressName =>
        [
            "## This ticket is express",
            "",
            "Two things follow. Where this issue stands in a column marked *Express",
            "skips*, the loop carries it on to the next column itself, without a person",
            "reading it first - no session runs and nothing is spent. An unanswered",
            "question is what stops that: anything that needs a person's eye is asked,",
            "not assumed, the same as it would be on any other ticket.",
            "",
        ],
        PreApprovalLevels.PreApprovedName =>
        [
            "## This ticket is pre-approved",
            "",
            "One thing follows, past what express already does: once this ticket is in",
            "review with a pull request recorded, and that pull request is green and",
            "current, the loop merges it itself, with nobody reading the diff first.",
            "Read your own diff before you open the pull request, the way the reviewer",
            "would have - that is the only reading it gets.",
            "",
        ],
        _ => [],
    };

    /// <summary>
    /// The ask handed to a session carrying a parentless issue into review, in
    /// the words HA-659's acceptance criterion settled on - a feature
    /// announcement for an epic, one line and no body for anything else.
    /// </summary>
    private static string[] ReleaseNote(string key, string? issueType) => issueType == "epic"
        ?
        [
            $"{key} is an epic, so write its release note too, as a feature announcement:",
            "",
            "```release-note",
            "One sentence announcing what shipped.",
            "",
            "A short paragraph beneath it, which may link into the docs as",
            "`/apps/hatch/docs/...` - a path, never an origin.",
            "```",
            "",
            "It shows on the changelog and the issue page, where a person may edit or correct",
            "it. The last such block wins, and writing none is fine - the changelog then falls",
            "back to the title.",
            "",
        ]
        :
        [
            $"{key} is moving into review, so write its release note too: one line, no body.",
            "",
            "```release-note",
            "One sentence a user would read in a changelog.",
            "```",
            "",
            "It shows on the changelog and the issue page, where a person may edit or correct",
            "it. The last such block wins, and writing none is fine - the changelog then falls",
            "back to the title.",
            "",
        ];

    /// <summary>
    /// Which checkout "the repository root" means, said only once there is more
    /// than one in play - with exactly one, the sentence is already unambiguous.
    /// </summary>
    private static string WhichRoot(IReadOnlyList<Checkouts.RepositoryLine>? repositories) =>
        repositories is { Count: > 1 } list
            ? $" - {list.First(r => r.Primary).Remote}'s checkout, not the others named above"
            : "";

    /// <summary>
    /// Which of the model and the effort the ticket chose rather than the
    /// playbook, as half a sentence - or nothing at all, which is every issue
    /// on a stock board.
    /// </summary>
    /// <remarks>
    /// The server has already folded an issue's overrides into the playbook it
    /// hands back, so the playbook's model is the value that won and there is
    /// nothing here to decide. What is left is saying where it came from, and
    /// that is decided by comparing the value being spawned on against the
    /// override the issue carries - not by tracking whether a flag was given.
    /// The comparison is derivable from what the caller already holds, it is
    /// exactly right on every unattended path, and the one case it softens - an
    /// operator typing <c>--model opus</c> at a ticket already set to
    /// <c>opus</c> - prints a sentence that is still true.
    /// </remarks>
    public static string? OverrideLine(WorkDto work, string model, string effort)
    {
        var which = new StringBuilder();

        if (work.Issue.ModelOverride is { Length: > 0 } m && m == model) which.Append("model");
        if (work.Issue.EffortOverride is { Length: > 0 } e && e == effort)
        {
            if (which.Length > 0) which.Append(" and ");
            which.Append("effort");
        }

        return which.Length == 0 ? null : $"{which} from {work.Issue.Key}, not the playbook";
    }

    /// <summary>
    /// Which playbook spoke for this move, and whose it was - a board-wide
    /// row, the issue's own project's row, or a row read off that project's
    /// own repo-sourced file. Null exactly when nothing matched, which never
    /// reaches a caller that has already refused the move. Whenever
    /// <see cref="WorkDto.PlaybookFile"/> is set, the trunk and a short sha of
    /// the copy the dispatch was judged against is appended, regardless of
    /// which of the three sources matched.
    /// </summary>
    public static string? PlaybookLine(WorkDto work)
    {
        if (work.Playbook is not { } playbook) return null;

        var line = playbook.Source switch
        {
            "project" => $"playbook: {playbook.ProjectKey}'s own",
            "file" => $"playbook: {playbook.ProjectKey}'s own file",
            _ => "playbook: board-wide",
        };

        return work.PlaybookFile is { } file ? $"{line} on {file.Trunk} at {Short(file.Sha)}" : line;
    }

    /// <summary>The first 7 characters of a sha, for a dry-run line - unchanged where it is shorter.</summary>
    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    /// <summary>
    /// What a task on its story's branch is told in place of the pull request
    /// section: not to open one, and why. The story's closeout is what opens it.
    /// </summary>
    private static string[] SharedPullRequest(string key, string story, string to) =>
    [
        "## No pull request from this ticket",
        "",
        $"Do not open a pull request for {key}, and do not run `hatch pr {key}`. {story}'s branch",
        "carries every task's work, and one pull request - titled with the story's key, not",
        $"this ticket's - is opened for it once its tasks are all pushed. Push your commits to the",
        $"branch above, then move {key} to \"{to}\": it waits there, with nothing of its own to",
        "merge, until the story's pull request does.",
        "",
    ];

    /// <summary>
    /// What a session is told in place of the pull request section, on a
    /// project that lands work straight on the trunk: never to open one, and
    /// how the work gets there instead.
    /// </summary>
    private static string[] Landing(string key) =>
    [
        "## Landing on the trunk",
        "",
        $"This project lands on the trunk, so there is no pull request to open for {key}.",
        "When the work is green:",
        "",
        "```",
        "git pull --no-rebase origin <trunk>",
        "```",
        "",
        "A merge, explicitly never a rebase. Run the build and tests again if that merge",
        "brought anything in, then:",
        "",
        "```",
        "git push origin <trunk>",
        "```",
        "",
        "If the push is refused because somebody else landed first, pull and push again -",
        "never force it. Once the work is on origin's trunk, for each repository:",
        "",
        "```",
        $"hatch landed {key} <sha>",
        "```",
        "",
        "and write the comment with the sha, the way every landed ticket does. The",
        "increment is not finished until the landing is recorded - \"## Where this",
        $"increment ends\" below still names review as where {key} stops.",
        "",
    ];

    /// <summary>
    /// What to write into a pull request, if this increment opens one. The link
    /// is only ever an absolute http(s) one; without it the first line is the
    /// bare key, and the session is told not to make a link up.
    /// </summary>
    private static string[] PullRequest(string key, string? issueUrl, bool sharesPullRequest = false)
    {
        var linked = Uri.TryCreate(issueUrl, UriKind.Absolute, out var url)
            && url.Scheme is "http" or "https";

        return
        [
            "## Opening a pull request",
            "",
            .. sharesPullRequest
                ?
                [
                    "This story's tasks share its branch, and none of them opens a pull request of its",
                    "own. If this increment is the story's closeout - its tasks are pushed to the branch",
                    "above - the one pull request for all of them is yours to open, on that branch,",
                    "unless `hatch pr` on this ticket already names one.",
                    "",
                ]
                : Array.Empty<string>(),
            "Open one before this increment ends, if it pushes any commit meant for a person to",
            "review - the branch section above says which kind of increment this is. Name the",
            "ticket in two places, so the reviewer can tell where it came from and reach the",
            "brief in one click.",
            "",
            $"- **Title:** `{key} ` and then the subject in the usual house style, `Area: what",
            $"  changed, as a sentence`. No brackets and no second colon: `{key} Auth: the first",
            "  Admin`, not `[" + key + "] Auth: …` or `" + key + ": Auth: …`.",
            "- **First line of the description:** exactly this, and nothing else on the line,",
            "  followed by a blank line and then the summary.",
            "",
            "```",
            linked ? $"[{key}]({issueUrl})" : key,
            "```",
            "",
            linked
                ? "Use that line as given. Never write a relative link."
                : "No link to the ticket is available, so the line is the bare key. Do not make a link up, and never write a relative one.",
            "",
            $"Afterwards, record it on the ticket with `hatch pr {key} <url>`.",
            "",
        ];
    }
}

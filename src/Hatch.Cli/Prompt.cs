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
        IReadOnlyList<BranchEntry>? branches = null)
    {
        var issue = work.Issue;
        var key = issue.Key;
        var to = work.ToStatus?.Name ?? "?";
        var lines = new List<string>
        {
            work.Playbook?.Prompt ?? "",
            "",
            "---",
            "",
            "## The ticket",
            "",
            $"{key}  [{issue.Type}]  {issue.Title}",
            $"moving:   {work.FromStatus.Name} -> {to}",
        };

        if (issue.ParentKey is { Length: > 0 }) lines.Add($"parent:   {issue.ParentKey}");
        if (issue.ReadyAt is { Length: > 0 }) lines.Add($"ready:    {issue.ReadyAt}");
        if (issue.DueAt is { Length: > 0 }) lines.Add($"due:      {issue.DueAt}");

        lines.Add("");
        lines.Add(issue.Description.Length > 0
            ? issue.Description
            : "_No description. That is itself worth noting on the ticket._");
        lines.Add("");

        if (repositories is { Count: > 0 })
        {
            lines.Add("## Repositories");
            lines.Add("");
            lines.AddRange(repositories.Select(r => r.Path is { } path
                ? $"{path}  {r.Remote}{(r.Primary ? "  (primary)" : "")}"
                : $"{r.Remote}  (no checkout here)"));
            lines.Add("");
        }

        if (branches is { Count: > 0 }) lines.AddRange(Branch(branches));

        if (work.Children.Count > 0)
        {
            lines.Add("## Its children");
            lines.Add("");
            lines.AddRange(work.Children.Select(c => $"- {c.Key}  [{c.Type}]  {c.Title}"));
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

        lines.AddRange(Tail(key, to, work.IssueUrl, repositories));
        return string.Join('\n', lines);
    }

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
    /// The git state the session starts in, as the runner left it. Written as
    /// fact rather than instruction wherever it can be, and the one instruction
    /// it does give - that it overrides the playbook - is there because a stock
    /// playbook still tells a session to cut a branch from the trunk.
    /// </summary>
    private static IEnumerable<string> Branch(IReadOnlyList<BranchEntry> entries)
    {
        yield return "## The branch";
        yield return "";
        yield return "This is the state the runner left the tree in. It overrides any instruction about";
        yield return "which branch to start from or whether to cut one in the playbook above.";
        yield return "";

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
                    break;

                case BranchKind.None:
                    yield return $"- {where}origin has no branch for this issue. The tree is on the trunk at origin's tip; cut `{entry.Cut}` from it.";
                    break;

                case BranchKind.AlreadyMerged:
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
    /// The part that is the same for every ticket: how to reach Hatch, what to
    /// do with a decision that is not the implementer's, and where the
    /// increment ends.
    /// </summary>
    private static IEnumerable<string> Tail(
        string key, string to, string? issueUrl, IReadOnlyList<Checkouts.RepositoryLine>? repositories) =>
    [
        "## Reaching Hatch",
        "",
        $"Run these from the repository root{WhichRoot(repositories)}. The key is already in the environment.",
        "",
        "```",
        $"hatch show {key}              the ticket and its comments",
        $"hatch start {key}             move it to in progress",
        $"hatch move {key} <column>     move it anywhere non-terminal",
        $"hatch comment {key} \"...\"     write on the ticket",
        $"hatch ask {key} \"...\"         ask for a decision, and stop",
        $"hatch api GET /api/hatch/issues?parentKey={key}",
        "```",
        "",
        "Filing new issues, editing descriptions and setting dates all go through",
        "`api`, and docs/hatch-planning.md documents the shapes. Read it if this",
        "increment files or reshapes work; an increment that only writes code does",
        "not need it.",
        "",
        ..PullRequest(key, issueUrl),
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
        "something the operator presses - in the browser and at a terminal - rather",
        "than a paragraph they have to read twice and then compose a reply to. So the",
        "body is the question alone, in a sentence; the tradeoffs go inside the options",
        "they belong to; and `--recommend` is the one you would take, of which there",
        "may be one. Ask in prose only when the answer is genuinely open-ended.",
        "",
        "A label is short enough to press and reads as a decision on its own -",
        "\"child-weighted\", not \"we should weight each direct child equally\". It",
        "becomes the answer text itself, and that is what somebody reads six months",
        "later.",
        "",
        "One call per question, so each can be answered on its own. Then stop. An open",
        "question blocks this ticket from being dispatched at all, so nothing further",
        "will be spawned at it until somebody answers - and anything built past an",
        "unanswered question is built on a guess.",
        "",
        "What the repository can answer, answer by reading the repository. A question",
        "the code already settles is a round trip through a person for nothing.",
        "",
        "## Where this increment ends",
        "",
        $"{key} should be in \"{to}\" when you stop, and no further.",
        "Only the operator moves work into a terminal column.",
        "",
        "Do not edit playbooks. The API refuses it, and the refusal is deliberate:",
        "an agent that could widen its own instructions and its own budget is a loop",
        "with no end. If a playbook is wrong, say so on the ticket and stop.",
        "",
        "Last of all, say what you did, in a fenced block:",
        "",
        "```work-log",
        "A title naming what this session did",
        "",
        "The summary, under 100 words.",
        "```",
        "",
        $"That block becomes the work log row for this session on {key},",
        "beside what the session cost. A run that writes none still gets its row,",
        "marked as never having said what it did - so the block is worth the two",
        "lines it takes. Write it last, and write it once.",
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
    /// What to write into a pull request, if this increment opens one. The link
    /// is only ever an absolute http(s) one; without it the first line is the
    /// bare key, and the session is told not to make a link up.
    /// </summary>
    private static string[] PullRequest(string key, string? issueUrl)
    {
        var linked = Uri.TryCreate(issueUrl, UriKind.Absolute, out var url)
            && url.Scheme is "http" or "https";

        return
        [
            "## If you open a pull request",
            "",
            "Only if this increment opens one. Name the ticket in two places, so the",
            "reviewer can tell where it came from and reach the brief in one click.",
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

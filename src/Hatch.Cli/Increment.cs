using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// What one increment came to - read by the loop after it ends, so that a night
/// can say what happened without asking the board a second time.
/// </summary>
public sealed class IncrementReport
{
    /// <summary>The issue the increment was spent on.</summary>
    public required string Key { get; init; }

    /// <summary>The column it started in.</summary>
    public required string From { get; init; }

    /// <summary>The column the playbook was moving it to.</summary>
    public required string To { get; init; }

    /// <summary>The column it is in now, which is the only report that counts.</summary>
    public string Ended { get; set; } = "";

    /// <summary>Whether those last two differ.</summary>
    public bool Moved { get; set; }

    /// <summary>
    /// The board's own read, taken in the same call as <see cref="Ended"/>, of
    /// whether the ticket is sitting in the review column right now - not
    /// <see cref="To"/>, which is the target computed before the session ran
    /// and can be stale by the time it is over.
    /// </summary>
    public bool EndedInReview { get; set; }

    /// <summary>The board says it ended where it started: an increment that did nothing.</summary>
    public bool Stalled { get; set; }

    /// <summary>
    /// This increment's own verdict on a stall: the first in a row, let go of
    /// quietly rather than flagged. Not to be confused with the dispatch's own
    /// <c>WorkDto.LetGo</c> - an <c>int</c>, the count of consecutive releases
    /// already spent before this one started - which this bool is the answer
    /// to: zero coming in, one going out.
    /// </summary>
    public bool LetGo { get; set; }

    /// <summary>
    /// What the claim is released with: <see cref="ClaimOutcomes.Worked"/> for
    /// anything that changed something on the ticket's behalf - moved it,
    /// resolved its conflict, pushed a build fix, or had the session ask its
    /// own question - and <see cref="ClaimOutcomes.Dropped"/> for a stall,
    /// flagged or let go alike. Left null for a lost lease, a usage limit, an
    /// interrupted increment, and a board read that failed: none of those is
    /// this increment's verdict to give, because none of them ran to one.
    /// </summary>
    public string? ReleaseOutcome { get; set; }

    /// <summary>
    /// A conflict increment, and origin's branch merges with the trunk now. Not a
    /// stall, and not a move either: the ticket stays in review and that is the
    /// right place for it.
    /// </summary>
    public bool Resolved { get; set; }

    /// <summary>The trunk a conflict increment was about, for the words it is reported in.</summary>
    public string? ConflictTrunk { get; set; }

    /// <summary>The files a conflict increment left conflicting, when it did.</summary>
    public IReadOnlyList<string> StillConflicting { get; set; } = [];

    /// <summary>
    /// A build increment pushed a new tip to the branch. Not a stall, and not a
    /// move either: the ticket stays in review, and the build on the new tip is
    /// judged later, by the board.
    /// </summary>
    public bool FixPushed { get; set; }

    /// <summary>The checks a build increment left failing on the tip it was sent to, when it did.</summary>
    public IReadOnlyList<string> StillFailing { get; set; } = [];

    /// <summary>The branch and the sha a build increment was sent to, for the words it is reported in.</summary>
    public string? BuildBranch { get; set; }
    public string? BuildSha { get; set; }

    /// <summary>
    /// The session ended because its Claude account ran out of usage - when it
    /// expects to reset, or null when this was an ordinary increment.
    /// </summary>
    public DateTimeOffset? UsageLimitResetAt { get; set; }

    /// <summary>Whether <see cref="UsageLimitResetAt"/> came from what the session said, or is the one-hour backstop.</summary>
    public bool UsageLimitResetKnown { get; set; } = true;

    /// <summary>Whether this increment ended on a usage limit rather than an ordinary result.</summary>
    public bool UsageLimited => UsageLimitResetAt is not null;

    /// <summary>What was done about that, in the words the tally says it in.</summary>
    public string? Flag { get; set; }

    /// <summary>What <c>claude --resume</c> takes.</summary>
    public string? SessionId { get; set; }

    /// <summary>Dollars, out of the result event.</summary>
    public decimal? Cost { get; set; }

    /// <summary>What the CLI exited with.</summary>
    public int ExitCode { get; set; }

    /// <summary>Questions the session opened on its way out.</summary>
    public int Asked { get; set; }

    /// <summary>
    /// The keys under the ticket after the session that were not there at
    /// dispatch - progress, though the ticket did not move.
    /// </summary>
    public IReadOnlyList<string> Filed { get; set; } = [];

    /// <summary>
    /// The lease went while the session was running, so the session was stopped.
    /// Not a failure: it is the loop working correctly on a busy board, and
    /// three of them in a row must not end a night.
    /// </summary>
    public bool LostLease { get; set; }

    /// <summary>
    /// The board asked this runner to stand down for an emergency ticket while
    /// the session was running. Not a lost lease: this runner still owns the
    /// ticket, so it tidies, commits and pushes rather than writing nothing.
    /// </summary>
    public bool Preempted { get; set; }

    /// <summary>The emergency issue this runner was put down for.</summary>
    public string? PreemptedKey { get; set; }

    /// <summary>Its title, so the put-down comment can say why without a second call.</summary>
    public string? PreemptedTitle { get; set; }

    /// <summary>
    /// A cancellation reached deep enough into the increment - past the spawn
    /// itself, which already answers a Ctrl-C by stopping the session and
    /// returning normally - that nothing further could be read from or written
    /// to the board. Not a failure of the increment, which is why it has no
    /// bearing on <see cref="Outcome"/>: it is one more thing the closing
    /// banner's glyph reads, alongside <see cref="LostLease"/> and a non-zero
    /// exit.
    /// </summary>
    public bool Interrupted { get; set; }

    /// <summary>
    /// A cancellation reached <see cref="RunAsync"/>'s own token, but not the
    /// caller's - the keyboard's <c>k</c>, confirmed, rather than a Ctrl-C.
    /// Mutually exclusive with <see cref="Interrupted"/>: they are the two
    /// readings of one <see cref="OperationCanceledException"/>, told apart by
    /// the one method holding both tokens.
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>When the increment started and ended, wall clock - the banner's "Took" line.</summary>
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }

    /// <summary>
    /// The four counts added up, the same definition the work log answers with -
    /// so the banner and the issue page never disagree. Null when the session
    /// never reported what it spent, which the banner reads as "not reported"
    /// and never as zero.
    /// </summary>
    public long? TotalTokens { get; set; }

    /// <summary>Requests, out of the stream - one per distinct assistant message id. Null for the same reason <see cref="TotalTokens"/> can be.</summary>
    public int? Requests { get; set; }

    /// <summary>The largest context any one request carried. Null for the same reason <see cref="TotalTokens"/> can be.</summary>
    public long? PeakContextTokens { get; set; }

    /// <summary>Turns, out of the result event. Null for the same reason <see cref="TotalTokens"/> can be.</summary>
    public int? Turns { get; set; }

    /// <summary>What became of the ticket, in the phrase both the running commentary and the tally say it in.</summary>
    public string Outcome =>
        Moved ? $"{From} -> {Ended}{(Flag is { Length: > 0 } ? $", {Flag}" : "")}"
        : Preempted ? $"put down for {PreemptedKey} - {PreemptedTitle}"
        : UsageLimited ? $"out of Claude usage until {UsageLimit.Clock(UsageLimitResetAt!.Value)}{(UsageLimitResetKnown ? "" : " (unknown, one hour assumed)")}"
        : Skipped ? "skipped from the keyboard"
        : Resolved ? $"conflicts with {ConflictTrunk ?? Conflicts.UnnamedTrunk} resolved{(Flag is { Length: > 0 } ? $", {Flag}" : "")}"
        : FixPushed ? $"fix pushed, build pending{(Flag is { Length: > 0 } ? $", {Flag}" : "")}"
        : Filed.Count > 0 ? $"filed {Filed.Count} under it"
        : Stalled && StillFailing.Count > 0
            ? $"its build still fails ({string.Join(", ", StillFailing)}){(Flag is { Length: > 0 } ? $", {Flag}" : "")}"
        : Stalled && StillConflicting.Count > 0
            ? $"still conflicts with {ConflictTrunk ?? Conflicts.UnnamedTrunk}{(Flag is { Length: > 0 } ? $", {Flag}" : "")}"
        : Stalled ? $"still in \"{Ended}\"{(Flag is { Length: > 0 } ? $", {Flag}" : "")}"
        : Flag is { Length: > 0 } flag ? flag
        : "where it ended up is not known";
}

/// <summary>
/// What a conflict increment needs that <see cref="Increment"/> has no git to
/// find out: what the recheck just found, for the prompt, and the question asked
/// again after the session, for the verdict.
/// </summary>
/// <remarks>
/// A delegate rather than a workspace, so that <see cref="Increment"/> stays
/// without git and a test scripts the answer without a fake tree. Both callers
/// build it from their <see cref="Lifecycle"/>.
/// </remarks>
/// <param name="Found">The recheck that decided a session was worth spending - the prompt's facts.</param>
/// <param name="Judge">Fetches and checks every checkout again, and reports what it finds to the board.</param>
public sealed record ConflictRun(Rechecked Found, Func<CancellationToken, Task<Rechecked>> Judge);

/// <summary>
/// One increment: the header, the session, and what became of the ticket.
/// </summary>
/// <remarks>
/// Everything between "here is the dispatch" and "here is what happened", which
/// is the part <c>work</c> and <c>go-to-work</c> have in common. Choosing the
/// ticket stays with the callers, because they choose it differently, and so
/// does the last word, because one of them is talking to somebody sitting there
/// and the other is writing a log nobody will read until morning.
/// </remarks>
/// <param name="tempDirectory">
/// Where the hooks' directory is made - outside every checkout, so wiring them
/// in never shows up in a session's <c>git status</c>. The system's temporary
/// directory unless a test says otherwise.
/// </param>
public sealed class Increment(
    Board board, ISessionRunner sessions, Settings settings, Terminal say, IReadOnlyList<CheckoutEntry> checkouts,
    ReadoutState? readout = null, string? tempDirectory = null, Controls? controls = null)
{
    private readonly ReadoutState _readout = readout ?? new();
    private readonly string _temp = tempDirectory ?? Path.GetTempPath();
    private readonly Controls? _controls = controls;
    private RunFacts? _facts;

    /// <summary>
    /// What <c>claude --resume</c> would take, read live off the facts a
    /// running session is filling in - null before a session has said its id,
    /// and null when <see cref="RunAsync"/> was never called. Read by a caller
    /// whose call into this instance threw, to say whether there is a session
    /// left to resume.
    /// </summary>
    public string? SessionId => _facts?.SessionId;

    public async Task<IncrementReport> RunAsync(
        WorkDto work, string root, string model, string effort, bool quiet,
        Claim claim, CancellationToken ct,
        CancellationToken? callerCt = null,
        IReadOnlyList<string>? addDirs = null,
        IReadOnlyList<Checkouts.RepositoryLine>? repositories = null,
        IReadOnlyList<BranchEntry>? branches = null,
        ConflictRun? conflict = null,
        BuildRun? build = null,
        string runnerName = "",
        int incrementNumber = 1)
    {
        // A caller that passes only ct - every call site before this ticket,
        // and every existing test - is comparing ct against itself, so Skipped
        // can never be set by accident. Only a caller wrapping ct in its own
        // linked source (GoToWork.PassAsync, WorkCommand.SpendAsync) passes a
        // real caller apart from it.
        var caller = callerCt ?? ct;

        var report = new IncrementReport
        {
            Key = work.Issue.Key,
            From = work.FromStatus.Name,
            To = work.ToStatus?.Name ?? "?",
            ConflictTrunk = conflict is null ? null : conflict.Found.Trunk ?? Conflicts.Trunk(work.Issue),
        };
        report.Ended = report.From;
        report.StartedAt = DateTimeOffset.UtcNow;

        say.Lines(Banner.Opening(report, work, model, effort, runnerName, incrementNumber, conflict, build));
        say.Line("");

        _readout.BeginIncrement(
            report.Key, work.Issue.Title, Banner.What(report, conflict, build), work.IssueUrl, report.StartedAt);
        _controls?.SetIncrementRunning(true);

        try
        {
            // What was open before the run, so that what the run asked can be
            // told apart from what was already sitting there. Ids rather than a
            // count: an operator who answered one question in another terminal
            // while this ran would otherwise see the arithmetic come out to zero.
            var before = work.Questions.Where(q => q.Answers.Count == 0).Select(q => q.Id).ToHashSet();

            var facts = new RunFacts();
            _facts = facts;
            var started = DateTimeOffset.UtcNow;

            // The session is stopped the moment the lease goes, and only then: a
            // Ctrl-C comes down the caller's own token, which this is linked to.
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
            claim.OnLost = _ => stopping.Cancel();
            claim.OnPreempted = p =>
            {
                report.Preempted = true;
                report.PreemptedKey = p.Key;
                report.PreemptedTitle = p.Title;

                // Before the cancel, not after: BeatAsync reads Chatter.Line at
                // the top of the next tick, so this is what gets onto the very
                // next heartbeat - the card and the Runners page say the ticket
                // is being put down while the session is still being torn down.
                claim.Chatter.Line = $"putting {report.Key} down for {p.Key} - {p.Title}";
                stopping.Cancel();
            };

            var result = await SpawnAsync(work, root, model, effort, quiet, facts, claim, stopping.Token, addDirs, repositories, branches, conflict?.Found, build?.Found);
            report.ExitCode = result.ExitCode;
            report.SessionId = facts.SessionId;
            report.Cost = facts.CostUsd;

            if (facts.Result is { } summary)
            {
                report.Turns = summary.Turns;
                report.TotalTokens = summary.Models?.Sum(m =>
                    m.InputTokens + m.OutputTokens + m.CacheCreationTokens + m.CacheReadTokens);
                report.Requests = summary.Requests;
                report.PeakContextTokens = summary.PeakContextTokens;
            }

            // Before anything is written anywhere. A lease that went means this
            // runner no longer holds the ticket, and everything below except the
            // work log is a write onto somebody else's increment.
            if (claim.Lost is { } gone)
            {
                report.LostLease = true;
                report.Flag = $"the claim was taken - {gone}";
                say.Complain($"hatch: {report.Key} - {gone}");
                say.Complain("hatch:   the session was stopped; nothing further was written there");
            }

            // Before the board is asked anything, so a row exists even when the
            // reads after it fail. Money was spent on that ticket either way, and
            // a meter reading is not a claim to have done the work.
            await PostWorkLogAsync(report, facts, started, ct);

            // A run that ended in error, or one that ended before it could report
            // at all, may have ended because the account it ran under ran out of
            // Claude usage - read from whatever it said on its way out, never
            // from the claim, which knows nothing about why a session stopped.
            // Never when the lease was lost: the ticket is somebody else's by
            // then.
            //
            // The result event's own text is trusted on any error - it is the
            // CLI's own structured account of how the run ended. The last thing
            // the assistant said is trusted only when there was no result event
            // at all *and* the process exited abnormally: without that second
            // guard, an ordinary crash whose last words happened to quote this
            // very sentence - and this file's own tests do - would read as a
            // usage limit and hide the crash behind a clean-looking wait instead
            // of the three-strikes stall that would otherwise catch it.
            var said = facts.Result is { IsError: true } ? facts.ResultText
                : facts.Result is null && result.ExitCode != 0 ? facts.ResultText ?? facts.LastAssistantText
                : null;

            if (claim.Lost is null && claim.Preempted is null && UsageLimit.Recognise(said, DateTimeOffset.UtcNow) is { } hit)
            {
                report.UsageLimitResetAt = hit.ResetAt;
                report.UsageLimitResetKnown = hit.ResetKnown;

                say.Line("");
                say.Line(hit.ResetKnown
                    ? $"hatch: {report.Key} - out of Claude usage, resets {UsageLimit.Clock(hit.ResetAt)}"
                    : $"hatch: {report.Key} - out of Claude usage, and the reset time could not be read - treating it as an hour away");
            }

            // Everything from here on reads from or writes to the board, all of
            // it through the same token a Ctrl-C cancels. The spawn above
            // already answers a cancellation by stopping the session and
            // returning normally, but a signal caught this much later - between
            // the session ending and the last of these calls - has nothing left
            // running to stop, and would otherwise unwind out of this method
            // with no report at all: an opening banner with no closing one. So
            // it is caught here instead, and the report closes with whatever it
            // already knows.
            try
            {
                // Where the ticket actually ended up, asked of the board rather
                // than of the session. An increment that says it did the work
                // and leaves the ticket in the column it found it in did not do
                // the work, and this is the read that can tell the difference -
                // unless it filed work under the ticket instead, which the
                // board can see just as well as a move.
                try
                {
                    var later = await board.WorkAsync(checkouts, report.Key, claim.Lost is null ? claim.Token : null, ct);
                    report.Ended = later?.FromStatus.Name ?? report.From;
                    report.EndedInReview = later?.InReview ?? false;
                    report.Filed = later?.Children.Select(c => c.Key).Except(work.Children.Select(c => c.Key)).ToList() ?? [];

                    // A conflict or a build increment is judged by the branch,
                    // below, and not by the column: it starts and ends in
                    // review, so "did not move" is what success looks like. The
                    // column is still read and still reported.
                    if (report.Ended != report.From) report.Moved = true;
                    else if (conflict is null && build is null && report.Filed.Count == 0) report.Stalled = true;
                }
                catch (HatchException e)
                {
                    // Not knowing where it ended up is not the same as knowing
                    // it went nowhere. A stall is written on the ticket in front
                    // of a person, so a read that did not happen must not
                    // become one.
                    say.Complain(e.Message);
                    report.Flag ??= "where it ended up is not known - the board did not answer";
                }

                // The verdict on a conflict increment: the branch on origin,
                // fetched now, asked of git again. Never when the lease was
                // lost - the ticket is somebody else's by then, and what the
                // board is told about its branch is theirs to say. Never on a
                // usage limit either: there is no fix to judge, only a session
                // that did not finish.
                if (conflict is not null && claim.Lost is null && !report.UsageLimited && !report.Preempted) await JudgeAsync(report, conflict, ct);

                // The same for a build increment, and the same reason: what it
                // did is on origin's branch. Whether that fixed the build is the
                // board's to say, later, when the build on the new tip has run.
                if (build is not null && claim.Lost is null && !report.UsageLimited && !report.Preempted) await JudgeBuildAsync(report, build, ct);

                // Whatever the session asked for on its way out. This is the
                // half of the loop that makes asking worth doing: an unattended
                // run's questions are the one thing in its output that somebody
                // has to act on, and they would otherwise be a paragraph in the
                // middle of a transcript nobody scrolls back through.
                int? open = null;
                try
                {
                    var after = await board.QuestionsAsync(report.Key, open: true, ct);
                    open = after.Count;

                    var asked = after.Where(q => !before.Contains(q.Id)).ToList();
                    report.Asked = asked.Count;

                    if (asked.Count > 0)
                    {
                        say.Line("");
                        say.Line($"--- {report.Key} asked {asked.Count} question(s) ---");
                        say.Line("");
                        foreach (var line in Questions.Draw(asked)) say.Line(line);
                        say.Line($"  hatch answer {report.Key}");
                        say.Line($"  {board.Client.Origin}/issues/{report.Key}");
                    }
                }
                catch (HatchException e)
                {
                    say.Complain(e.Message);
                }

                // And if the board says nothing happened, say so on the ticket -
                // once quietly, and only the second time in a row with a flag.
                // Not for an increment whose lease went: writing a stall onto a
                // ticket another runner now holds would flag their increment as
                // ours, and the ticket did not move because we stopped - which
                // the loop already knows. And not for a usage limit: that is not
                // a stall, it is the one increment kind whose ticket is left
                // clean and unquestioned on purpose.
                //
                // Stalled and Moved are not mutually exclusive here: a conflict
                // or build increment is judged by its branch above, and can move
                // the ticket to a new column while still leaving that branch
                // unresolved. This guard runs regardless, so the branch is still
                // said - but see below for who wins the claim's own verdict.
                if (report.Stalled && !report.LostLease && !report.UsageLimited && !report.Preempted)
                {
                    if (report.Asked > 0)
                    {
                        // The session already asked its own question on the way
                        // out, and a question already does everything a stall
                        // comment would: it blocks the next dispatch and badges
                        // the card. A second comment here would say the same
                        // thing back in different words.
                        report.ReleaseOutcome = ClaimOutcomes.Worked;
                    }
                    else if (work.LetGo == 0)
                    {
                        // The first increment in a row to leave this ticket
                        // where it found it is let go of quietly rather than
                        // flagged: most of the time whatever happened is
                        // weather, and a retry a few minutes later just works.
                        // The ticket goes straight back onto the board, free
                        // for the next pass to try again - one comment saying
                        // why, and what to resume, and nothing that blocks it.
                        var why = said is { Length: > 0 }
                            ? $"the session ended with an error - \"{said}\""
                            : "the session ended without moving it";

                        await LetGo.LeftAsync(board, say, report.Key, why, report.SessionId, ct);
                        report.LetGo = true;
                        report.Flag = "let go";
                        report.ReleaseOutcome = ClaimOutcomes.Dropped;
                        say.Line("");
                        say.Line($"hatch: {report.Key} moved nothing - let go, and going on to the next");
                    }
                    else
                    {
                        // The second increment in a row to do the same thing:
                        // whatever the first one quietly let go of has
                        // repeated, and that is worth a person's eye - flagged
                        // exactly as every stall used to be, before this ticket.
                        await FlagStallAsync(report, open, work.LetGo, ct);
                        report.ReleaseOutcome = ClaimOutcomes.Dropped;
                    }
                }

                // The claim's own verdict: worked for anything that changed
                // something on the ticket's behalf, and that always wins over a
                // stall reported alongside it - a conflict or build increment
                // that moved the ticket did work, whatever its branch still
                // needs. Left at whatever the guard above decided (Dropped, or
                // still null) for everything else - a lost lease, a usage
                // limit, and the "not known" reads above never reach a verdict
                // at all.
                if (report.Preempted) report.ReleaseOutcome = ClaimOutcomes.Preempted;
                else if (report.Moved || report.Resolved || report.FixPushed || report.Filed.Count > 0) report.ReleaseOutcome = ClaimOutcomes.Worked;
            }
            catch (OperationCanceledException)
            {
                // The only place both tokens are visible: ct is what this
                // method operated with, and caller is what an actual Ctrl-C
                // would have cancelled. A skip cancels only the former, so a
                // cancellation that leaves the latter untouched is the
                // keyboard's k and not an interrupt.
                if (ct.IsCancellationRequested && !caller.IsCancellationRequested)
                {
                    report.Skipped = true;
                }
                else
                {
                    report.Interrupted = true;
                    report.Flag ??= "interrupted - nothing further could be read or written";
                }
            }

            report.EndedAt = DateTimeOffset.UtcNow;
            return report;
        }
        finally
        {
            _readout.EndIncrement();
            _controls?.SetIncrementRunning(false);
        }
    }

    // ---- The verdict ----

    /// <summary>
    /// What became of a conflict, asked of git and not of the column.
    /// </summary>
    /// <remarks>
    /// Three answers, and only one of them is a stall. Nothing conflicts any
    /// more: resolved, and reported as such. Something still does: a stall,
    /// flagged like any other, with the files named - a conflict nobody can
    /// resolve costs one increment and not a night. And a check that could not be
    /// made is neither: not knowing is not the same as knowing it went nowhere,
    /// which is the rule the column read above follows too.
    /// </remarks>
    private async Task JudgeAsync(IncrementReport report, ConflictRun conflict, CancellationToken ct)
    {
        Rechecked judged;
        try
        {
            judged = await conflict.Judge(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            say.Complain($"hatch: {report.Key} - could not check whether its branch still conflicts - {e.Message}");
            report.Flag ??= "whether the branch still conflicts is not known - the check failed";
            return;
        }

        report.ConflictTrunk = judged.Trunk ?? report.ConflictTrunk;

        if (judged.Conflicts.Count > 0)
        {
            report.Stalled = true;
            report.StillConflicting = judged.Files;
            say.Line("");
            say.Line($"hatch: {report.Key} still conflicts with {report.ConflictTrunk} in {judged.Files.Count} file(s): {string.Join(", ", judged.Files)}");
            return;
        }

        if (judged.Unknown)
        {
            say.Complain($"hatch: {report.Key} - whether its branch still conflicts with {report.ConflictTrunk} could not be checked");
            report.Flag ??= "whether the branch still conflicts is not known - the check could not be made";
            return;
        }

        report.Resolved = true;
        say.Line("");
        say.Line($"hatch: {report.Key} conflicts with {report.ConflictTrunk} resolved");
    }

    /// <summary>
    /// What became of a failing build, asked of origin's branch and not of the
    /// column - and not of the build, which will not have run by the time the
    /// session ends.
    /// </summary>
    /// <remarks>
    /// Three answers, and only one of them is a stall. A new tip was pushed: a
    /// fix, reported as one, with the build on it pending - the runner has
    /// already told the board so, marked as a build increment's, so that if the
    /// build fails again the board asks and no other agent is sent. The tip did
    /// not move: a stall, flagged like any other with the checks named, so a build
    /// nobody can fix costs one increment and not a night. And a tip that could
    /// not be read is neither, which is the rule the column read follows too.
    /// </remarks>
    private async Task JudgeBuildAsync(IncrementReport report, BuildRun build, CancellationToken ct)
    {
        report.BuildBranch = build.Found.StillFailing.FirstOrDefault()?.Branch;
        report.BuildSha = build.Found.StillFailing.FirstOrDefault()?.TipSha;

        BuildJudged judged;
        try
        {
            judged = await build.Judge(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            say.Complain($"hatch: {report.Key} - could not check whether a fix was pushed - {e.Message}");
            report.Flag ??= "whether a fix was pushed is not known - the check failed";
            return;
        }

        if (judged.Pushed)
        {
            report.FixPushed = true;
            say.Line("");
            say.Line($"hatch: {report.Key} fix pushed - the build on it is pending");
            return;
        }

        if (judged.Unknown)
        {
            say.Complain($"hatch: {report.Key} - whether a fix was pushed could not be checked");
            report.Flag ??= "whether a fix was pushed is not known - origin's branch could not be read";
            return;
        }

        report.Stalled = true;
        report.StillFailing = build.Found.Names;
        say.Line("");
        say.Line($"hatch: {report.Key} pushed nothing - its build still fails ({string.Join(", ", report.StillFailing)})");
    }

    // ---- The session ----

    private async Task<SessionResult> SpawnAsync(
        WorkDto work, string root, string model, string effort, bool quiet,
        RunFacts facts, Claim claim, CancellationToken ct,
        IReadOnlyList<string>? addDirs, IReadOnlyList<Checkouts.RepositoryLine>? repositories,
        IReadOnlyList<BranchEntry>? branches, Rechecked? conflict, BuildFound? build)
    {
        // The hooks a message sent while this runs reaches the session by. Made
        // for this increment and deleted with it, in a directory of its own.
        using var hooks = SessionHooks.Write(_temp, work.Issue.Key, SessionHooks.Binary(Environment.ProcessPath));
        if (hooks is null)
            say.Complain($"hatch: {work.Issue.Key} - could not write the hooks a message reaches the session by; one sent now waits for the next session");

        var budget = work.Playbook?.Budget is { } m ? m * 1_000_000L : (long?)null;
        if (budget is { } limitTokens && hooks is not null)
        {
            try
            {
                File.WriteAllText(hooks.Budget, limitTokens.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A budget that cannot be written must not fail the increment -
                // a --quiet session's hook simply has nothing to clamp against.
            }
        }

        var prompt = Prompt.Compose(work, repositories, branches, conflict, build);
        var request = new SessionRequest(root, model, effort, prompt, quiet, addDirs, hooks?.Settings);
        var render = new StreamRender(root, facts);

        await MarkSaidAsync(work, ct);

        if (quiet)
        {
            // No renderer, so the facts come from the CLI's own summary instead:
            // one object at the end carrying the session id, the cost, and the
            // last thing the session said. Nothing is carried on the claim, and
            // that is what --quiet is rather than a gap to engineer around.
            var quietly = await sessions.RunAsync(request, null, ct);
            var output = quietly.Output.Trim();

            if (output.StartsWith('{'))
            {
                // What the session said on its way out, and then the same object
                // read as the event a stream ends with - so the closing lines and
                // the facts come from one place rather than being written twice.
                //
                // Handed over whole rather than flattened onto a line: the shell
                // needed a line because its renderer read a pipe, and the text a
                // session ends with is made of newlines that matter.
                if (StreamRender.Closing(output) is { Length: > 0 } closing) say.Line(closing);
                foreach (var line in render.Read(output)) say.Line(line);
            }
            else if (output.Length > 0)
            {
                // Not JSON, so it is the CLI complaining - or, on a crash that
                // never reached its final object, whatever of the run's own
                // output survived. Kept as the last-resort text a usage limit
                // can still be read from in quiet mode, the same as the
                // streamed path keeps the assistant's last line for the same
                // reason.
                facts.LastAssistantText ??= output;
                say.Line(output);
            }

            if (facts.Result is { } quietEntry) facts.Result = quietEntry with { PromptChars = prompt.Length };

            return quietly;
        }

        using var pulse = new Pulse(settings.HeartbeatSeconds, say);

        var streamed = await sessions.RunAsync(request, raw =>
        {
            foreach (var line in render.Read(raw))
            {
                say.Line(line);
                pulse.Said(line);
                _readout.Activity(render.TokensSoFar, DateTimeOffset.UtcNow, pulse.Inside);

                // Only what the run is inside of goes to the board. An empty
                // line is a blank in a transcript, and the heartbeat reads it as
                // "no change" rather than clearing a card mid-run.
                if (line.Length > 0) claim.Chatter.Line = line.Trim();
            }

            // Outside the loop above, and reading the property rather than
            // Read's own (always empty) result: a rate_limit_event draws no
            // line, so the foreach body never runs for the raw line that
            // carried one - but Read already updated Usage by the time it
            // returned.
            if (render.UsageReadAt is { } readAt) _readout.SetUsage(render.Usage, readAt);

            // Same reason the usage read above is out here and not in the loop:
            // a usage-only assistant message draws no line either, yet still
            // moves TokensSoFar. The first crossing, and only the first - once
            // hooks.Clamp exists nothing here writes it again, so the session is
            // told to wrap up once for the rest of its life and not on every
            // line after.
            if (budget is { } limit && hooks is not null && render.TokensSoFar >= limit && !File.Exists(hooks.Clamp))
            {
                try
                {
                    File.WriteAllText(hooks.Clamp, JsonSerializer.Serialize(
                        new ClampFact(render.TokensSoFar, render.Requests), HatchJson.Default.ClampFact));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A clamp that cannot be written must not fail the
                    // increment; the session runs on undirected.
                }
            }
        }, ct);

        if (facts.Result is { } streamedEntry)
            facts.Result = streamedEntry with
            {
                Requests = render.Requests,
                PeakContextTokens = render.PeakContextTokens,
                PromptChars = prompt.Length,
            };

        return streamed;
    }

    /// <summary>
    /// Marks read exactly the messages the prompt is about to carry, and no
    /// others - one sent since the dispatch was read is not in the prompt, and
    /// is left for the hooks to deliver. Public because an attached session is
    /// handed the same prompt.
    /// </summary>
    /// <remarks>
    /// A failure is said and does not stop the increment. The message stays
    /// unread, and the session's own hooks will hand it over at its first step.
    /// </remarks>
    public async Task MarkSaidAsync(WorkDto work, CancellationToken ct)
    {
        if (work.Messages is not { Count: > 0 } said) return;

        try
        {
            await board.DeliverMessagesAsync(work.Issue.Key, said.Select(m => m.Id).ToList(), ct);
        }
        catch (HatchException e)
        {
            say.Complain(e.Message);
            say.Complain($"hatch: {work.Issue.Key} - could not mark what was said to the session as read; the session's hooks will hand it over");
        }
    }

    // ---- The meter ----

    /// <summary>
    /// What the increment just spent, on the ticket it spent it: one row per
    /// agent session, which is the only record anywhere that knows <em>which</em>
    /// ticket the money went on.
    /// </summary>
    /// <remarks>
    /// Nothing here may fail the increment. The work happened either way, and a
    /// loop must not stop because a meter did not.
    /// </remarks>
    private async Task PostWorkLogAsync(IncrementReport report, RunFacts facts, DateTimeOffset started, CancellationToken ct)
    {
        var key = report.Key;

        // No result event arrived - an interrupted run, or a CLI that fell over
        // before it could report. Nothing is posted, because a row of zeros
        // would be a claim rather than a record.
        if (facts.Result is not { } entry)
        {
            say.Complain($"hatch: no work log entry for {key} - the run ended before it said what it had spent");
            return;
        }

        // Wall clock either side of the CLI. Deliberately not reconciled with
        // the duration the session reports: this pair says when the increment
        // occupied the machine, and DurationMs says how long it was thinking,
        // which is the smaller number and the honest answer to "how long did
        // this take".
        var row = entry with { StartedAt = started, EndedAt = DateTimeOffset.UtcNow };

        try
        {
            var written = await board.WorkLogAsync(key, row, ct);
            say.Line(written is null
                ? $"hatch: work log: written to {key}"
                : $"hatch: work log: {Format.Compact(written.TotalTokens)} tokens, "
                  + $"{Format.Spent(written.CostUsd)}, {StreamRender.Clock(written.DurationMs / 1000)}"
                  + $", {(written.Requests is { } r ? $"{r} requests" : "requests not reported")}"
                  + $", {(written.PeakContextTokens is { } p ? $"{Format.Compact(p)} peak context" : "peak context not reported")}");

            // The server's own figure, when it wrote one - so the closing
            // banner and the issue page never disagree about the headline.
            if (written is not null)
            {
                report.TotalTokens = written.TotalTokens;
                report.Requests = written.Requests;
                report.PeakContextTokens = written.PeakContextTokens;
            }
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            say.Complain($"hatch: the work log entry for {key} could not be written - {e.Message}");
        }
    }

    // ---- The stall guard ----

    /// <summary>
    /// An increment that ended with the ticket in the column it found it in,
    /// said where a person will see it.
    /// </summary>
    /// <remarks>
    /// <para>This is the one failure mode of an unattended loop that is
    /// dangerous rather than merely disappointing. Nothing about the board
    /// changed, so the next pass picks the same issue, spends the same money and
    /// fails the same way - and does it all night. Every other way an increment
    /// can go badly costs one increment.</para>
    ///
    /// <para>The flag is an open question, because a question already does all
    /// three things a flag field would have to be taught: it blocks the issue
    /// from being dispatched again, it badges the card on the board, and it is
    /// the list <c>hatch.sh answer</c> walks. Answering it clears the flag,
    /// which is the right gesture - the flag means "nobody has looked at this",
    /// and answering is somebody having looked.</para>
    ///
    /// <para>Neither option is recommended, and that is not modesty.
    /// A recommendation is for a choice something knows the answer to, and the
    /// whole content of a stall is that nothing here knows why it happened.</para>
    ///
    /// <para>Reached only for the second increment in a row to leave a ticket
    /// where it found it - <paramref name="letGo"/> counts the ones before
    /// this that already went quietly (see <see cref="LetGo.LeftAsync"/>), so
    /// it is always at least one here, and the comment says which one this is.</para>
    /// </remarks>
    private async Task FlagStallAsync(IncrementReport report, int? open, int letGo, CancellationToken ct)
    {
        // Nothing is written on a guess. If the questions could not be read,
        // whether this issue is already flagged is not known, and a second
        // question under the first is one more thing for somebody to answer
        // saying no more than it did.
        if (open is not { } waiting)
        {
            report.Flag = "not flagged - its questions could not be read";
            say.Complain($"hatch: {report.Key} moved nothing and could not be flagged - a later pass may offer it again");
            return;
        }

        var body = report.StillFailing.Count > 0
            ? $"""
              An unattended increment ran here to fix this branch's failing build, and the branch
              on origin has not moved: {report.BuildBranch} is still at {report.BuildSha}, and the build on it
              still fails. The checks that fail:

              {string.Join('\n', report.StillFailing.Select(f => $"- {f}"))}
              """
            : report.StillConflicting.Count > 0
            ? $"""
              An unattended increment ran here to resolve this branch's conflicts with
              {report.ConflictTrunk}, and the branch on origin still conflicts with it. The
              files that still conflict:

              {string.Join('\n', report.StillConflicting.Select(f => $"- {f}"))}
              """
            : $"""
              An unattended increment ran here and left this issue where it found it:
              still in "{report.From}", under a playbook moving {report.From} -> {report.To}. The
              board is the report that counts, and it says nothing happened.
              """;

        body += $"\n\nThis is the {Ordinal(letGo + 1)} increment in a row to leave this ticket here.";

        body += report.SessionId is { Length: > 0 } session
            ? $"\n\nThe session it ran in is still there, with everything it did in context:\n\n    claude --resume {session}"
            : "\n\nThere is no session to resume: the run ended before it said what its id was.";

        if (report.ExitCode != 0) body += $"\n\nIt exited {report.ExitCode}.";

        say.Line("");
        if (waiting > 0)
        {
            // A question already open on the ticket is this flag, raised -
            // usually the session's own, asked on the way out by something that
            // knew why it was stopping. It blocks the next dispatch and badges
            // the same card, so all that is left to do is write down which
            // session it was.
            body += "\n\nThere is already a question open here, and that is the flag: nothing further\n"
                  + "will be dispatched at this issue until somebody answers it.";
            say.Line($"hatch: {report.Key} moved nothing, and is already waiting on a question");
        }
        else
        {
            body += "\n\nA question goes up with this comment, so nothing further will be dispatched at\n"
                  + "this issue until somebody answers it.";
            say.Line($"hatch: {report.Key} moved nothing - flagging it, and going on to the next");
        }

        say.Line("");

        // A stall is not a reason to stop, and neither is failing to write one
        // down. The comment goes on either way - it is where the session id
        // lives, and resuming the conversation is most of why a stall is worth
        // recording rather than merely counting. The question goes up only when
        // there is not one there.
        try
        {
            await board.CommentAsync(report.Key, body, ct);

            if (waiting == 0)
                await board.AskAsync(
                    report.Key,
                    $"An unattended increment left {report.Key} in \"{report.From}\" without moving it - what should happen to it now?",
                    StallAnswers.Options(),
                    ct);

            report.Flag = waiting > 0 ? "waiting on a question" : "flagged";
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            report.Flag = "not flagged - the write was refused";
            say.Complain($"hatch: {report.Key} could not be flagged - a later pass may offer it again");
        }
    }

    /// <summary>
    /// <c>2</c> as "second", the common case - a lone prior let-go turning
    /// into this, its flagged sequel - through <c>10</c> named in full, and a
    /// plain numeral suffixed past that: nobody needs "twelfth" read out, but
    /// "12th" says exactly the same thing in fewer words.
    /// </summary>
    internal static string Ordinal(int n)
    {
        string[] named = ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth"];
        if (n >= 0 && n < named.Length) return named[n];

        if (n % 100 is >= 11 and <= 13) return $"{n}th";

        return (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };
    }
}

/// <summary>
/// The rendered lines, plus a pulse when there are none.
/// </summary>
/// <remarks>
/// The renderer covers everything the agent says; this covers the gaps between,
/// which is where the doubt actually lives - a three-minute <c>make test-api</c>
/// produces no events at all, and silence is indistinguishable from a crash. So
/// the last thing seen is held onto and said back: "still working - Bash make
/// test-api (2m14s)" is the difference between waiting and wondering.
/// </remarks>
public sealed class Pulse : IDisposable
{
    private readonly Terminal _say;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Timer? _timer;
    private readonly Lock _gate = new();
    private TimeSpan _lastSaid;
    private string _inside = "starting up";

    /// <summary>What the run is inside of right now - the readout's live version of the same fact.</summary>
    public string Inside { get { lock (_gate) return _inside; } }

    public Pulse(int everySeconds, Terminal say)
    {
        _say = say;
        if (everySeconds <= 0) return;

        var every = TimeSpan.FromSeconds(everySeconds);
        _timer = new Timer(_ => Tick(every), null, every, TimeSpan.FromSeconds(1));
    }

    /// <summary>A line went out, so the silence starts again from here.</summary>
    public void Said(string line)
    {
        lock (_gate)
        {
            _lastSaid = _elapsed.Elapsed;

            // Tool calls, and nothing else - the thinking counter is already a
            // pulse, and prose is not a thing the run can be stuck inside of.
            var mark = line.IndexOf("⏺ ", StringComparison.Ordinal);
            if (mark >= 0) _inside = line[(mark + 2)..].Trim();
        }
    }

    private void Tick(TimeSpan every)
    {
        // A timer callback runs on its own thread, outside anything an
        // increment's own try/catch could reach - an exception here takes the
        // whole process down, claim and all, so nothing above this line may
        // escape it.
        try
        {
            string inside;
            long seconds;

            lock (_gate)
            {
                if (_elapsed.Elapsed - _lastSaid < every) return;

                _lastSaid = _elapsed.Elapsed;
                inside = _inside;
                seconds = (long)_elapsed.Elapsed.TotalSeconds;
            }

            _say.Line($"  · still working - {inside} ({seconds / 60}m{seconds % 60:00}s)");
        }
        catch (Exception)
        {
        }
    }

    public void Dispose() => _timer?.Dispose();
}

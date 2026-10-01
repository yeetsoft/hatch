using System.Globalization;

namespace Hatch.Cli;

/// <summary>
/// The tally, accumulated as the loop goes rather than reconstructed at the end
/// - because an interrupted run has to be able to print it too, and an
/// interrupted run is exactly the one with nothing left to reconstruct from.
/// </summary>
public sealed class Tally
{
    private readonly TimeProvider _clock;
    private readonly List<string> _moved;
    private readonly List<string> _stalled;
    private readonly List<string> _letGo;
    private readonly List<string> _interrupted;
    private readonly List<string> _preempted;
    private readonly List<string> _failed = [];
    private readonly DateTimeOffset _started;

    /// <summary>
    /// A fresh night, or one already part-spent - <paramref name="carried"/>
    /// being what the incarnation before a restart handed forward. Everything a
    /// bound is measured against is seeded from it, which is what stops a
    /// restart from being a way to start the budget over.
    /// </summary>
    public Tally(TimeProvider clock, NightState? carried = null)
    {
        _clock = clock;
        _moved = [.. carried?.Moved ?? []];
        _stalled = [.. carried?.Stalled ?? []];
        _letGo = [.. carried?.LetGo ?? []];
        _interrupted = [.. carried?.Interrupted ?? []];
        _preempted = [.. carried?.Preempted ?? []];

        // The night's start and not this process's, so the elapsed time in the
        // morning covers the whole of it. A state file with no start in it is
        // taken as starting now rather than in the year zero.
        _started = carried is { Started.Year: > 1 } ? carried.Started : clock.GetUtcNow();

        Runs = carried?.Runs ?? 0;
        Spent = carried?.Spent ?? 0m;
        Fails = carried?.Fails ?? 0;
        Restarts = carried?.Restarts ?? 0;
        ExhaustedUntil = carried?.ExhaustedUntil;
        ExhaustedKnown = carried?.ExhaustedKnown ?? true;
    }

    public int Runs { get; private set; }
    public decimal Spent { get; private set; }

    /// <summary>When the night began - carried across a restart, the same as everything else it is judged against.</summary>
    public DateTimeOffset Started => _started;

    /// <summary>Increments that exited non-zero, in a row.</summary>
    public int Fails { get; private set; }

    /// <summary>How many times the loop has come back as a newer version of itself.</summary>
    public int Restarts { get; }

    /// <summary>
    /// The last increment ran out of Claude usage, and this is when it expects
    /// to reset - null on an ordinary night, and cleared the moment an
    /// increment finishes without hitting one.
    /// </summary>
    public DateTimeOffset? ExhaustedUntil { get; private set; }

    /// <summary>Whether <see cref="ExhaustedUntil"/> came from the session, or is the one-hour backstop.</summary>
    public bool ExhaustedKnown { get; private set; } = true;

    /// <summary>
    /// The reset instant has passed - told rather than let expire on its own,
    /// so the very next heartbeat says this runner is not out any more instead
    /// of repeating a stale instant until the next increment happens to clear it.
    /// </summary>
    public void ClearExhausted()
    {
        ExhaustedUntil = null;
        ExhaustedKnown = true;
    }

    /// <summary>The sentence naming what ended the run.</summary>
    public string? StopWhy { get; set; }

    // Everything the loop has to stop for, and none of them set by default. An
    // unattended run that stopped for a reason nobody asked for would be a run
    // somebody has to check on, which is the thing being built away from.
    //
    // Settable rather than fixed at construction, because three of them are
    // also a control on a page: the board answers a heartbeat with the bounds
    // it would like, and the loop folds them in between increments. The
    // counters they are compared against keep counting either way, so a cap
    // moved below what a night has already spent stops it at the next check -
    // which is the honest reading of a cap.
    public int? MaxRuns { get; set; }
    public decimal? MaxSpend { get; set; }
    public string? Until { get; set; }
    public DateTimeOffset? UntilAt { get; set; }
    public string? StopFile { get; init; }

    /// <summary>What the keyboard has decided - null where this run reads no keys at all.</summary>
    public Controls? Controls { get; init; }

    /// <summary>
    /// Whether this run is over, and why - asked before every increment and
    /// through every wait. One method, because "every exit path" is not
    /// something several call sites can promise between them.
    /// </summary>
    public bool ShouldStop()
    {
        StopWhy = null;

        // First of all: a key the operator pressed is a more direct answer to
        // "should this stop" than any of the counters below, and the increment
        // in flight has already finished by the time this is asked.
        if (Controls?.Snapshot() is { StopArmed: true })
        {
            StopWhy = "the keyboard asked this runner to stop after this increment";
            return true;
        }

        // Next, because it is the one that says something is wrong rather than
        // something is finished.
        if (Fails >= 3)
        {
            StopWhy = $"three increments in a row failed: {string.Join(", ", _failed)}";
            return true;
        }

        // A file, so that stopping a loop needs nothing but a shell and a path -
        // no pid to find, no signal to send, and nothing that could land in the
        // middle of a push. The increment in flight finishes first; this is only
        // ever read between them.
        if (StopFile is { Length: > 0 } stop && Path.Exists(stop))
        {
            StopWhy = $"{stop} exists";
            return true;
        }

        if (MaxRuns is { } cap && Runs >= cap)
        {
            StopWhy = $"--max-runs {cap} reached";
            return true;
        }

        if (MaxSpend is { } budget && Spent >= budget)
        {
            StopWhy = $"--max-spend {budget.ToString(CultureInfo.InvariantCulture)} reached at ${Format.Money(Spent)}";
            return true;
        }

        if (UntilAt is { } hour && _clock.GetUtcNow() >= hour)
        {
            StopWhy = $"--until {Until} has come";
            return true;
        }

        return false;
    }

    /// <summary>One increment, added to the run's account.</summary>
    public void Record(IncrementReport report)
    {
        Runs++;
        if (report.Cost is { } cost) Spent += cost;

        // Set on every increment and cleared the moment one finishes without
        // hitting one - a person restarting the loop by hand is the only other
        // way this clears, since a fresh incarnation carries nothing forward.
        ExhaustedUntil = report.UsageLimitResetAt;
        ExhaustedKnown = report.UsageLimitResetKnown;

        // A third list, because a usage limit is neither: nothing was moved,
        // and nothing is waiting on a person either - the next runner with
        // usage left picks this ticket straight up. It does not touch the
        // failure streak, the same reason a lost lease does not: three of these
        // in a row is the loop working correctly against a spent account, not
        // three broken increments.
        if (report.UsageLimited)
        {
            _interrupted.Add($"hatch:   usage    {report.Key}  {report.Outcome}");
            return;
        }

        // A sixth list, and not a failure: the board ordered this ticket put
        // down for an emergency, which says nothing about whether the
        // increment itself was going well - three of these in a row must not
        // arm the three-strikes stop the way three broken increments should.
        if (report.Preempted)
        {
            _preempted.Add($"hatch:   preempt  {report.Key}  {report.Outcome}");
            return;
        }

        // Three lists rather than two, because a let-go ticket is neither of
        // the other mornings: the moved ones are what the night got done, the
        // stalled ones are what is waiting on somebody, and the let-go ones are
        // what nothing needs to be done about yet - the first increment in a
        // row to leave a ticket alone, freed for the next pass rather than
        // flagged. A conflict that was resolved is something the night got
        // done, though the ticket did not move, and so is a fix pushed to a
        // failing build.
        if (report.Moved || report.Resolved || report.FixPushed) _moved.Add($"hatch:   moved    {report.Key}  {report.Outcome}");
        else if (report.LetGo) _letGo.Add($"hatch:   let go   {report.Key}  {report.Outcome}");
        else _stalled.Add($"hatch:   stalled  {report.Key}  {report.Outcome}");

        // A lost lease is not a failure. It is the loop working correctly on a
        // busy board - the ticket went to somebody who was already further into
        // it - and three of them in a row must not end a night the way three
        // broken increments should. It does not reset the streak either: it says
        // nothing about whether the last increment worked.
        if (report.LostLease) return;

        // A failed increment on its own is not a reason to stop - a ticket can
        // be wrong, or a test can be flaky, and the next ticket is a different
        // question. Three in a row is something else: whatever is broken is
        // broken for every ticket, and the loop is now spending money to prove
        // it.
        if (report.ExitCode != 0)
        {
            Fails++;
            _failed.Add($"{report.Key} (exit {report.ExitCode})");
        }
        else
        {
            Fails = 0;
            _failed.Clear();
        }
    }

    /// <summary>
    /// The night so far, for the incarnation that is about to take it over -
    /// counting the restart that is being asked for, since this is only ever
    /// called on the way out to one.
    /// </summary>
    public NightState ToState() => new()
    {
        Runs = Runs,
        Spent = Spent,
        Started = _started,
        Fails = Fails,
        Restarts = Restarts + 1,
        UntilAt = UntilAt,
        Moved = _moved,
        Stalled = _stalled,
        LetGo = _letGo,
        Interrupted = _interrupted,
        Preempted = _preempted,
        ExhaustedUntil = ExhaustedUntil,
        ExhaustedKnown = ExhaustedKnown,
    };

    /// <summary>What the night has come to so far, in one line, on the way to a restart.</summary>
    public string SoFar() => $"{Runs} increment(s), ${Format.Money(Spent)} so far, carried forward";

    /// <summary>
    /// What the run came to. Printed on the way out and nowhere else, because
    /// the reasons a loop ends include the ones nobody wrote code for - an
    /// interrupt, a failure, a terminal closing - and those are precisely the
    /// runs whose tally somebody needs.
    /// </summary>
    public void Print(Terminal say)
    {
        var elapsed = (long)(_clock.GetUtcNow() - _started).TotalSeconds;

        // Every incarnation's, because the night is the thing somebody wanted
        // counted and a restart is an implementation detail of it. The restarts
        // are named only when there were some, so a night without one reads
        // exactly as it always did.
        var restarts = Restarts > 0 ? $", {Restarts} restart(s)" : "";

        say.Line("");
        if (StopWhy is { Length: > 0 } why) say.Line($"hatch: {why}");
        say.Line($"hatch: {Runs} increment(s) in {Format.Duration(elapsed)}, ${Format.Money(Spent)}{restarts}");
        say.Lines(_moved);
        say.Lines(_stalled);
        say.Lines(_letGo);
        say.Lines(_interrupted);
        say.Lines(_preempted);
    }
}

/// <summary>
/// Something said in full the first time, again whenever the answer changes, and
/// otherwise rarely.
/// </summary>
/// <remarks>
/// An idle loop and a busy board are both things somebody left running; either
/// should be able to say it is alive without filling a scrollback with the same
/// sentence six hundred times - and the one pass whose reasons changed, because
/// somebody answered a question at three in the morning, is exactly the one
/// nobody would find in that.
/// </remarks>
public sealed class SaidOnce
{
    private string? _digest;
    private DateTimeOffset _since;
    private DateTimeOffset _last;

    /// <summary>Whether nothing has happened since this last had something to say.</summary>
    public bool Running => _since != default;

    /// <summary>How long that has been going on.</summary>
    public TimeSpan For(DateTimeOffset now) => now - _since;

    /// <summary>Whether the whole report should go out: the first time, or because it changed.</summary>
    public bool InFull(string digest, DateTimeOffset now)
    {
        if (Running && _digest == digest) return false;

        _digest = digest;
        _last = now;
        if (!Running) _since = now;
        return true;
    }

    /// <summary>Whether it is time for the short reminder that nothing has changed.</summary>
    public bool Again(DateTimeOffset now, TimeSpan after)
    {
        if (now - _last < after) return false;

        _last = now;
        return true;
    }

    /// <summary>Something happened. The next quiet spell starts from scratch.</summary>
    public void Clear()
    {
        _digest = null;
        _since = default;
        _last = default;
    }
}

/// <summary>
/// The command this epic is named for: pick the next actionable issue, spend one
/// increment on it, and do it again.
/// </summary>
/// <remarks>
/// The loop is a program and not the model. A fresh context per ticket is
/// cheaper, and a session that has been running for six hours is one whose
/// earliest decisions nobody can audit.
/// </remarks>
public sealed class GoToWorkCommand(Runtime runtime)
{
    /// <summary>How long to wait between reminders that nothing has changed.</summary>
    private static readonly TimeSpan StillNothing = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How stale a usage probe's reading is allowed to get before the top of a
    /// pass asks again, on an otherwise idle loop. The server's own freshness
    /// window is five minutes; this plus one <c>--interval</c> stays inside it -
    /// see the decisions on HA-173, criterion 1.4.
    /// </summary>
    private static readonly TimeSpan ProbeMaxAge = TimeSpan.FromMinutes(2);

    /// <summary>
    /// What the runner exits with to ask the supervisor in <c>scripts/hatch.sh</c>
    /// or <c>scripts/hatch.ps1</c> to build the new source and run it again.
    /// </summary>
    /// <remarks>
    /// 75 is <c>EX_TEMPFAIL</c> - "try again" - and collides with nothing else
    /// the runner answers with: 0 for a night that ended, 1 for a refusal, 130
    /// for an interrupt. A process cannot exec itself into a newer build, so
    /// asking to be replaced is the only move available from in here.
    /// </remarks>
    public const int RestartExitCode = 75;

    /// <summary>How long a loop runs before the backstop restarts it anyway.</summary>
    private const int RestartAfterMinutes = 30;

    /// <summary>Changed paths named on a restart before the rest are counted instead.</summary>
    private const int NamedPaths = 5;

    public static readonly string[] GoToWorkUsage =
    [
        "usage: hatch go-to-work [--mine] [--under <epic key>] [--once] [--quiet]",
        "                        [--interval <seconds>] [--max-runs <n>]",
        "                        [--max-spend <dollars>] [--until <HH:MM>]",
        "                        [--stop-file <path>] [--no-keys]",
        "                        [--restart-after <minutes> | --no-restart]",
        "                        [--repo <path>]... [--workspace <dir>]",
        "",
        "  `work` in a circle: the next actionable issue, one increment, ask again -",
        "  until nothing on the board is an agent's to move, and then wait and ask",
        "  again every interval. Run inside the checkout the board is about, or name",
        "  one or more with --repo or HATCH_REPOS; one loop per served checkout.",
        "",
        "  It takes no ticket key. One increment on a named ticket is `hatch work",
        "  AER-12`; this command's question is what is next, asked again and again.",
        "",
        "  --mine             take only the caller's own tickets - assigned to the",
        "                     person this key belongs to, or to the key itself. Plain",
        "                     go-to-work still skips every person's tickets, including",
        "                     your own; `hatch do-my-work` is this flag on by default,",
        "                     and the two are otherwise identical",
        "  --under            stay inside one epic's subtree",
        "  --once             one pass, and out",
        "  --quiet            no per-increment stream, only what each one ended as",
        "  --interval         seconds to wait when there was nothing to do (default 60)",
        "  --repo <path>      also serve this checkout, repeatable - the whole list for",
        "                     this run, beside the standing checkout if there is one;",
        "                     HATCH_REPOS is not consulted when this is given",
        "  --workspace <dir>  clone every repository the board binds that this runner",
        "                     has no checkout of, into <dir> - HATCH_WORKSPACE is not",
        "                     consulted when this is given",
        "",
        "  None of the bounds are set by default - an unattended run that stopped for",
        "  a reason nobody asked for is a run somebody has to go and check on. Each is",
        "  read between increments and through every wait, so the increment in flight",
        "  always finishes:",
        "",
        "  --max-runs     stop after this many increments",
        "  --max-spend    stop once the run has cost this much, in dollars",
        "  --until        stop at this wall-clock hour - tomorrow, if it has gone by",
        "  --stop-file    stop once this path exists. `touch` it from anywhere: no pid",
        "                 to find, and no signal that could land in the middle of a push",
        "",
        "  --no-keys         read no keys from this terminal - Ctrl-C and --stop-file still work",
        "",
        "  A run also ends on three failed increments in a row, on a workspace that",
        "  cannot be reset, and when the board's Runners page asks this runner to stop.",
        "",
        "  A loop whose own source changed on the trunk asks to be restarted as the",
        "  new build, which needs something standing over the process to rebuild and",
        "  run it again. A hatch started by hand has no such supervisor, and these",
        "  three do nothing there:",
        "",
        "  --restart-after   come back as a newer build at least this often (default 30)",
        "  --restart-after 0 ...only when its own source actually changed",
        "  --no-restart      ...never",
        "",
        "  Exits 0 when the run ended; 1 on a refusal; 75 asking a supervisor to",
        "  rebuild and run it again; 130 on an interrupt.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(runtime.Say, GoToWorkUsage);

        string? key = null, under = null, until = null, stopFile = null;
        var interval = 60;
        var once = false;
        var quiet = false;
        var mine = false;
        var restartAfter = RestartAfterMinutes;
        var noRestart = false;
        int? maxRuns = null;
        decimal? maxSpend = null;
        var repoFlags = new List<string>();
        string? workspaceFlag = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mine": mine = true; break;
                case "--under" when i + 1 < args.Length: under = args[++i]; break;
                case "--interval" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out interval) || interval < 1)
                    {
                        // Zero is refused rather than clamped: it reads as "as
                        // fast as possible" and means a board asked the same
                        // question thousands of times a minute.
                        runtime.Say.Complain("hatch: --interval takes a number of seconds, at least one");
                        return 1;
                    }

                    break;
                case "--once": once = true; break;
                case "--quiet": quiet = true; break;
                case "--max-runs" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out var runs) || runs < 0)
                    {
                        runtime.Say.Complain("hatch: --max-runs takes a count");
                        return 1;
                    }

                    maxRuns = runs;
                    break;
                case "--max-spend" when i + 1 < args.Length:
                    if (!decimal.TryParse(args[++i], CultureInfo.InvariantCulture, out var spend) || spend < 0)
                    {
                        runtime.Say.Complain("hatch: --max-spend takes an amount in dollars");
                        return 1;
                    }

                    maxSpend = spend;
                    break;
                case "--until" when i + 1 < args.Length: until = args[++i]; break;
                case "--stop-file" when i + 1 < args.Length: stopFile = args[++i]; break;
                case "--restart-after" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out restartAfter) || restartAfter < 0)
                    {
                        // Refused rather than clamped, like --interval: a
                        // negative age is a loop that is always too old, which
                        // is a loop that does nothing but restart.
                        runtime.Say.Complain(
                            "hatch: --restart-after takes a number of minutes, at least one - or 0 to turn it off");
                        return 1;
                    }

                    break;
                case "--no-restart": noRestart = true; break;
                // Recognised so it is not refused as an unknown flag - Program.cs
                // already acted on it, before this command was ever constructed.
                case "--no-keys": break;
                case "--repo" when i + 1 < args.Length: repoFlags.Add(args[++i]); break;
                case "--repo":
                    return Usage.Refuse(runtime.Say, "go-to-work --repo takes a path", GoToWorkUsage);
                case "--workspace" when i + 1 < args.Length: workspaceFlag = args[++i]; break;
                case "--workspace":
                    return Usage.Refuse(runtime.Say, "go-to-work --workspace takes a directory", GoToWorkUsage);
                case var flag when flag.StartsWith('-'):
                    return Usage.Refuse(runtime.Say, $"go-to-work does not take {flag}", GoToWorkUsage);
                default: key = args[i]; break;
            }
        }

        // A key with --under is the refusal `work` makes, for the reason `work`
        // makes it. A key on its own is a refusal too, and a different one: this
        // command's question is "what is next", asked again and again, and one
        // ticket cannot be the answer to it twice.
        if (key is { Length: > 0 } && under is { Length: > 0 })
        {
            runtime.Say.Complain(
                "hatch: go-to-work takes --under or a key, not both - one names where to look, the other names the ticket");
            return 1;
        }

        if (key is { Length: > 0 })
        {
            runtime.Say.Complain(
                "hatch: go-to-work does not take a ticket - it asks the board what is next, until there is nothing.");
            runtime.Say.Complain($"hatch: one increment on {key} is  hatch work {key}");
            return 1;
        }

        var repoOverridden = repoFlags.Count > 0 || workspaceFlag is not null;
        if (repoOverridden)
        {
            // The whole list for this run, in place of Settings.Repos - the
            // highest layer wins whole, as the settings already fold, rather
            // than a merge nobody can predict from the command line. The
            // standing checkout, if there is one, is still named first. Likewise
            // for the workspace: a flag given here is this run's whole answer,
            // and HATCH_WORKSPACE is not folded in beside it.
            var standing = runtime.Checkouts.FirstOrDefault(c => c.Standing)?.Path;
            var effectiveRepos = repoFlags.Count > 0 ? repoFlags : runtime.Settings.Repos;
            var effectiveWorkspace = workspaceFlag ?? runtime.Settings.Workspace;

            if (!Checkouts.TryDiscover(
                    standing, effectiveRepos, effectiveWorkspace, out var repoCheckouts, out var strays, out var badRepo))
            {
                runtime.Say.Complain(badRepo);
                return 1;
            }

            foreach (var stray in strays)
                runtime.Say.Line($"hatch: {stray} - not a checkout, and nothing under it is one either; left alone");

            var root = repoCheckouts.Count > 0 ? repoCheckouts[0].Path : runtime.Root;

            // Not yet named for the new root - that asks the board, and asking
            // is deferred past the lock below, so a lock refusal touches the
            // wire for nothing.
            runtime = runtime with
            {
                Checkouts = repoCheckouts,
                Root = root,
                Settings = runtime.Settings with { Workspace = effectiveWorkspace },
            };
        }

        if (runtime.Checkouts.Count == 0 && runtime.Settings.Workspace is null)
        {
            runtime.Say.Complain("hatch: this is not a git repository, and a ticket is about a codebase.");
            runtime.Say.Complain(
                "hatch:   run it inside a checkout, name one with --repo, set HATCH_REPOS, or give a --workspace to clone into.");
            return 1;
        }

        // Read before anything is spawned. A wrong clock discovered at the end
        // of the night is a stop condition that never applied.
        DateTimeOffset? untilAt = null;
        if (until is { Length: > 0 })
        {
            if (AtClock(until, runtime.Clock.GetLocalNow()) is not { } hour)
            {
                runtime.Say.Complain("hatch: --until takes a wall-clock time, as HH:MM");
                return 1;
            }

            untilAt = hour;
        }

        // A stop file that is already there would end the loop before its first
        // increment, silently, and look exactly like a board with nothing on it.
        if (stopFile is { Length: > 0 } && Path.Exists(stopFile))
        {
            runtime.Say.Complain(
                $"hatch: {stopFile} already exists - that is the stop signal, so nothing would run. Remove it, or name another path.");
            return 1;
        }

        // One lock per checkout this runner serves - a second loop naming any
        // one of them is refused, by the first loop still holding it. And, where
        // a workspace is set, one more on the workspace directory itself, taken
        // here rather than left implicit in the per-checkout locks below: a
        // clone that appears at three in the morning has no lock of its own yet,
        // so what stops two loops from cloning into the same fresh path at once
        // is the workspace's own lock, held for the whole night.
        var locks = new List<LoopLock>();
        if (runtime.Settings.Workspace is { } lockedWorkspace)
        {
            var held = LoopLock.Take(lockedWorkspace, runtime.TempDirectory, out var refusal);
            if (held is null)
            {
                runtime.Say.Complain(refusal);
                return 1;
            }

            locks.Add(held);
        }

        foreach (var checkout in runtime.Checkouts)
        {
            var held = LoopLock.Take(checkout.Path, runtime.TempDirectory, out var refusal);
            if (held is null)
            {
                foreach (var taken in locks) taken.Dispose();
                runtime.Say.Complain(refusal);
                return 1;
            }

            locks.Add(held);
        }

        try
        {
            // Named for the new root only once the locks say this loop
            // actually gets to run - so a lock refusal above never touched the
            // wire - and inside this try, so a failure here still releases
            // them in the finally below.
            if (repoOverridden)
            {
                var lookupBoard = runtime.NewBoard(Checkout.Where(Checkout.Host(), runtime.Root));
                var runnerName = await Checkout.RunnerAsync(
                    runtime.Settings.Runner, Checkout.Host(), runtime.Root, lookupBoard, ct, runtime.RunnersPath);

                runtime = runtime with
                {
                    RunnerName = runnerName,
                    Where = Checkout.Where(Checkout.Host(), runtime.Root),
                    Board = runtime.NewBoard(runnerName),
                };
            }

            // What the incarnation before a restart handed over, if this is one.
            var carried = NightState.Read(runtime.NightStatePath);

            var tally = new Tally(runtime.Clock, carried)
            {
                MaxRuns = maxRuns,
                MaxSpend = maxSpend,
                Until = until,
                // Carried rather than recomputed, and it is the one bound that
                // would otherwise be wrong: `--until 23:59` typed at 23:58 and
                // restarted at 00:01 re-reads as 23:59 tomorrow, and adds a day
                // to the night.
                UntilAt = carried?.UntilAt ?? untilAt,
                StopFile = stopFile,
                Controls = runtime.Controls,
            };

            var restart = Restarts.Armed(runtime, noRestart, once, restartAfter);
            var restarting = false;
            var refused = false;

            try
            {
                restarting = await LoopAsync(under, quiet, mine, once, interval, tally, restart, ct);

                // A restart that could not hand the night's totals over would be
                // a fresh night: the budget back to nothing, the streak cleared,
                // the hour re-read. Better the version that is running carries
                // on to its own end than a new one starts with no bounds on it.
                if (restarting && !tally.ToState().Write(runtime.NightStatePath))
                {
                    runtime.Say.Complain(
                        $"hatch: could not write the night's totals to {runtime.NightStatePath} - not restarting, since the next one would start the budget over");
                    tally.StopWhy = "the night's totals could not be handed forward";
                    restarting = false;
                }
            }
            catch (OperationCanceledException)
            {
                // A signal caught while an increment was in flight. The claim has
                // already gone back through the pass's own way out; what is left
                // is to say why the night ended, which is the whole reason the
                // tally is printed from here and not from the loop.
                tally.StopWhy ??= "interrupted";
            }
            catch (HatchException e)
            {
                // The one heartbeat answer that is not weather: this name is
                // already the live runner somewhere else. Nothing was claimed,
                // and this is not an ordinary end of night - it is exit 1, so a
                // supervisor watching the exit code notices.
                runtime.Say.Complain(e.Message);
                refused = true;
            }
            finally
            {
                // A restart is the middle of a night and not the end of one: the
                // tally goes into the state file for whoever comes back, and is
                // printed once, by whichever incarnation is the last.
                if (!restarting)
                {
                    NightState.Forget(runtime.NightStatePath);
                    tally.Print(runtime.Say);
                }
            }

            return refused ? 1 : restarting ? RestartExitCode : 0;
        }
        finally
        {
            foreach (var held in locks) held.Dispose();
        }
    }

    /// <summary>
    /// Whether this loop may come back as a newer version of itself, when it
    /// should give up waiting for a reason to, and what its own source looked
    /// like when it started.
    /// </summary>
    /// <remarks>
    /// The baseline is taken once, at startup, and deliberately not carried
    /// across a restart. That is what makes a failed rebuild safe: an
    /// incarnation running the old code with the new source already on disk has
    /// the new source as <em>its</em> baseline, and will not ask again for the
    /// same change.
    /// </remarks>
    private sealed record Restarts(bool On, TimeSpan? After, SelfPrint Baseline, DateTimeOffset Born)
    {
        public static Restarts Armed(Runtime runtime, bool noRestart, bool once, int afterMinutes)
        {
            // Three ways of being off, and the third is not a flag: no state
            // path means nobody is supervising this process, so exiting 75 would
            // simply end the night. A runner started by hand is today's loop.
            var on = !noRestart && !once && runtime.NightStatePath is { Length: > 0 };

            return new Restarts(
                On: on,
                After: afterMinutes > 0 ? TimeSpan.FromMinutes(afterMinutes) : null,
                Baseline: on ? runtime.Self().Take() : SelfPrint.Nothing,
                Born: runtime.Clock.GetUtcNow());
        }

        /// <summary>Whether this incarnation - not the night - has been up long enough.</summary>
        public bool TooOld(DateTimeOffset now) => On && After is { } age && now - Born >= age;
    }

    /// <summary>Answers whether the loop is asking to come back as a newer version of itself.</summary>
    private async Task<bool> LoopAsync(
        string? under, bool quiet, bool mine, bool once, int interval, Tally tally, Restarts restart,
        CancellationToken ct)
    {
        var idle = new SaidOnce();
        var busy = new SaidOnce();
        var paused = new SaidOnce();
        var exhausted = new SaidOnce();
        var poll = new Poll();

        // When the loop's own checkout was last made current while waiting out
        // a usage limit - never, at first, so the first pass through the wait
        // checks at once rather than waiting a further ten minutes for news
        // that arrived just before the limit hit.
        var checkedSelfAt = DateTimeOffset.MinValue;

        // Who this beat says the runner works for, held here rather than read
        // fresh every time the readout is updated: the board writes it on every
        // beat, but a beat that answered with nothing (an origin gone quiet) is
        // not a beat that said "nobody" - the last name heard stands until a
        // fresh one replaces it.
        string? forName = null;

        if (!runtime.Sessions.CanSpawn(out var missing))
        {
            runtime.Say.Complain(missing);
            tally.StopWhy = "there is no claude CLI to spawn";
            return false;
        }

        // What this loop last did out loud, carried to the board by the next
        // heartbeat so the Runners page reads like the terminal does. A holder
        // rather than a return value, because the sentences worth having are
        // the ones a pass prints on its way past something.
        var line = new Chatter { Line = "reading the board" };

        // The usage probe's own clock, independent of the session stream's -
        // null until the first probe, and read fresh so a loop that has been up
        // a while without a session still asks between beats. See UsageReport
        // and the decisions on HA-173.
        DateTimeOffset? probedAt = null;

        // Set once an increment actually ran, off Pass.Worked, and read at the
        // top of the very next iteration: a session's own rate_limit_event has
        // just replaced the readout's reading with whatever the stream
        // carried, and without a fresh probe here the next heartbeat would
        // send that instead of the fuller account the CLI's own login reports.
        var workedSinceProbe = false;

        while (!ct.IsCancellationRequested)
        {
            if (runtime.Sessions is IUsageProbe probe)
            {
                var probeNow = runtime.Clock.GetUtcNow();
                var due = probedAt is not { } last || probeNow - last >= ProbeMaxAge || workedSinceProbe;
                workedSinceProbe = false;

                if (due)
                {
                    probedAt = probeNow;
                    var probed = await probe.ProbeAsync(runtime.TempDirectory, ct);
                    if (probed is not null && UsageReport.Parse(probed) is { } windows)
                        runtime.Readout.SetUsage(windows, probeNow);
                }
            }

            // Once per iteration, at the top - which is the one moment in a
            // pass when no claim is held. That is what makes "picked up between
            // increments, after the one in flight has finished" true by
            // construction rather than by a check somewhere.
            var told = await BeatAsync(line, under, mine, tally, once, ct);

            if (told is { State: RunnerStates.Stopping })
            {
                // Not an interrupt and not a failure: the night ends here, with
                // the ticket that was in flight finished and pushed.
                tally.StopWhy = "the board asked this runner to stop";
                return false;
            }

            // Before the stop conditions are asked, so a cap lowered from a
            // page is a cap this pass is judged against rather than the next
            // one.
            if (told is not null) under = Fold(told, tally, under);
            if (told?.For is { Length: > 0 } named) forName = named;

            runtime.Readout.SetRunner(new RunnerSnapshot(
                runtime.RunnerName, forName, tally.Runs, runtime.Clock.GetUtcNow() - tally.Started, tally.Spent,
                Bound(tally)));

            if (tally.ShouldStop()) return false;

            // The backstop, read here because here is where no claim is held. An
            // idle loop never resets - by design, since a fetch every interval
            // all night against a remote with nothing to say is a fetch for
            // nothing - so it never sees a change, and age is what covers it.
            // The fresh process re-reads scripts/.env, re-resolves the runner,
            // and the first pass with work in it resets and sees the rest.
            var now = runtime.Clock.GetUtcNow();
            if (restart.TooOld(now))
            {
                runtime.Say.Line("");
                runtime.Say.Line(
                    $"hatch: this loop has been running {Format.Duration((long)(now - restart.Born).TotalSeconds)} - restarting to pick up anything that landed");
                runtime.Say.Line($"hatch:   {tally.SoFar()}");
                return true;
            }

            if (told is { State: RunnerStates.Paused })
            {
                // Still heartbeating, so the row does not drift from idle to
                // gone while it is deliberately doing nothing - a paused loop
                // that read as dead would be a page that could not tell the two
                // apart.
                idle.Clear();
                busy.Clear();
                line.Line = "paused - waiting to be set running again";
                runtime.Readout.SetIdle(line.Line, now.AddSeconds(interval));
                await SayQuietlyAsync(paused, now, interval, once,
                    digest: "paused",
                    still: "hatch: still paused",
                    inFull: () =>
                    {
                        runtime.Say.Line("hatch: the board has this runner paused - nothing will be picked up until it is set running");
                        return Task.CompletedTask;
                    });

                if (!await NapAsync(interval, tally, ct)) return false;
                continue;
            }

            paused.Clear();

            if (tally.ExhaustedUntil is { } until && now < until)
            {
                // Still heartbeating, the same reason a paused loop does - and
                // this runner's own row is the one place that says why, on the
                // terms Restart HA-107 gave it: ExhaustedUntil, read back off
                // its own heartbeat by nobody but the page.
                idle.Clear();
                busy.Clear();
                var waiting = $"out of Claude usage until {UsageLimit.Clock(until)}";
                line.Line = waiting;
                await SayQuietlyAsync(exhausted, now, interval, once,
                    digest: "exhausted",
                    still: "hatch: still out of Claude usage",
                    inFull: () =>
                    {
                        runtime.Say.Line($"hatch: this runner is {waiting} - taking no tickets until then");
                        return Task.CompletedTask;
                    });

                // The one thing a paused wait does not have to do: stay
                // current. Nothing here is ever dispatched against a stale
                // trunk, so a claimed pass fetches on every ticket - but an idle
                // wait that fetched every interval all night would be exactly
                // the fetch-for-nothing the workspace's own remarks already
                // rule out, so this is timed on its own, independently of
                // --interval, at the same ten minutes a quiet reminder waits.
                if (now - checkedSelfAt >= StillNothing)
                {
                    checkedSelfAt = now;
                    runtime.Workspace(runtime.Root, runtime.Settings.BaseBranch).Prepare();

                    if (Changed(restart) is { Count: > 0 } changed)
                    {
                        SayChanged(changed, tally);
                        return true;
                    }
                }

                if (!await NapAsync(interval, tally, ct)) return false;
                continue;
            }

            if (exhausted.Running)
            {
                runtime.Say.Line("");
                runtime.Say.Line("hatch: back within Claude usage - taking tickets again");
            }

            exhausted.Clear();
            tally.ClearExhausted();

            // Between the heartbeat and the pass, and on every iteration - an
            // idle loop and a busy one alike - so a pull request that stopped
            // merging while a session was running is conflict work as soon as
            // the next pass reads the board. It is timed by the interval and not
            // by the iteration: a loop that finishes an increment and goes
            // straight on asks git nothing more than one that waited.
            await poll.RunAsync(runtime, interval, ct);

            var pass = await PassAsync(under, quiet, mine, tally, idle, busy, interval, once, restart, line, ct);
            if (pass == Pass.Worked) workedSinceProbe = true;
            if (pass == Pass.Fatal) return false;
            if (pass == Pass.Restarting) return true;

            // `--once` is the loop's own dry run against a board that is not a
            // fixture: one pass, whatever it found, and out. A hop is not that
            // pass - it spawns nothing and costs nothing, so `--once` waits for
            // the pass that actually is one.
            if (once && pass is not Pass.Hopped)
            {
                tally.StopWhy = "--once, and the pass is done";
                return false;
            }

            // An increment that ran is followed by the next one immediately. The
            // interval is what to do when there was nothing to do. A hop reads
            // the board again straight away, the same as a cleared conflict.
            if (pass is Pass.Worked or Pass.Asked or Pass.Cleared or Pass.Hopped) continue;
            if (!await NapAsync(interval, tally, ct)) return false;
        }

        if (ct.IsCancellationRequested) tally.StopWhy ??= "interrupted";
        return false;
    }

    private enum Pass
    {
        Worked,
        Waited,
        Fatal,

        /// <summary>
        /// The loop's own source changed under it. Nothing was spawned, and the
        /// claim goes back on the way out - a restart holds no ticket.
        /// </summary>
        Restarting,

        /// <summary>
        /// A conflict dispatch whose conflict had gone by the time the claim was
        /// held and the branch checked again. Nothing was spawned and no
        /// increment counted, and the board has taken the verdict that says so -
        /// so the next pass is asked for at once, and does not find it again.
        /// </summary>
        Cleared,

        /// <summary>
        /// The ticket needed a person to say which branch, and the question is
        /// on it. Nothing was spawned; the next pass reads a board that no
        /// longer offers it, so there is nothing to wait for.
        /// </summary>
        Asked,

        /// <summary>
        /// An express issue was carried across the column it stood in - see
        /// docs/hatch.md, "The hop". No claim, no reset, no session, and no
        /// tally: <c>--max-runs</c> and <c>--once</c> count sessions, and a hop
        /// is not one - so the next pass is asked for at once, the same as
        /// <see cref="Cleared"/>.
        /// </summary>
        Hopped,
    }

    /// <summary>
    /// One pass of the board: everything it folds past and why, and then the one
    /// thing it does about the rest.
    /// </summary>
    /// <remarks>
    /// The claim is taken before the workspace is reset and let go of in a
    /// <c>finally</c>, so every way out of a pass - a finished increment, a
    /// session that would not start, a tree that would not reset, an interrupt -
    /// gives the ticket back.
    /// </remarks>
    /// <summary>
    /// Says this runner is here, and answers with what the board would like it
    /// to do - or null, which is every runner's ordinary state against a Hatch
    /// that has nothing to say and against one too old to have the route at
    /// all.
    /// </summary>
    /// <remarks>
    /// The four bounds are sent on every beat and read on none. The server
    /// writes them when it first sees this name and never again, so what
    /// somebody set at midnight survives the loop's own half-hourly restart -
    /// and sending them each time is what makes the row show the truth from its
    /// first appearance without the runner having to know whether it is new.
    /// </remarks>
    private async Task<RunnerInstructionDto?> BeatAsync(
        Chatter line, string? under, bool mine, Tally tally, bool once, CancellationToken ct)
    {
        var usage = runtime.Readout.Snapshot();

        var beat = await runtime.Runners().BeatAsync(
            new RunnerHeartbeatRequest(
                Kind: once ? RunnerKinds.Once : RunnerKinds.Loop,
                Line: line.Line,
                Under: under,
                MaxRuns: tally.MaxRuns,
                MaxSpend: tally.MaxSpend,
                UntilAt: tally.UntilAt,
                Remotes: runtime.Checkouts.Where(c => c.Remote is not null).Select(c => c.Remote!).ToList(),
                Clones: runtime.Settings.Workspace is not null,
                Mine: mine,
                Where: runtime.Where,
                Exhausted: tally.ExhaustedUntil is not null,
                ExhaustedUntil: tally.ExhaustedUntil,
                // Absent before this loop's first rate_limit_event - a restart
                // mid-night sends nothing rather than blanking what the server
                // already holds for this account. See RunnerHeartbeatRequest.Usage.
                Usage: usage.UsageReadAt is null ? null : usage.UsageWindows
                    .Select(w => new RunnerUsageWindowDto(
                        w.Window, w.Label, (int)Math.Round(w.Utilization * 100), w.ResetsAt))
                    .ToList(),
                UsageReadAt: usage.UsageReadAt),
            ct);

        // The one heartbeat answer that ends a run: this name is already the
        // live runner somewhere else, so nothing here is claimed.
        if (beat.Refusal is { Length: > 0 } refusal) throw new HatchException($"hatch: {refusal}");

        // `--once` says hello and reads nothing back. There is no second pass
        // to apply an instruction to, and a single increment that acknowledged
        // a `stopping` it could not act on would be a lie on the row.
        return once ? null : beat.Instruction;
    }

    /// <summary>
    /// The board's bounds, folded into this loop's - and its scope, which is
    /// the one that comes back rather than being set. Answers the epic to stay
    /// inside, or null for the whole board.
    /// </summary>
    /// <remarks>
    /// Wholesale, including the absences: the row was seeded from the flags
    /// this process started with, so a field that is empty on it is a field
    /// somebody cleared. Reading an absence as "leave the flag alone" would
    /// make a cap impossible to take off from the page that set it.
    /// </remarks>
    private string? Fold(RunnerInstructionDto told, Tally tally, string? under)
    {
        var scope = told.Under is { Length: > 0 } named ? named : null;

        // Said out loud, because the sentence that ends a night names a flag -
        // "--max-runs 3 reached" - and a terminal that had never seen anybody
        // type one would be a run that stopped for no reason it could show.
        // Nothing is said on the ordinary beat, where what comes back is what
        // this process started with.
        var moved = new List<string>();
        if (scope != under) moved.Add($"--under {scope ?? "off"}");
        if (told.MaxRuns != tally.MaxRuns) moved.Add($"--max-runs {told.MaxRuns?.ToString(CultureInfo.InvariantCulture) ?? "off"}");
        if (told.MaxSpend != tally.MaxSpend) moved.Add($"--max-spend {told.MaxSpend?.ToString(CultureInfo.InvariantCulture) ?? "off"}");
        if (told.UntilAt != tally.UntilAt) moved.Add($"--until {told.UntilAt?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) ?? "off"}");

        if (moved.Count > 0) runtime.Say.Line($"hatch: the board set {string.Join(", ", moved)}");

        tally.MaxRuns = told.MaxRuns;
        tally.MaxSpend = told.MaxSpend;
        tally.UntilAt = told.UntilAt;

        // The hour as somebody would have typed it, because it is printed in
        // the sentence that ends the night and `--until 06:00` is what that
        // sentence has always named.
        tally.Until = told.UntilAt?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        return scope;
    }

    /// <summary>Whatever the night will stop at, for the readout's runner row - null when nothing is set.</summary>
    private static string? Bound(Tally tally)
    {
        var parts = new List<string>();
        if (tally.MaxRuns is { } runs) parts.Add($"--max-runs {runs.ToString(CultureInfo.InvariantCulture)}");
        if (tally.MaxSpend is { } spend) parts.Add($"--max-spend {spend.ToString(CultureInfo.InvariantCulture)}");
        if (tally.UntilAt is not null) parts.Add($"--until {tally.Until}");

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private async Task<Pass> PassAsync(
        string? under, bool quiet, bool mine, Tally tally,
        SaidOnce idle, SaidOnce busy, int interval, bool once, Restarts restart, Chatter line, CancellationToken ct)
    {
        // Hoisted so the catch below - which has to cover picking, claiming,
        // preparing the tree, the session, judging it and tidying up, in one
        // net - can say which ticket failed and put its tree back, however far
        // this pass got before it did.
        Claim? claim = null;
        WorkDto? work = null;
        Checkouts.Choice? chosen = null;
        Lifecycle? lifecycle = null;
        Increment? increment = null;
        var entered = false;

        // What the release in the `finally` below says about how this pass
        // went - set on the increment's own report when one ran to a verdict,
        // or to dropped when the runner itself fell over. Left null on every
        // other way out: a claim that was never spent on a finished increment
        // has nothing to say about the ticket.
        string? releaseOutcome = null;

        try
        {
            var picked = await runtime.Picker().PickAsync(under, runtime.OffsetMinutes, ct, runtime.Heartbeat, mine);
            var now = runtime.Clock.GetUtcNow();

            // A clone this walk could not make - recorded exactly as a failed spawn
            // would be, so three of them in a row end the night the same way three
            // broken increments do. Declared on the next read either way: the
            // checkouts a clone attempt grew survive past a candidate it gave up on.
            foreach (var failed in picked.CloneFailures ?? []) tally.Record(failed);
            if (picked.Checkouts is { } grown) runtime = runtime with { Checkouts = grown };

            switch (picked.Outcome)
            {
                case Pick.Idle:
                    busy.Clear();
                    line.Line = "nothing on the board is an agent's to move";
                    runtime.Readout.SetIdle(line.Line, now.AddSeconds(interval));
                    await SayQuietlyAsync(idle, now, interval, once,
                        digest: string.Join('\n', Digest.Of(picked.Queue)),
                        still: "hatch: still nothing an agent may move",
                        inFull: () => runtime.Idle(mine).ReportAsync(under, picked.Queue, runtime.OffsetMinutes, ct));
                    return Pass.Waited;

                case Pick.Busy:
                    idle.Clear();
                    line.Line = "every issue an agent could take is being worked elsewhere";
                    runtime.Readout.SetIdle(line.Line, now.AddSeconds(interval));
                    await SayQuietlyAsync(busy, now, interval, once,
                        digest: string.Join('\n', picked.Busy),
                        still: "hatch: every issue an agent could take is still being worked elsewhere",
                        inFull: () =>
                        {
                            runtime.Say.Lines(Picker.BusyReport(under, picked.Busy));
                            return Task.CompletedTask;
                        });
                    return Pass.Waited;

                case Pick.Unreadable:
                    // The client already said what went wrong, in a sentence. This
                    // says what is going to happen about it.
                    line.Line = "the board did not answer";
                    runtime.Say.Complain($"hatch: the board did not answer - asking again in {interval}s");
                    return Pass.Waited;

                case Pick.Refused:
                    // A 400 is the dispatcher saying this will never succeed as
                    // asked - today, a --mine pass whose key belongs to nobody.
                    // The same path "the workspace could not be reset" takes:
                    // nothing was spawned, so this ends the night without going
                    // through the three-failures tally.
                    line.Line = "the board refused this pass";
                    tally.StopWhy = picked.Refusal;
                    return Pass.Fatal;

                case Pick.Hopped:
                    idle.Clear();
                    busy.Clear();
                    var hop = picked.Hopped!;
                    var hopLine = $"{hop.Key}  {hop.From} -> {hop.To}  {hop.Reason}";
                    line.Line = hopLine;
                    runtime.Say.Line($"hatch: {hopLine}");
                    return Pass.Hopped;
            }

            idle.Clear();
            busy.Clear();

            claim = picked.Claim!;
            work = picked.Work!;
            chosen = picked.Chosen!;

            // An increment is about to run, so what the pass walked past on the
            // way to it is context rather than noise.
            if (Digest.Of(picked.Queue) is { Count: > 0 } digest)
            {
                runtime.Say.Line($"hatch:   folded past {picked.Queue.Count(q => q.Blocked is not null)} issue(s) on the way here:");
                runtime.Say.Lines(digest);
            }

            foreach (var held in picked.Busy)
                runtime.Say.Line($"hatch:   another runner had{held}");

            // Here rather than at the top of the pass, and the difference is
            // only ever visible on an idle board: a reset before the board is
            // read is a fetch every interval all night, against a remote that
            // has nothing to say to a loop with nothing to run. The guarantee is
            // the same either way, because it is about the spawn and not about
            // the pass.
            foreach (var (path, baseBranch) in chosen.Resets)
            {
                switch (runtime.Workspace(path, baseBranch).Prepare())
                {
                    case Reset.Never:
                        line.Line = "the workspace could not be reset";
                        // The one condition that ends a night without an
                        // increment having failed: a tree that cannot be made
                        // current is a tree every ticket would be built wrong
                        // on, and the loop has no way to make it right.
                        tally.StopWhy = $"the workspace could not be reset ({path})";
                        return Pass.Fatal;

                    case Reset.Later:
                        // Nothing was spawned, nothing was spent, and the tree
                        // is where it was.
                        line.Line = "the workspace is not ready";
                        runtime.Say.Complain($"hatch: the workspace is not ready ({path}) - trying again in {interval}s");
                        return Pass.Waited;
                }
            }

            // The one place the change trigger can fire, because the new source
            // only exists on disk once the reset has pulled it. Inside the `try`
            // on purpose: the outer catch and the loop below both give the ticket
            // back on every other way out of a pass, so a restart holds no claim
            // and the next incarnation finds the ticket free.
            if (Changed(restart) is { Count: > 0 } changed)
            {
                SayChanged(changed, tally);
                return Pass.Restarting;
            }

            // After the change check, and that order is the point: the check
            // reads the loop's own source off the disk, and an issue's branch
            // that edits it would make the loop restart the moment it was
            // checked out. On the trunk, the check sees what the trunk has.
            lifecycle = new Lifecycle(runtime);

            // A conflict is asked about again before anything is spawned, against
            // the refs the reset just fetched: the board's verdict was read
            // before the claim, and the trunk or the branch may have moved since.
            // The tree has not been moved onto the branch yet, so nothing needs
            // undoing if there is nothing to do.
            Rechecked? found = null;
            if (work.Kind == WorkKinds.Conflicts)
            {
                found = await lifecycle.RecheckAsync(work, chosen, ct);

                if (found.Conflicts.Count == 0)
                {
                    if (found.Unknown)
                    {
                        // The board's verdict is not contradicted by a runner
                        // that cannot read one, and a session should not be
                        // spent on a merge that may not exist.
                        line.Line = $"{work.Issue.Key} could not be checked against the trunk";
                        runtime.Say.Complain(
                            $"hatch: {work.Issue.Key} - its branch could not be checked against the trunk, so nothing was spawned - trying again in {interval}s");
                        return Pass.Waited;
                    }

                    var was = found.Trunk ?? Conflicts.Trunk(work.Issue);
                    line.Line = $"{work.Issue.Key} no longer conflicts with {was}";
                    runtime.Say.Line($"hatch: {work.Issue.Key} no longer conflicts with {was} - nothing to do");

                    // Straight on only if the board took the verdicts. One that was
                    // refused still calls the issue conflicted, and a pass that
                    // went straight back would find it again in a tight loop.
                    return found.Reported ? Pass.Cleared : Pass.Waited;
                }
            }

            // A failing build is read again for the same reason, and by the same
            // call `hatch work` makes: the tip may have moved and the build may
            // have gone green since the board was read. Nothing is spawned and no
            // increment is counted for a build there is nothing to fix in.
            BuildFound? built = null;
            if (work.Kind == WorkKinds.Build)
            {
                built = await lifecycle.RecheckBuildAsync(work, chosen, ct);

                if (built.StillFailing.Count == 0)
                {
                    if (built.Unknown)
                    {
                        line.Line = $"{work.Issue.Key} could not have its build read";
                        runtime.Say.Complain(
                            $"hatch: {work.Issue.Key} - its build could not be read, so nothing was spawned - trying again in {interval}s");
                        return Pass.Waited;
                    }

                    line.Line = $"{work.Issue.Key} {built.Nothing()}";
                    runtime.Say.Line($"hatch: {work.Issue.Key} {built.Nothing()} - nothing to do");
                    return built.Reported ? Pass.Cleared : Pass.Waited;
                }
            }

            var entering = await lifecycle.EnterAsync(work, chosen, ct);
            if (entering.Asked)
            {
                // A checkout that did have one branch is on it: back to the
                // trunk, so the restart between increments builds from there.
                await lifecycle.LeaveAsync(work, chosen, ownsTicket: false, ct);
                line.Line = $"{work.Issue.Key} needs to be told which branch to use";
                return Pass.Asked;
            }

            // From here on the tree is on the issue's branch, and not the trunk
            // - so a failure past this point has to be told to put it back.
            entered = true;

            runtime.Say.Line("");

            // The playbook's, and no flag reaches this: `work --model` is one
            // operator's opinion about one increment, and a loop that carried it
            // across a night would be applying it to tickets nobody looked at.
            // What the loop does carry is the ticket's own model and effort,
            // which arrive folded into the playbook already.
            increment = runtime.Increment();
            var report = await increment.RunAsync(
                work, chosen.Root, work.Playbook?.Model ?? "", work.Playbook?.Effort ?? "",
                quiet, claim, ct, chosen.AddDirs, chosen.Repositories, entering.Entries,
                found is null ? null : new ConflictRun(found, judge => lifecycle.JudgeAsync(work, chosen, judge)),
                built is null ? null : new BuildRun(built, judge => lifecycle.JudgeBuildAsync(work.Issue.Key, built, chosen, judge)),
                runnerName: runtime.RunnerName, incrementNumber: tally.Runs + 1);

            tally.Record(report);
            releaseOutcome = report.ReleaseOutcome;

            try
            {
                // Before the lease is let go, because the ticket is still this
                // runner's to write on: what the session left is said there, and
                // the pull request's branch is brought up to date. A lease that
                // went gets the trees back on the trunk and nothing written.
                var limit = report.UsageLimitResetAt is { } resetAt
                    ? new UsageLimitInfo(resetAt, report.UsageLimitResetKnown, report.SessionId)
                    : null;
                var preempted = report.Preempted
                    ? new PreemptionInfo(report.PreemptedKey!, report.PreemptedTitle!, report.SessionId)
                    : null;
                await lifecycle.LeaveAsync(work, chosen, ownsTicket: !report.LostLease, ct, limit, preempted);
            }
            finally
            {
                // Every opening banner has a closing one - in a finally, so a
                // Ctrl-C that lands while the tree is being left still gets one
                // rather than the loop unwinding past it.
                runtime.Say.Lines(Banner.Closing(report, Readout.OneLine(runtime.Readout.Snapshot().UsageWindows)));
            }

            // A conflict that was resolved did not move the ticket, and did not
            // fail to: it stays in review, which is where it belongs.
            var said = report.Moved ? $"{report.Key} moved, {report.Outcome}"
                : report.Resolved || report.FixPushed ? $"{report.Key} {report.Outcome}"
                : $"{report.Key} did not move - {report.Outcome}";

            runtime.Say.Line("");
            runtime.Say.Line($"hatch: {said}  ({tally.Runs} increment(s), ${Format.Money(tally.Spent)})");

            // What the next heartbeat carries. In-increment chatter rides the
            // claim, so what this row wants is what happened to the last one.
            line.Line = said;

            return Pass.Worked;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // One failed increment, not the end of the night - whatever stage
            // this pass was in when it fell over: picking, claiming, preparing
            // the tree, the session itself, judging it, or tidying up after.
            var key = e is PickFailedException pf ? pf.Key : work?.Issue.Key;
            var cause = e is PickFailedException { InnerException: { } inner } ? inner : e;
            var why = $"{cause.GetType().Name}: {cause.Message}";

            runtime.Say.Complain(key is { Length: > 0 }
                ? $"hatch: {key} - the runner failed, and is letting it go - {why}"
                : $"hatch: the runner failed, before a ticket was settled on - {why}");

            // Back on the trunk only if it was ever moved off it - a failure
            // during picking or preparing the tree never left the trunk.
            if (entered) await lifecycle!.LeaveAsync(work!, chosen!, ownsTicket: false, CancellationToken.None);

            if (key is { Length: > 0 })
                await LetGo.CommentAsync(runtime.Board, runtime.Say, key, why, increment?.SessionId, CancellationToken.None);

            // The runner fell over rather than the increment running to a
            // verdict, so the claim (where one was ever held) goes back the
            // same way any other stall does.
            releaseOutcome = ClaimOutcomes.Dropped;

            tally.Record(new IncrementReport
            {
                Key = key ?? "?",
                From = work?.FromStatus.Name ?? "?",
                To = work?.ToStatus?.Name ?? "?",
                Ended = work?.FromStatus.Name ?? "?",
                ExitCode = 1,
                Flag = why,
            });

            return Pass.Worked;
        }
        finally
        {
            // The lease covers the bookkeeping too, and this is the door every
            // path out of a pass goes through.
            if (claim is not null) await claim.ReleaseAsync(releaseOutcome);
        }
    }

    /// <summary>
    /// What of the loop's own source is not what it was when this incarnation
    /// started. Empty when nothing changed, and when restarts are off - the
    /// print is not even read then.
    /// </summary>
    private IReadOnlyList<string> Changed(Restarts restart) =>
        restart.On ? runtime.Self().Take().ChangedFrom(restart.Baseline) : [];

    /// <summary>
    /// Which trigger fired and what it saw - because a terminal at three in the
    /// morning that goes quiet and comes back reads as a crash unless something
    /// says otherwise.
    /// </summary>
    private void SayChanged(IReadOnlyList<string> changed, Tally tally)
    {
        var named = string.Join(", ", changed.Take(NamedPaths));
        if (changed.Count > NamedPaths) named += $", and {changed.Count - NamedPaths} more";

        runtime.Say.Line("");
        runtime.Say.Line("hatch: the loop's own source changed on the trunk - restarting as the new version");
        runtime.Say.Line($"hatch:   {named}");
        runtime.Say.Line($"hatch:   {tally.SoFar()}");
    }

    private async Task SayQuietlyAsync(
        SaidOnce paced, DateTimeOffset now, int interval, bool once,
        string digest, string still, Func<Task> inFull)
    {
        var first = !paced.Running;

        if (paced.InFull(digest, now))
        {
            await inFull();

            // What the loop is going to do about it, said once. A reprint later
            // is a report about the board having changed, not a fresh
            // explanation of the interval.
            if (first && !once) runtime.Say.Line($"hatch: waiting, and asking again every {interval}s");
        }
        else if (paced.Again(now, StillNothing))
        {
            runtime.Say.Line($"{still}, {Format.Duration((long)paced.For(now).TotalSeconds)} now");
        }
    }

    /// <summary>
    /// Waiting, in slices, so that a stop file dropped during a wait is noticed
    /// then rather than an interval later, and an <c>--until</c> at three in the
    /// morning lands at three in the morning however long the interval is.
    /// </summary>
    private async Task<bool> NapAsync(int seconds, Tally tally, CancellationToken ct)
    {
        var left = seconds;
        while (left > 0)
        {
            var slice = Math.Min(5, left);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(slice), runtime.Clock, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            left -= slice;
            if (tally.ShouldStop()) return false;
        }

        return true;
    }

    /// <summary>
    /// <c>HH:MM</c> today, or tomorrow if that hour has already gone by -
    /// somebody who says <c>--until 06:00</c> at eleven at night means the
    /// morning, and a loop that read it as "seventeen hours ago" would stop
    /// before it started.
    /// </summary>
    internal static DateTimeOffset? AtClock(string hhmm, DateTimeOffset now)
    {
        if (!TimeOnly.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hour))
            return null;

        var at = new DateTimeOffset(now.Date.Add(hour.ToTimeSpan()), now.Offset);
        return at > now ? at : at.AddDays(1);
    }
}

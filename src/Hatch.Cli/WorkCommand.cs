namespace Hatch.Cli;

/// <summary>
/// One increment, on the next thing due or on a named ticket.
/// </summary>
/// <remarks>
/// The claim is taken here and let go of in a <c>finally</c>, so that every way
/// out of this command goes through the same door: a finished session, a failed
/// one, one that would not start, and a Ctrl-C - which arrives as a cancelled
/// token and unwinds through the same block.
/// </remarks>
public sealed class WorkCommand(Runtime runtime)
{
    public static readonly string[] WorkUsage =
    [
        "usage: hatch work [<issue key>] [--mine] [--under <epic key>] [-i] [--quiet]",
        "                  [--model <model>] [--effort <effort>] [--dry-run] [--no-keys]",
        "                  [--repo <path>]... [--workspace <dir>]",
        "",
        "  One increment: claim a ticket, spawn one headless claude session with the",
        "  prompt, model and effort its column and type call for, and exit when that",
        "  session ends. Run inside the checkout the board is about, or name one with",
        "  --repo or HATCH_REPOS.",
        "",
        "  With no key it takes the next actionable issue on the board. A key names",
        "  one outright; --under names the epic to look under. Not both - one says",
        "  which ticket, the other says where to look for one.",
        "",
        "  --mine             the next ticket `hatch do-my-work` would take - only the",
        "                     caller's own. Refused together with a key: one names the",
        "                     ticket, the other narrows which ticket is picked",
        "  -i                 a session you sit in, rather than a headless one",
        "  --quiet            say nothing until the increment is finished",
        "  --model            beat the playbook, for this run only",
        "  --effort           ...and likewise",
        "  --dry-run          print the prompt and exit: claims nothing, spawns nothing,",
        "                     and clones nothing even where a workspace is configured",
        "  --no-keys          read no keys from this terminal - Ctrl-C and --stop-file still work",
        "  --repo <path>      also serve this checkout, repeatable - the whole list for this",
        "                     run, beside the standing checkout if there is one; HATCH_REPOS",
        "                     is not consulted when this is given",
        "  --workspace <dir>  clone every repository the board binds that this runner has",
        "                     no checkout of, into <dir> - HATCH_WORKSPACE is not consulted",
        "                     when this is given",
        "",
        "  Exits 0 when an increment ran, whatever the session itself exited with;",
        "  1 on a refusal; 2 when there is nothing to do - the board is idle, the",
        "  ticket is blocked, or another runner has it; 130 on an interrupt.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(runtime.Say, WorkUsage);

        string? key = null, under = null, model = null, effort = null;
        var dry = false;
        var attach = false;
        var quiet = false;
        var mine = false;
        var repoFlags = new List<string>();
        string? workspaceFlag = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mine": mine = true; break;
                case "--model" when i + 1 < args.Length: model = args[++i]; break;
                case "--effort" when i + 1 < args.Length: effort = args[++i]; break;
                case "--under" when i + 1 < args.Length: under = args[++i]; break;
                case "--dry-run": dry = true; break;
                case "--quiet": quiet = true; break;
                // Recognised so it is not refused as an unknown flag - Program.cs
                // already acted on it, before this command was ever constructed.
                case "--no-keys": break;
                case "-i" or "--interactive": attach = true; break;
                case "--repo" when i + 1 < args.Length: repoFlags.Add(args[++i]); break;
                case "--repo":
                    return Usage.Refuse(runtime.Say, "work --repo takes a path", WorkUsage);
                case "--workspace" when i + 1 < args.Length: workspaceFlag = args[++i]; break;
                case "--workspace":
                    return Usage.Refuse(runtime.Say, "work --workspace takes a directory", WorkUsage);
                case var flag when flag.StartsWith('-'):
                    return Usage.Refuse(runtime.Say, $"work does not take {flag}", WorkUsage);
                default: key = args[i]; break;
            }
        }

        if (repoFlags.Count > 0 || workspaceFlag is not null)
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
            var lookupBoard = runtime.NewBoard(Checkout.Where(Checkout.Host(), root));
            var runnerName = await Checkout.RunnerAsync(
                runtime.Settings.Runner, Checkout.Host(), root, lookupBoard, ct, runtime.RunnersPath);

            runtime = runtime with
            {
                Checkouts = repoCheckouts,
                Root = root,
                RunnerName = runnerName,
                Where = Checkout.Where(Checkout.Host(), root),
                Board = runtime.NewBoard(runnerName),
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

        // Two different asks: a bare key is "this ticket", --under is "whatever
        // is next below this one". Silently preferring either would be a run
        // spent on a ticket nobody named, so both together is a refusal.
        if (key is { Length: > 0 } && under is { Length: > 0 })
        {
            runtime.Say.Complain(
                "hatch: work takes a key or --under, not both - one names the ticket, the other names where to look for it");
            return 1;
        }

        // A key names the ticket; --mine narrows which ticket is picked when
        // none is named - somebody who names one has already chosen it, the
        // same refusal --under makes and for the same reason.
        if (key is { Length: > 0 } && mine)
        {
            runtime.Say.Complain(
                "hatch: work takes a key or --mine, not both - one names the ticket, the other narrows which ticket is picked");
            return 1;
        }

        return dry
            ? await DryRunAsync(key, under, mine, model, effort, ct)
            : await SpendAsync(key, under, mine, model, effort, attach, quiet, ct);
    }

    /// <summary>
    /// The prompt, and nothing else. Claims nothing, because it spawns nothing -
    /// and reads <c>work/next</c> rather than walking the claim, since a dry run
    /// that took a lease would be exactly the thing it is a dry run of.
    /// </summary>
    private async Task<int> DryRunAsync(
        string? key, string? under, bool mine, string? model, string? effort, CancellationToken ct)
    {
        var clones = runtime.Settings.Workspace is not null;

        WorkDto? work;
        try
        {
            work = key is { Length: > 0 }
                ? await runtime.Board.WorkAsync(runtime.Checkouts, key, null, ct, clones)
                : await runtime.Board.NextAsync(runtime.Checkouts, under, runtime.OffsetMinutes, ct, clones, mine);
        }
        catch (HatchException e)
        {
            runtime.Say.Complain(e.Message);
            return 1;
        }

        if (work is null)
        {
            await runtime.Idle(mine).ReportAsync(under, null, runtime.OffsetMinutes, ct);
            return 2;
        }

        if (Refuse(work)) return 2;

        if (work.Hop)
        {
            runtime.Say.Line($"# {work.Issue.Key} {work.FromStatus.Name} -> {work.ToStatus?.Name}");
            runtime.Say.Line($"# {Picker.HopWord(work.HopKind, work.HopUnder)}: carried across with no session");
            return 0;
        }

        var chosen = Checkouts.Choose(work.Repositories, runtime.Checkouts, runtime.Root, runtime.Settings.BaseBranch);
        if (chosen is null)
        {
            runtime.Say.Complain(Changed(work.Issue.Key));
            return 2;
        }

        model ??= work.Playbook?.Model ?? "";
        effort ??= work.Playbook?.Effort ?? "";

        runtime.Say.Line(
            work.Kind == WorkKinds.Conflicts ? $"# {work.Issue.Key} {work.FromStatus.Name}, {Conflicts.Words(work.Issue)}"
            : work.Kind == WorkKinds.Build ? $"# {work.Issue.Key} {work.FromStatus.Name}, {Builds.Words(work.Issue)}"
            : $"# {work.Issue.Key} {work.FromStatus.Name} -> {work.ToStatus?.Name}");
        runtime.Say.Line($"# model {model}, effort {effort}");
        if (Prompt.OverrideLine(work, model, effort) is { } chose) runtime.Say.Line($"# {chose}");

        // What the branch step would do, read from the refs as they stand - so
        // as of the last fetch, and nothing here fetches, checks out or merges.
        var plan = new Lifecycle(runtime).Plan(work, chosen);
        foreach (var entry in plan) runtime.Say.Line($"# would enter: {entry.Sentence()}");
        runtime.Say.Line("");
        runtime.Say.Lines(Prompt.Compose(work, chosen.Repositories, plan).Split('\n'));
        return 0;
    }

    /// <summary>
    /// The project's repositories no longer match what the queue said a moment
    /// before - the same "changed under us" a picked-up dispatch walks past,
    /// said here because a named or dry-run read has nowhere to walk on to.
    /// </summary>
    private static string Changed(string key) =>
        $"hatch: {key} - the project's repositories changed under us since this was read - try again";

    private async Task<int> SpendAsync(
        string? key, string? under, bool mine, string? model, string? effort, bool attach, bool quiet,
        CancellationToken ct)
    {
        if (!runtime.Sessions.CanSpawn(out var missing))
        {
            runtime.Say.Complain(missing);
            return 1;
        }

        // One heartbeat, so that an increment run by hand shows up beside the
        // loops on the Runners page rather than being a session nobody can see.
        // The instruction is not read: there is no second pass here to apply
        // one to, and the row ages out on its own once this exits. The one
        // refusal that is not weather still ends the run, before anything is
        // claimed.
        var beat = await runtime.Runners().BeatAsync(
            new RunnerHeartbeatRequest(
                Kind: RunnerKinds.Once,
                Line: key is { Length: > 0 } ticket ? $"one increment on {ticket}" : "one increment, by hand",
                Where: runtime.Where),
            ct);

        if (beat.Refusal is { Length: > 0 } refusal)
        {
            runtime.Say.Complain($"hatch: {refusal}");
            return 1;
        }

        // No tally here - this is one increment, not a night - so the readout's
        // runner row shows this one and nothing carried from a restart.
        runtime.Readout.SetRunner(new RunnerSnapshot(runtime.RunnerName, beat.Instruction?.For, 1, TimeSpan.Zero, 0m, null));

        var clones = runtime.Settings.Workspace is not null;

        WorkDto? work = null;
        Claim? claim = null;
        Checkouts.Choice? chosen = null;

        // Set once a real, headless increment starts - so a catch below that
        // never reaches a report can still ask what `claude --resume` would
        // take. Null for an attached session, which is a person's to resume.
        Increment? increment = null;

        // What the release in the `finally` below says about how this
        // increment went - the report's own verdict on the ordinary path, or
        // dropped where the runner itself fell over. Null on every other way
        // out, matching every release before this one.
        string? releaseOutcome = null;

        try
        {
            if (key is { Length: > 0 })
            {
                // A live claim held by somebody else comes back as `blocked`
                // carrying the server's sentence, so a ticket another runner is
                // working refuses here and nothing is spawned.
                WorkDto? named;
                try
                {
                    named = await runtime.Board.WorkAsync(runtime.Checkouts, key, null, ct, clones);
                }
                catch (HatchException e)
                {
                    runtime.Say.Complain(e.Message);
                    return 1;
                }

                if (named is null)
                {
                    runtime.Say.Complain($"hatch: {key} - there is nothing to do here");
                    return 2;
                }

                if (Refuse(named)) return 2;

                // Express, standing in a column marked to skip: carried across with
                // no session, and nothing above this has taken a checkout or a
                // claim yet, so there is nothing to undo.
                if (named.Hop)
                {
                    string? walkOn;
                    try
                    {
                        (_, walkOn) = await runtime.Board.HopAsync(runtime.Checkouts, key, ct, clones);
                    }
                    catch (HatchException e)
                    {
                        runtime.Say.Complain(e.Message);
                        return 1;
                    }

                    if (walkOn is not null)
                    {
                        runtime.Say.Complain($"hatch: {key} - {walkOn}");
                        return 2;
                    }

                    runtime.Say.Line($"hatch: {key}  {named.FromStatus.Name} -> {named.ToStatus?.Name}  {Picker.HopReason(named.HopKind, named.HopUnder)}");
                    return 0;
                }

                // Without a clone, so that the ordinary case - already matched, or
                // never going to match at all - refuses exactly as it always has,
                // before anything is claimed. Only a primary this runner could
                // still clone its way to needs the claim first.
                var preclaim = Checkouts.Choose(named.Repositories, runtime.Checkouts, runtime.Root, runtime.Settings.BaseBranch);
                if (preclaim is null && runtime.Settings.Workspace is null)
                {
                    runtime.Say.Complain(Changed(key));
                    return 2;
                }

                // The race between that read and this take, which the server
                // decides and this only reports.
                var (taken, refused) = await Claim.TakeAsync(
                    runtime.Board.Client, key, runtime.RunnerName, ct, runtime.Heartbeat);
                if (taken is null)
                {
                    runtime.Say.Complain($"hatch: {key} - {refused?.Sentence ?? "the claim was refused"}");
                    return 2;
                }

                Checkouts.Choice resolvedChoice;
                try
                {
                    if (preclaim is not null)
                    {
                        resolvedChoice = preclaim;
                    }
                    else
                    {
                        // Only now, with the lease already held: a checkout cloned for
                        // a ticket nobody ends up spending is a checkout somebody
                        // else's increment would have to notice and clean up.
                        var resolved = Clones.Resolve(
                            named, runtime.Checkouts, runtime.Root, runtime.Settings.BaseBranch,
                            runtime.Settings.Workspace, runtime.MakeClone);
                        runtime = runtime with { Checkouts = resolved.Checkouts };

                        if (resolved.Failed is { } failure)
                        {
                            await taken.ReleaseAsync();

                            // Best-effort: the claim is already let go of either way.
                            try
                            {
                                await runtime.Board.CommentAsync(
                                    key, $"hatch could not clone {failure.Remote} into {failure.Path}:\n\n    {failure.Error}", ct);
                            }
                            catch (Exception e) when (e is HatchException or OperationCanceledException)
                            {
                                runtime.Say.Complain($"hatch: {key} - could not tell the board that {failure.Remote} could not be cloned - {e.Message}");
                            }

                            runtime.Say.Complain(
                                $"hatch: {key} - could not clone {failure.Remote} into {failure.Path}: {failure.Error}");
                            return 2;
                        }

                        if (resolved.Chosen is not { } fromClone)
                        {
                            await taken.ReleaseAsync();
                            runtime.Say.Complain(Changed(key));
                            return 2;
                        }

                        resolvedChoice = fromClone;
                    }
                }
                catch (Exception)
                {
                    await taken.ReleaseAsync();
                    throw;
                }

                (work, claim, chosen) = (named, taken, resolvedChoice);
            }
            else
            {
                // The same walk the loop uses, so one rule picks a ticket wherever
                // a ticket is picked.
                var picked = await runtime.Picker().PickAsync(under, runtime.OffsetMinutes, ct, runtime.Heartbeat, mine);

                switch (picked.Outcome)
                {
                    case Pick.Idle:
                        await runtime.Idle(mine).ReportAsync(under, picked.Queue, runtime.OffsetMinutes, ct);
                        return 2;

                    case Pick.Busy:
                        runtime.Say.Lines(Picker.BusyReport(under, picked.Busy));
                        return 2;

                    case Pick.Unreadable:
                        return 1;

                    case Pick.Refused:
                        runtime.Say.Complain(picked.Refusal ?? "hatch: the board refused this pass");
                        return 1;

                    case Pick.Hopped:
                        var hop = picked.Hopped!;
                        runtime.Say.Line($"hatch: {hop.Key}  {hop.From} -> {hop.To}  {hop.Reason}");
                        return 0;
                }

                if (picked.Checkouts is { } grown) runtime = runtime with { Checkouts = grown };
                (work, claim, chosen) = (picked.Work!, picked.Claim!, picked.Chosen!);
            }

            // What the server decided, unless a flag says otherwise. The
            // playbook's model is already the effective value - an issue
            // carrying its own has had it folded in there - so a flag beats an
            // override for free, and typing one is naming a value for this run.
            model ??= work.Playbook?.Model ?? "";
            effort ??= work.Playbook?.Effort ?? "";

            // The tree is made ready the way the loop makes it, and for the same
            // reason: a session should not have to work out its own base. One
            // difference - a tree with changes in it is refused rather than
            // stashed, because somebody is sitting there and they are theirs.
            foreach (var (path, baseBranch) in chosen.Resets)
            {
                switch (runtime.Workspace(path, baseBranch).Prepare(stash: false))
                {
                    case Reset.Never:
                        return 1;

                    case Reset.Later:
                        runtime.Say.Complain($"hatch: the workspace is not ready ({path}) - try again");
                        return 1;
                }
            }

            var lifecycle = new Lifecycle(runtime);

            // The loop's own recheck, through the same call: the board's verdict
            // was read before the claim and the reset, and a conflict that has
            // gone since is nothing to spend a session on.
            Rechecked? found = null;
            if (work.Kind == WorkKinds.Conflicts)
            {
                found = await lifecycle.RecheckAsync(work, chosen, ct);

                if (found.Conflicts.Count == 0)
                {
                    if (found.Unknown)
                    {
                        runtime.Say.Complain(
                            $"hatch: {work.Issue.Key} - its branch could not be checked against the trunk, so nothing was spawned");
                        return 1;
                    }

                    runtime.Say.Line(
                        $"hatch: {work.Issue.Key} no longer conflicts with {found.Trunk ?? Conflicts.Trunk(work.Issue)} - nothing to do");
                    return 0;
                }
            }

            // And a failing build, through the same call as the loop's.
            BuildFound? built = null;
            if (work.Kind == WorkKinds.Build)
            {
                built = await lifecycle.RecheckBuildAsync(work, chosen, ct);

                if (built.StillFailing.Count == 0)
                {
                    if (built.Unknown)
                    {
                        runtime.Say.Complain(
                            $"hatch: {work.Issue.Key} - its build could not be read, so nothing was spawned");
                        return 1;
                    }

                    runtime.Say.Line($"hatch: {work.Issue.Key} {built.Nothing()} - nothing to do");
                    return 0;
                }
            }

            var entering = await lifecycle.EnterAsync(work, chosen, ct);
            if (entering.Asked)
            {
                await lifecycle.LeaveAsync(work, chosen, ownsTicket: false, ct);
                return 2;
            }

            // False until a branch below says otherwise, rather than true until
            // one says not: an exception thrown out of either branch is neither
            // a finished attach nor a report to read `LostLease` off, and
            // defaulting to "owned" would have the `finally` below write a tidy
            // comment and push a pending merge for an increment that never
            // finished.
            var owned = false;
            IncrementReport? report = null;
            try
            {
                if (attach)
                {
                    var code = await AttachAsync(work, model, effort, claim, chosen, entering.Entries, found, built, ct);
                    owned = true;
                    return code;
                }

                // Zero for an increment that happened, whatever the session exited
                // with: the report is where "it went badly" is said, and a shell
                // that treated a hard ticket as a broken command would be one more
                // thing an operator has to work around.
                increment = runtime.Increment();

                using var skip = CancellationTokenSource.CreateLinkedTokenSource(ct);
                void OnSkip() => skip.Cancel();
                runtime.Controls.Skipped += OnSkip;
                try
                {
                    report = await increment.RunAsync(
                        work, chosen.Root, model, effort, quiet, claim, skip.Token, ct, chosen.AddDirs, chosen.Repositories, entering.Entries,
                        found is null ? null : new ConflictRun(found, judge => lifecycle.JudgeAsync(work, chosen, judge)),
                        built is null ? null : new BuildRun(built, judge => lifecycle.JudgeBuildAsync(work.Issue.Key, built, chosen, judge)),
                        runnerName: runtime.RunnerName, incrementNumber: 1);
                }
                finally
                {
                    runtime.Controls.Skipped -= OnSkip;
                }

                owned = !report.LostLease;
                releaseOutcome = report.ReleaseOutcome;
                return 0;
            }
            finally
            {
                owned &= claim.Lost is null;
                var limit = report?.UsageLimitResetAt is { } resetAt
                    ? new UsageLimitInfo(resetAt, report.UsageLimitResetKnown, report.SessionId)
                    : null;
                var preempted = report is { Preempted: true }
                    ? new PreemptionInfo(report.PreemptedKey!, report.PreemptedTitle!, report.SessionId)
                    : null;
                await lifecycle.LeaveAsync(work, chosen, owned, CancellationToken.None, limit, preempted);

                // Every opening banner has a closing one - null only for the
                // attach path, which prints its own header and has no report to
                // close with; a person is sitting at that session.
                if (report is not null)
                    runtime.Say.Lines(Banner.Closing(report, Readout.OneLine(runtime.Readout.Snapshot().UsageWindows)));
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // One failed increment, not an unhandled crash: whatever stage this
            // was in - picking, claiming, the reset, the recheck, the branch
            // switch, or the session itself - the tree has already gone back to
            // the trunk by now, by the same `finally` that runs on every other
            // way out of the inner try above, or was never moved off it to
            // begin with. A ticket picked by the walk rather than named names
            // itself through the claim it had already taken when it fell over.
            var ticketKey = work?.Issue.Key ?? (e as PickFailedException)?.Key ?? key;
            var cause = e is PickFailedException { InnerException: { } inner } ? inner : e;
            var why = $"{cause.GetType().Name}: {cause.Message}";

            runtime.Say.Complain(ticketKey is { Length: > 0 }
                ? $"hatch: {ticketKey} - the runner failed, and is letting it go - {why}"
                : $"hatch: the runner failed, before a ticket was settled on - {why}");

            if (ticketKey is { Length: > 0 })
                await LetGo.CommentAsync(runtime.Board, runtime.Say, ticketKey, why, increment?.SessionId, CancellationToken.None);

            // The runner fell over rather than the increment running to a
            // verdict, so the claim (where one was ever held) goes back the
            // same way any other stall does.
            releaseOutcome = ClaimOutcomes.Dropped;

            return 1;
        }
        finally
        {
            if (claim is not null) await claim.ReleaseAsync(releaseOutcome);
        }
    }

    /// <summary>
    /// The same ticket, the same playbook, the same budget, in a session
    /// somebody is sitting in front of - claimed for as long as it runs, and
    /// released when it ends.
    /// </summary>
    /// <remarks>
    /// Its lost-lease sentinel is written and never acted on: there is a person
    /// in front of that terminal, and killing their session out from under them
    /// is not the heartbeat's call.
    /// </remarks>
    private async Task<int> AttachAsync(
        WorkDto work, string model, string effort, Claim claim, Checkouts.Choice chosen,
        IReadOnlyList<BranchEntry> branches, Rechecked? conflict, BuildFound? build, CancellationToken ct)
    {
        runtime.Say.Line($"hatch: {work.Issue.Key} [{work.Issue.Type}] {work.Issue.Title}");
        runtime.Say.Line(
            work.Kind == WorkKinds.Conflicts ? $"hatch: {model}, effort {effort}, {work.FromStatus.Name}, {Conflicts.Words(work.Issue)}"
            : work.Kind == WorkKinds.Build ? $"hatch: {model}, effort {effort}, {work.FromStatus.Name}, {Builds.Words(work.Issue)}"
            : $"hatch: {model}, effort {effort}, {work.FromStatus.Name} -> {work.ToStatus?.Name}");
        if (Prompt.OverrideLine(work, model, effort) is { } chose) runtime.Say.Line($"hatch:   {chose}");
        runtime.Say.Line("");

        // The prompt carries what was said to the agent, so it is read. Nothing
        // here declares hooks: a person is in that session, and they can say
        // it to the agent themselves.
        await runtime.Increment().MarkSaidAsync(work, ct);

        return await runtime.Sessions.AttachAsync(
            new SessionRequest(chosen.Root, model, effort, Prompt.Compose(work, chosen.Repositories, branches, conflict, build), Quiet: false, chosen.AddDirs),
            ct);
    }

    /// <summary>
    /// Whether the dispatch says no agent should be spawned - and if so, why, in
    /// the server's own sentence.
    /// </summary>
    /// <remarks>
    /// A refusal that names a question prints the question. Being told a ticket
    /// is blocked and then having to go and ask what by is two round trips for
    /// something already in hand.
    /// </remarks>
    private bool Refuse(WorkDto work)
    {
        if (work.Blocked is not { Length: > 0 } why) return false;

        runtime.Say.Complain($"hatch: {work.Issue.Key} - {why}");

        var open = work.Questions.Where(q => q.Answers.Count == 0).ToList();
        if (open.Count > 0)
        {
            runtime.Say.Complain("");
            foreach (var line in Questions.Draw(open)) runtime.Say.Complain(line);
            runtime.Say.Complain($"  hatch answer {work.Issue.Key}");
            runtime.Say.Complain($"  {runtime.Board.Client.Origin}/issues/{work.Issue.Key}");
        }

        return true;
    }
}

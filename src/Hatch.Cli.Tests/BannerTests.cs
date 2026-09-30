namespace Hatch.Cli.Tests;

/// <summary>
/// The eggs: what wraps one increment in the log, pure of any terminal.
/// </summary>
public sealed class BannerTests
{
    private static IncrementReport Report(
        string key = "AER-1", string from = "In Progress", string to = "In Review", string ended = "In Review",
        bool moved = false, bool resolved = false, bool fixPushed = false, bool stalled = false,
        bool lostLease = false, bool interrupted = false, bool preempted = false, int exitCode = 0, string? flag = null,
        long? totalTokens = 1234, decimal? cost = 1.5m, int? turns = 12,
        DateTimeOffset? started = null, DateTimeOffset? ended2 = null) => new()
    {
        Key = key,
        From = from,
        To = to,
        Ended = ended,
        Moved = moved,
        Resolved = resolved,
        FixPushed = fixPushed,
        Stalled = stalled,
        LostLease = lostLease,
        Interrupted = interrupted,
        Preempted = preempted,
        PreemptedKey = preempted ? "AER-9" : null,
        PreemptedTitle = preempted ? "Trunk is down" : null,
        ExitCode = exitCode,
        Flag = flag,
        TotalTokens = totalTokens,
        Cost = cost,
        Turns = turns,
        StartedAt = started ?? DateTimeOffset.UnixEpoch,
        EndedAt = ended2 ?? DateTimeOffset.UnixEpoch.AddSeconds(272),
    };

    // ---- Opening ----

    [Fact]
    public void AnOrdinaryMove_NamesTheTicketTheModelAndTheRunner()
    {
        var work = Fixtures.Work("AER-1", from: "In Progress", to: "In Review");
        var report = Report();

        var lines = Banner.Opening(report, work, "opus", "high", "Chrissy", 3, conflict: null, build: null);

        Assert.Equal("🥚🥚🥚🥚🥚 STARTING WORK ON AER-1", lines[0]);
        Assert.Contains(lines, l => l.Contains("[task]", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("In Progress -> In Review", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("opus, effort high", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Chrissy, increment 3 of the night", StringComparison.Ordinal));
    }

    [Fact]
    public void AConflictIncrement_NamesTheTrunkInsteadOfAColumn()
    {
        var work = Fixtures.Work("AER-1", kind: WorkKinds.Conflicts);
        var report = Report();
        report.ConflictTrunk = "main";
        var conflict = new ConflictRun(new Rechecked([], false, true), _ => throw new NotSupportedException());

        var lines = Banner.Opening(report, work, "opus", "high", "Chrissy", 1, conflict, build: null);

        Assert.Contains(lines, l => l.Contains("resolving conflicts with main", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOverriddenModel_SaysTheIssueChoseIt()
    {
        var issue = Fixtures.Issue("AER-1", modelOverride: "opus");
        var work = Fixtures.Work("AER-1", issue: issue);
        var report = Report();

        var lines = Banner.Opening(report, work, "opus", "high", "Chrissy", 1, conflict: null, build: null);

        Assert.Contains(lines, l => l.Contains("model from AER-1, not the playbook", StringComparison.Ordinal));
    }

    // ---- Closing: the glyph ----

    [Fact]
    public void AMove_ClosesWithAChick()
    {
        var lines = Banner.Closing(Report(moved: true));
        Assert.StartsWith("🐣🐣🐣🐣🐣 STOPPING WORK ON AER-1", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("Moved from In Progress to In Review", StringComparison.Ordinal));
    }

    [Fact]
    public void AResolvedConflict_ClosesWithAChick()
    {
        var report = Report(resolved: true);
        report.ConflictTrunk = "main";

        var lines = Banner.Closing(report);

        Assert.StartsWith("🐣🐣🐣🐣🐣 STOPPING WORK ON AER-1", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("conflicts with main resolved", StringComparison.Ordinal));
    }

    [Fact]
    public void AStall_ClosesWithAnEgg()
    {
        var lines = Banner.Closing(Report(stalled: true, flag: "flagged"));
        Assert.StartsWith("🥚🥚🥚🥚🥚 STOPPING WORK ON AER-1", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("still in \"In Review\", flagged", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonZeroExit_ClosesWithAPanEvenThoughItMoved()
    {
        // The error glyph outranks the rest - a session that exited badly said
        // nothing trustworthy about whether the board moved.
        var lines = Banner.Closing(Report(moved: true, exitCode: 1));
        Assert.StartsWith("🍳🍳🍳🍳🍳 STOPPING WORK ON AER-1", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ALostLease_ClosesWithAPan()
    {
        var lines = Banner.Closing(Report(lostLease: true));
        Assert.StartsWith("🍳🍳🍳🍳🍳 STOPPING WORK ON AER-1", lines[0], StringComparison.Ordinal);
    }

    /// <summary>HA-169: a preemption is its own glyph, not the error pan.</summary>
    [Fact]
    public void APreemption_ClosesWithASiren()
    {
        var lines = Banner.Closing(Report(preempted: true, exitCode: 143));
        Assert.StartsWith("🚨🚨🚨🚨🚨 STOPPING WORK ON AER-1", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnInterruptedIncrement_ClosesWithAPan()
    {
        var lines = Banner.Closing(Report(interrupted: true, flag: "interrupted - nothing further could be read or written"));
        Assert.StartsWith("🍳🍳🍳🍳🍳 STOPPING WORK ON AER-1", lines[0], StringComparison.Ordinal);
    }

    // ---- Closing: what it took ----

    [Fact]
    public void TheTookLine_NamesTheWallClockTheTokensTheCostAndTheTurns()
    {
        var lines = Banner.Closing(Report(moved: true, totalTokens: 128_000, cost: 1.5m, turns: 42,
            started: DateTimeOffset.UnixEpoch, ended2: DateTimeOffset.UnixEpoch.AddSeconds(272)));

        Assert.Contains(lines, l => l.Contains("Took 4m32s, 128k tokens, $1.5, 42 turns", StringComparison.Ordinal));
    }

    [Fact]
    public void AFigureThatNeverArrived_ReadsNotReportedAndNeverZero()
    {
        var lines = Banner.Closing(Report(moved: true, totalTokens: null, cost: null, turns: null));

        var took = Assert.Single(lines, l => l.Contains("Took", StringComparison.Ordinal));
        Assert.Contains("tokens not reported", took, StringComparison.Ordinal);
        Assert.Contains("cost not reported", took, StringComparison.Ordinal);
        Assert.Contains("turns not reported", took, StringComparison.Ordinal);
        Assert.DoesNotContain("0 tokens", took, StringComparison.Ordinal);
    }

    [Fact]
    public void AUsageLine_IsAddedOnlyWhereThereIsOne()
    {
        var withUsage = Banner.Closing(Report(moved: true), usageLine: "session 42% (reset in 3h)");
        Assert.Contains(withUsage, l => l.Contains("session 42%", StringComparison.Ordinal));

        var without = Banner.Closing(Report(moved: true));
        Assert.DoesNotContain(without, l => l.Contains("session 42%", StringComparison.Ordinal));
    }
}

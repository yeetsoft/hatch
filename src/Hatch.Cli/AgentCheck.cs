namespace Hatch.Cli;

/// <summary>
/// The cheap session a runner whose agent has stopped starting runs before it
/// claims anything again: one turn on the adapter's cheapest model, in a
/// directory of its own, through the same <see cref="ISessionRunner"/> the
/// increments use. It claims nothing and touches no ticket.
/// </summary>
/// <remarks>
/// Never calls the adapter's probe, so it cannot mark a probe as costly - the
/// probe's own bookkeeping is about the account's usage window, and this reads
/// only whether a session starts and answers.
/// </remarks>
public static class AgentCheck
{
    /// <summary>What the check asks. Short, so the answer is a few tokens.</summary>
    public const string Prompt = "Reply with the single word ok.";

    /// <summary>How long the session has to answer before the check calls it silent.</summary>
    public static readonly TimeSpan DefaultCeiling = TimeSpan.FromMinutes(2);

    /// <param name="Passed">The agent started and its result was not an error.</param>
    /// <param name="Why">The sentence saying why not; empty on a pass.</param>
    /// <param name="Cost">What the session reported costing, or null.</param>
    /// <param name="Tokens">The tokens it reported, or null.</param>
    public sealed record Verdict(bool Passed, string Why, decimal? Cost, long? Tokens);

    /// <summary>
    /// Runs the check. A <paramref name="ct"/> that is cancelled ends it with
    /// the cancellation, for the caller to read as the loop stopping; every
    /// other way it can go wrong is a failed <see cref="Verdict"/>.
    /// </summary>
    public static async Task<Verdict> RunAsync(
        ISessionRunner sessions, IAgentAdapter adapter, Settings settings, string tempRoot, CancellationToken ct,
        TimeSpan? ceiling = null)
    {
        // Asked as a claim is asked, before anything is spawned: a runner with no
        // CLI to run has nothing to check, and says why.
        if (!sessions.CanSpawn(out var refusal))
            return new Verdict(false, refusal, null, null);

        var dir = Path.Combine(tempRoot, $"hatch-agent-check-{Guid.NewGuid():N}");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(ceiling ?? DefaultCeiling);

        try
        {
            Directory.CreateDirectory(dir);

            var model = adapter.CheapestModel;
            var request = new SessionRequest(dir, model, "low", Prompt, Quiet: true, Settings: settings);
            var facts = new RunFacts();
            var reader = adapter.CreateReader(dir, facts, model);

            SessionResult result;
            try
            {
                result = await sessions.RunAsync(request, null, limit.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new Verdict(false, $"no answer in {Format.Duration((long)(ceiling ?? DefaultCeiling).TotalSeconds)}", null, null);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return new Verdict(false, e.Message, null, null);
            }

            // Read as Increment reads a quiet run: one event per line, where an
            // agent that answers with a single object is a single pass.
            var output = result.Output.Trim();
            if (output.StartsWith('{'))
            {
                foreach (var raw in output.Split('\n'))
                {
                    if (raw.Trim().Length == 0) continue;
                    foreach (var _ in reader.Read(raw)) { }
                }
            }

            var cost = facts.CostUsd;
            var tokens = facts.Result?.Models?.Sum(m =>
                m.InputTokens + m.OutputTokens + m.CacheCreationTokens + m.CacheReadTokens);

            if (limit.IsCancellationRequested && !ct.IsCancellationRequested)
                return new Verdict(false, $"no answer in {Format.Duration((long)(ceiling ?? DefaultCeiling).TotalSeconds)}", cost, tokens);

            if (!facts.Started)
            {
                var last = Scrub.Lines(result.Stray.Concat(result.Stderr)).LastOrDefault();
                return new Verdict(false, last is { Length: > 0 } ? $"the agent never started - the CLI said \"{last}\"" : "the agent never started", cost, tokens);
            }

            if (facts.Result is { IsError: true })
                return new Verdict(false, facts.ResultText is { Length: > 0 } text ? text : "the session ended with an error", cost, tokens);

            return new Verdict(true, "", cost, tokens);
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Best-effort, the same as every other clean-up of a temp
                // directory: the OS sweeps it in time.
            }
        }
    }
}

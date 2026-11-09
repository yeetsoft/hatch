namespace Hatch.Cli;

/// <summary>
/// The models this runner has stopped offering, each until an instant or for
/// the rest of the run - what the beat leaves off its list so the board does
/// not route a ticket to a model this process cannot run.
/// </summary>
/// <remarks>
/// A plain class held on <see cref="Runtime"/>, so the one instance survives the
/// loop's <c>runtime with { ... }</c>. The night state carries it across a
/// supervisor restart; a fresh start has none. The beat may run off the loop's
/// thread, so every read and write takes the one lock.
/// </remarks>
public sealed class ModelNarrowing
{
    private readonly object _gate = new();

    // A null value is "for the rest of this run".
    private readonly Dictionary<string, DateTimeOffset?> _until = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stop offering <paramref name="model"/> until <paramref name="until"/>, or
    /// for the rest of the run where that is null. True only when the entry
    /// changed - new, or a different instant - so the caller says it once. An
    /// instant already past is no narrowing at all.
    /// </summary>
    public bool Narrow(string model, DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is { } at && at <= now) return false;

        lock (_gate)
        {
            if (_until.TryGetValue(model, out var held) && held == until) return false;

            _until[model] = until;
            return true;
        }
    }

    /// <summary>
    /// The models a probe reading shows spent: each per-model weekly window at
    /// 100% or more and still ahead of its reset, whose label names a tier,
    /// paired with that reset. A label that names none (<c>Weekly (model)</c>)
    /// narrows nothing - there is no alias to drop.
    /// </summary>
    public static IEnumerable<(string Model, DateTimeOffset Until)> Spent(IReadOnlyList<UsageWindow> windows, DateTimeOffset now)
    {
        foreach (var window in windows)
        {
            if (window.Window != "weeklyModel" || window.Utilization < 1) continue;
            if (window.ResetsAt is not { } at || at <= now) continue;

            var model = ModelFamilies.Aliases.FirstOrDefault(
                alias => window.Label.Contains(alias, StringComparison.OrdinalIgnoreCase));
            if (model is not null) yield return (model, at);
        }
    }

    /// <summary>The models in the order given, less any narrowed at <paramref name="now"/>.</summary>
    public IReadOnlyList<string> Offered(IEnumerable<string> models, DateTimeOffset now)
    {
        lock (_gate)
        {
            return [.. models.Where(m => !Live(m, now))];
        }
    }

    /// <summary>
    /// Why a run on <paramref name="model"/> cannot happen, or null where it can:
    /// the model is absent or not narrowed at <paramref name="now"/>.
    /// </summary>
    public string? Refusal(string? model, string agent, DateTimeOffset now, TimeProvider clock)
    {
        if (model is null) return null;

        DateTimeOffset? until;
        lock (_gate)
        {
            if (!Live(model, now)) return null;
            until = _until[model];
        }

        var when = until is { } at ? $"until {UsageLimit.Clock(at, known: false, clock)}" : "for the rest of this run";
        return $"runs on {model} - this runner's {agent} no longer offers it {when}";
    }

    /// <summary>What a restart hands over: the entries still in force at <paramref name="now"/>.</summary>
    public IReadOnlyList<NarrowedModel> ToState(DateTimeOffset now)
    {
        lock (_gate)
        {
            return [.. _until.Where(e => e.Value is null || e.Value > now).Select(e => new NarrowedModel(e.Key, e.Value))];
        }
    }

    /// <summary>Replace what is held with what a restart carried, less what has already run out. Null clears it.</summary>
    public void Load(IEnumerable<NarrowedModel>? carried, DateTimeOffset now)
    {
        lock (_gate)
        {
            _until.Clear();
            foreach (var entry in carried ?? [])
            {
                if (entry.Until is { } at && at <= now) continue;
                _until[entry.Model] = entry.Until;
            }
        }
    }

    // Callers hold the lock.
    private bool Live(string model, DateTimeOffset now) =>
        _until.TryGetValue(model, out var until) && (until is null || until > now);
}

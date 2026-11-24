using System.Diagnostics.Metrics;
using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Reads the dispatch meter for the code a test runs, and nothing else. It
/// begins a <see cref="ScanTally"/>, and its listener keeps a measurement only
/// when <see cref="ScanTally.Current"/> on the recording thread is that tally.
/// The meter is process-wide and test classes run in parallel; a callback runs
/// inline on the thread that recorded, so a scan in another test never lands
/// here. No <c>IMeterFactory</c>, so no constructor changes.
/// </summary>
internal sealed class ScanMeterProbe : IDisposable
{
    private readonly ScanTally.Scope scope;
    private readonly MeterListener listener = new();
    private readonly object gate = new();
    private readonly Dictionary<string, long> scans = [];
    private readonly Dictionary<string, long> treeBuilds = [];

    public ScanMeterProbe()
    {
        scope = ScanTally.Begin("test");
        Tally = scope.Tally;

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ScanMetrics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>(OnLong);
        listener.Start();
    }

    public ScanTally Tally { get; }

    /// <summary>Scans started, by caller name.</summary>
    public IReadOnlyDictionary<string, long> Scans { get { lock (gate) return new Dictionary<string, long>(scans); } }

    /// <summary>Priority trees built, by the caller name or route that built them.</summary>
    public IReadOnlyDictionary<string, long> TreeBuilds { get { lock (gate) return new Dictionary<string, long>(treeBuilds); } }

    private void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!ReferenceEquals(ScanTally.Current, Tally)) return;

        string? caller = null;
        foreach (var tag in tags)
            if (tag.Key == "caller") caller = tag.Value as string;
        if (caller is null) return;

        lock (gate)
        {
            var into = instrument.Name == "hatch.dispatch.scans" ? scans
                : instrument.Name == "hatch.dispatch.tree_builds" ? treeBuilds
                : null;
            if (into is not null) into[caller] = into.GetValueOrDefault(caller) + value;
        }
    }

    public void Dispose()
    {
        listener.Dispose();
        scope.Dispose();
    }
}

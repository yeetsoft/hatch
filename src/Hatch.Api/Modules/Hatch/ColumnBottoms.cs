namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The bottom of each column, for a request that appends to one more than
/// once.
///
/// <see cref="RankService.BottomAsync"/> asks the database where the bottom
/// is, and a bulk edit saves nothing until it has finished - so fifty issues
/// sent to the same column would every one of them be told the same number.
/// Ties are survivable (the board breaks them by id, and the next drop into
/// that column renumbers it) but they are not what was meant, and the order
/// the operator's list was in is lost. So the first append per column asks,
/// and the rest count on from it.
/// </summary>
internal sealed class ColumnBottoms(RankService ranks)
{
    private readonly Dictionary<int, long> handedOut = [];

    public async Task<long> NextAsync(int statusId, CancellationToken ct)
    {
        var rank = handedOut.TryGetValue(statusId, out var previous)
            ? previous + RankService.Gap
            : await ranks.BottomAsync(statusId, ct);

        handedOut[statusId] = rank;
        return rank;
    }
}

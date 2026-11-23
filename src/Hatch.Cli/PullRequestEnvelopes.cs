namespace Hatch.Cli;

/// <summary>
/// Writes a ticket's chain into the body of the pull request that was opened for it - the half of
/// <c>hatch pr</c> a board that receives the pull request reads. Shared by the command and by the
/// driver that opens a waiting pull request, so the two write it one way.
/// </summary>
internal static class PullRequestEnvelopes
{
    /// <summary>The most parents one envelope's chain climbs - a backstop to the cycle guard, far deeper than any tree.</summary>
    public const int MaxChain = 16;

    /// <summary>The reason the envelope was not written, or null where it was - or where it was already there.</summary>
    public static async Task<string?> TryWriteAsync(
        Board board, IForge forge, string url, IssueDto own, CancellationToken ct)
    {
        var names = (await board.StatusesAsync(ct)).ToDictionary(s => s.Id, s => s.Name);
        var origin = board.Client.Origin;

        var chain = new List<PullRequestEnvelopeIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var issue = own; issue is not null && seen.Add(issue.Key) && chain.Count < MaxChain;)
        {
            chain.Add(new PullRequestEnvelopeIssue(
                issue.Key, issue.Title, issue.Type, issue.Description,
                names.GetValueOrDefault(issue.StatusId), issue.ParentKey,
                $"{origin}/issues/{issue.Key}"));

            issue = issue.ParentKey is { Length: > 0 } parent && !seen.Contains(parent)
                ? await board.IssueAsync(parent, ct)
                : null;
        }

        var envelope = new PullRequestEnvelope(PullRequestEnvelope.CurrentVersion, origin, own.ProjectKey, chain);

        var read = await forge.ReadBodyAsync(url, ct);
        if (read.Body is not { } body) return read.Why ?? "the body could not be read";

        var replaced = PullRequestEnvelopeWriter.Replace(body, envelope, PullRequestEnvelopeWriter.GitHubBodyLimit);
        if (replaced == body) return null;

        var edit = await forge.EditBodyAsync(url, replaced, ct);
        return edit.Edited ? null : edit.Why ?? "the body could not be written";
    }
}

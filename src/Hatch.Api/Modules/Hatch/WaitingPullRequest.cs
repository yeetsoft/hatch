using System.Text.Json;
using System.Text.Json.Nodes;
using Hatch.Api.Ef;
using Hatch.Contracts;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The rules every site shares about a waiting pull request - one that is
/// written and not yet opened; see <see cref="EfHatchIssue.WaitingPrTitle"/>.
/// It is not a pull request: it does not count as <c>HasPr</c>, blocks nobody,
/// and is gone the moment a pull request url is recorded by any route.
/// </summary>
public static class WaitingPullRequest
{
    public static bool Has(EfHatchIssue issue) => Has(issue.WaitingPrTitle);

    /// <summary>The same question of a projected <see cref="EfHatchIssue.WaitingPrTitle"/>, for a caller that selected the column and not the row.</summary>
    public static bool Has(string? title) => !string.IsNullOrEmpty(title);

    /// <summary>The issue's waiting pull request as the DTO draws it, or null where there is none.</summary>
    public static WaitingPullRequestDto? ToDto(EfHatchIssue issue) =>
        !Has(issue)
            ? null
            : new WaitingPullRequestDto(
                issue.WaitingPrTitle!, issue.WaitingPrBody ?? "", issue.WaitingPrBranch ?? "", issue.WaitingPrSha ?? "",
                issue.WaitingPrRemote, issue.WaitingPrRunner ?? "", issue.WaitingPrWhy ?? "",
                issue.WaitingPrAt ?? issue.UpdatedAt);

    /// <summary>What an event payload says of one side: its title, branch and sha, or null for none.</summary>
    public static object? Side(EfHatchIssue issue) =>
        Has(issue) ? new { title = issue.WaitingPrTitle, branch = issue.WaitingPrBranch, sha = issue.WaitingPrSha } : null;

    /// <summary>Empties every column of the record.</summary>
    public static void Clear(EfHatchIssue issue)
    {
        issue.WaitingPrTitle = null;
        issue.WaitingPrBody = null;
        issue.WaitingPrBranch = null;
        issue.WaitingPrSha = null;
        issue.WaitingPrRemote = null;
        issue.WaitingPrRunner = null;
        issue.WaitingPrWhy = null;
        issue.WaitingPrAt = null;
    }

    /// <summary>
    /// Clears the waiting pull request because a pull request url is now
    /// recorded, and returns the event to add; null when there was nothing to
    /// clear. Call it beside every write of <c>PullRequestUrl</c> to a
    /// non-blank value, in the same save.
    /// </summary>
    public static EfHatchIssueEvent? ClearOnRecord(
        EfHatchIssue issue, string url, string actor, DateTimeOffset now)
    {
        if (!Has(issue)) return null;

        var payload = new JsonObject
        {
            ["from"] = JsonSerializer.SerializeToNode(Side(issue)),
            ["to"] = null,
            ["pullRequest"] = url,
        };
        Clear(issue);

        return new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.WaitingPullRequestChanged,
            Payload = payload.ToJsonString(),
            At = now,
        };
    }
}

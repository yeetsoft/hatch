using System.Text.RegularExpressions;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Every <c>IgnoreQueryFilters()</c> in the API, listed with the reason it is
/// there, the way <c>AdminSurfaceTests</c> lists what stays open. A call
/// anywhere else fails the build until somebody writes the sentence.
/// </summary>
/// <remarks>
/// An opt-out reads rows the caller's scope does not hold, so each one has to
/// answer for what it lets out: a count, a rank, an id, bytes of a file the
/// same for every caller - never a hidden row's content. The list is the
/// reviewer's whole attention for that question.
/// </remarks>
public class QueryFilterAuditTests
{
    /// <summary>
    /// <c>File.Method</c>, how many calls it holds, and why each may see every
    /// project. The count is exact: a second call in a listed method is a new
    /// decision and needs its own sentence.
    /// </summary>
    private static readonly Dictionary<string, (int Calls, string Why)> Sites = new()
    {
        ["Wip.LoadAsync"] = (2,
            "The house-wide work-in-progress count. The limit is the house's, so a count that left out a hidden "
            + "project's cards would let a scoped caller push past it. It reads ids and parents and returns "
            + "numbers. Its priority tree is read the same way, through PriorityTree.ForAsync."),
        ["Questions.WaitingLevelsAsync"] = (1,
            "The strictest open question per issue, read for the priority tree. A question on an ancestor in a "
            + "project the caller cannot see still holds the work beneath it, so a read that left it out would "
            + "honour no question there. What leaves is a level and a count, which the dispatcher folds into a "
            + "sentence naming the ancestor's key, as it does for a dangerous reason."),
        ["OnePrLock.CrossProjectTaskCountAsync"] = (1,
            "How many tasks of a story stand in another project. A task in a project the caller cannot see still "
            + "keeps its story from sharing one pull request, so a count that left it out would let a scoped caller "
            + "turn the setting on over it. What leaves is a number, never the hidden task's key."),
        ["Wip.EpicsAsync"] = (1,
            "An epic's own limit, which is one number whoever asks. Returns the limit and the key it prints."),
        ["RankService.PlaceAsync"] = (1,
            "Rank placement. A column is shared by every project, so a rank placed against only the visible "
            + "cards could land on a hidden card's rank, and a renumber would reorder the two sets against each "
            + "other. What leaves is a rank."),
        ["Dispatch.ForAsync"] = (4,
            "The dependency gate and the priority tree it, the family gate, the work-in-progress count and every "
            + "projection lean on. A blocker in a project the caller cannot see still blocks, a level on an ancestor "
            + "there still applies, and a hidden issue missing from the tree would read as unpaused and unblocked. "
            + "The dispatcher reads the tree whole, whatever the calling key's owner may see; the projections read "
            + "it whole too, with the reader's own scope laid over it, so what leaves is a level or a flag that a "
            + "value came from outside the scope, never a hidden key. The family gate is the fourth: a child in a "
            + "project the caller cannot see still counts as open, so an epic is not closed over it. What leaves is "
            + "a count and an id."),
        ["ProjectRepositoryBinding.WinnersAsync"] = (1,
            "Which project a repository belongs to. The winner is the same project whoever asks, or two callers "
            + "would file the same outage's bug under different projects. What leaves is an id and a key."),
        ["ChangelogFiles.ProjectAsync"] = (3,
            "The changelog file, which is the same bytes for every caller - a trunk's own file is judged against "
            + "it, and a scoped caller who got fewer entries would see a file forever behind. It reads every "
            + "project's repositories, closed issues and tree, and returns the entries of a file."),
        ["TrunkBuildController.PutTrunkBuild"] = (1,
            "Whether a row for this repository and trunk is stored, so a put naming a repository only a hidden "
            + "project binds is answered as a fresh row and not by the unique index failing. What is read is an "
            + "id, so the response reads as an insert would."),
        ["PlaybookFileController.PutPlaybookFile"] = (1,
            "Whether a row for this repository and trunk is stored, so a put naming a repository only a hidden "
            + "project binds is answered as a fresh row and not by the unique index failing. What is read is an "
            + "id, so the response reads as an insert would."),
        ["DependencyFileController.PutDependencyFile"] = (1,
            "Whether a row for this repository and trunk is stored, so a put naming a repository only a hidden "
            + "project binds is answered as a fresh row and not by the unique index failing. What is read is an "
            + "id, so the response reads as an insert would."),
        ["HatchContext.OnModelCreating"] = (1,
            "Inside the playbook proposal filter, reading the proposal's changes. A change's filter reads its "
            + "proposal and the proposal's filter reads its changes, and EF overflows the stack on the loop. The "
            + "playbook rule is written out in the filter beside it, so the opt-out lets nothing hidden through."),
        ["ProjectAccess.ReadAsync"] = (3,
            "The member, scope and scope-project rows of every person, read once per priority tree. Whether a level "
            + "set in another project may be honoured is a fact about the person who set it and not about whoever "
            + "is reading, so a filter on the caller's own scope would make one level read two ways. Nothing leaves "
            + "but yes or no, asked of an issue the caller already has."),
        ["ProjectsController.KeyTakenAsync"] = (1,
            "Whether a project key is taken. A key is unique across the house, so a hidden project's key is "
            + "just as taken as a visible one, and a filtered read would let a scoped caller through to the "
            + "unique index. What leaves is one bool, no row."),
        ["GooseFindings.FiledFromAFindingAsync"] = (1,
            "Whether an issue was filed as the bug for some finding, asked board-wide so every check agrees on "
            + "the answer regardless of the caller's own scope - a job's context already reads every project, "
            + "but a later caller must get the same answer a job would. What leaves is one bool, no row."),
        ["GooseJob.SweepAsync"] = (1,
            "The goose's own retention prune of closed findings, on a timer with no request and no caller scope "
            + "at all - the same reason the run pruning beside it reads every project. Nothing leaves the "
            + "process; a row is only ever deleted, never returned."),
    };

    private static readonly Regex Call = new(@"\.IgnoreQueryFilters\(", RegexOptions.Compiled);

    /// <summary>A line that opens a method: modifiers, a return type (which may be a tuple, so it may hold parentheses), a name, an open parenthesis.</summary>
    private static readonly Regex Method = new(
        @"^\s+(?:(?:public|private|internal|protected|static|async|override|sealed|virtual|unsafe)\s+)+[^=;]*?\b(\w+)\s*(?:<[^>]*>)?\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void EveryCallSite_IsListedWithItsReason_AndItsCount()
    {
        var found = FindSites();

        var unlisted = found.Keys.Where(k => !Sites.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(
            unlisted.Count == 0,
            "IgnoreQueryFilters() is called in " + string.Join(", ", unlisted)
            + ", which QueryFilterAuditTests.Sites does not list. An opt-out reads rows outside the caller's scope: "
            + "list the method with the sentence that says why it may, or take the call out.");

        foreach (var (site, (calls, _)) in Sites)
            if (found.TryGetValue(site, out var actual))
                Assert.True(actual == calls, $"{site} holds {actual} IgnoreQueryFilters() calls and the list says {calls}");
    }

    [Fact]
    public void EveryListedSite_StillHoldsACall()
    {
        var found = FindSites();

        foreach (var site in Sites.Keys)
            Assert.True(found.ContainsKey(site), $"{site} is listed and holds no IgnoreQueryFilters() call any more - take it off the list");
    }

    [Fact]
    public void EveryListedSite_SaysWhy()
    {
        foreach (var (site, (_, why)) in Sites)
            Assert.True(why.Length > 40, $"{site} needs a sentence saying why it may see every project");
    }

    /// <summary><c>File.Method</c> to the number of calls it holds, across the API's own source.</summary>
    private static Dictionary<string, int> FindSites()
    {
        var root = Path.Combine(ApiRoot(), "src", "Hatch.Api");
        var found = new Dictionary<string, int>();

        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//") || !Call.IsMatch(lines[i])) continue;

                var method = "(outside a method)";
                for (var j = i; j >= 0; j--)
                    if (Method.Match(lines[j]) is { Success: true } m && !lines[j].TrimStart().StartsWith("//"))
                    {
                        method = m.Groups[1].Value;
                        break;
                    }

                var key = $"{Path.GetFileNameWithoutExtension(file)}.{method}";
                found[key] = found.GetValueOrDefault(key) + Call.Matches(lines[i]).Count;
            }
        }

        return found;
    }

    private static string ApiRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "Hatch.Api", "Hatch.Api.csproj")))
                return directory.FullName;

        throw new DirectoryNotFoundException($"Could not find src/Hatch.Api above {AppContext.BaseDirectory}.");
    }
}

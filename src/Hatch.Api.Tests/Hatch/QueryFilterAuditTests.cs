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
        ["AncestorChains.ForAsync"] = (1,
            "A merge-train poll's rows and the issues above them, read a level at a time for the priority and "
            + "one-pull-request walks. A level on an ancestor in a project the caller cannot see still applies, "
            + "and a story there still holds the pull request its task waits on, so a read that left one out "
            + "would rank two callers' trains differently. It reads only the poll's own ids and their parents; "
            + "what leaves is a level, a flag and a key the route already holds, never the hidden issue's content."),
        ["Questions.WaitingLevelsAsync"] = (1,
            "The strictest open question per issue, read for the priority tree. A question on an ancestor in a "
            + "project the caller cannot see still holds the work beneath it, so a read that left it out would "
            + "honour no question there. What leaves is a level and a count, which the dispatcher folds into a "
            + "sentence naming the ancestor's key, as it does for a dangerous reason."),
        ["AnswerQuestions.ForAsync"] = (2,
            "The open question an ambition is aimed at, and the work log rows that name a question already "
            + "answered. A question on an issue in a project the caller cannot see still stands beneath the "
            + "ambition, and a session that answered it still counts, so a read that left either out would aim "
            + "two callers at different questions or at one twice. What leaves is the question's key, title and "
            + "body, and only to the caller who is dispatched the ambition above it."),
        ["WorkOutChanges.ForAsync"] = (2,
            "The epics an ambition's change is weighed against, and the capabilities each serves. An epic in a "
            + "project the caller cannot see still stands beneath the ambition, so a read that left one out would "
            + "aim two callers at different changes, or at none. What leaves is the epic's key, title, column and "
            + "the positions it serves, and only to the caller who is dispatched the ambition above it."),
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
        ["BoardHealthTrail.ReadAsync"] = (3,
            "The newest event of each issue and kind before a time, probed from Issues. The opt-out is query-wide, so "
            + "the probe reaches issues in projects the caller cannot see; it returns only the instants, which the method "
            + "keeps in memory, and the rows at those instants are then read through the caller's own filter, so what "
            + "leaves is the same as the filtered read it replaced. The filter in the probe would join Issues again for "
            + "every issue and kind and so cost a read of Issues per probe. Three calls, one reason: the probe is "
            + "written in two shapes so that a read with no closing time sends the statement it always did, and the "
            + "one with a closing time (HA-2784) adds the first event after it, which is again an instant and nothing "
            + "else."),
        ["Dispatch.ForAsync"] = (4,
            "The dependency gate and the priority tree it, the family gate, the work-in-progress count and every "
            + "projection lean on. A blocker in a project the caller cannot see still blocks, a level on an ancestor "
            + "there still applies, and a hidden issue missing from the tree would read as unpaused and unblocked. "
            + "The dispatcher reads the tree whole, whatever the calling key's owner may see; the projections read "
            + "it whole too, with the reader's own scope laid over it, so what leaves is a level or a flag that a "
            + "value came from outside the scope, never a hidden key. The family gate is the fourth: a child in a "
            + "project the caller cannot see still counts as open, so an epic is not closed over it. What leaves is "
            + "a count and an id."),
        ["Preemption.AnyOpenEmergencyAsync"] = (2,
            "Whether any open issue is at emergency level, on its own row or an ancestor's: the emergency rows, "
            + "then their children a generation at a time. A level on an ancestor in a project the caller cannot "
            + "see still applies to the work beneath it, as the priority tree the scan builds reads it, so a "
            + "filtered read could say no where the scan would have found an emergency issue. What leaves is one "
            + "bool, no row and no key."),
        ["Dispatch.SettledPassesAsync"] = (2,
            "The family gate's read of which ambitions have had a pass that nothing has since opened room for: "
            + "the work log and the events of the ambitions and their direct epics. An epic in a project the caller "
            + "cannot see still opens or fails to open its ambition's room, so a read that left it out would fold "
            + "or clear the ambition differently for two callers. What leaves is an id and the time a pass ended, "
            + "folded into the one sentence every caller is told."),
        ["ProjectRepositoryBinding.WinnersAsync"] = (1,
            "Which project a repository belongs to. The winner is the same project whoever asks, or two callers "
            + "would file the same outage's bug under different projects. What leaves is an id and a key."),
        ["ChangelogFiles.ProjectAsync"] = (3,
            "The changelog file, which is the same bytes for every caller - a trunk's own file is judged against "
            + "it, and a scoped caller who got fewer entries would see a file forever behind. It reads every "
            + "project's repositories, closed issues and tree, and returns the entries of a file."),
        ["AmbitionBudget.ReadAsync"] = (2,
            "What an ambition has cost: its tree and the work log of every issue in it. A budget is a fact about "
            + "the ambition, not about who asks, so a scoped caller whose scope hides one of its epics must read "
            + "the same figure as everyone else - and the later refusal that stops work on it must not be talked "
            + "past by a narrower read. What leaves is a sum, a count and a flag; no row and no key does."),
        ["ColumnNormals.ReadAsync"] = (2,
            "Every issue and every status change, to work out how long a column's stays normally last. A column "
            + "is shared by every project, as its WIP count is, so a scoped reader must get the same normal as "
            + "anyone else - and the first reader to ask fills the cache for everyone. What leaves is a duration, "
            + "never a row."),
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
        ["ChangelogMessages.ContentAsync"] = (1,
            "The key of the project an issue belongs to, read so the message can name and link the issue. A "
            + "column made terminal loads no project, and a patch that moves one changes it after the issue was "
            + "loaded; the writer's scope may hide the project, and the message must name the same key whoever "
            + "closed the issue. What leaves is one key string, no row."),
        ["ChatOutbox.ChangeAsync"] = (1,
            "The rows already queued for a thing, read so a change edits them instead of adding a second. Who "
            + "hears a thing is the rooms' to say and not the writer's, so a writer whose scope hides the project "
            + "must still find the rows its own earlier write queued, or a repeat would add a duplicate. Nothing "
            + "leaves: the rows are edited in place and never returned."),
        ["ChatOutbox.RoomsAsync"] = (3,
            "The rooms that carry a project, read with the bindings and the rooms' chosen projects. A room is the "
            + "install's and not the writer's, so a scoped writer must queue for the same rooms as anyone else, or "
            + "the same change would reach different rooms by who made it. What leaves is a room id and its "
            + "workspace id, which become the queued row's."),
        ["FindingMessages.LinksAsync"] = (1,
            "The key of the project a finding belongs to, read so the finding's own issue link can be checked "
            + "against it. A finding is the install's and not the writer's, and a job's context reads every "
            + "project anyway, so a later caller must build the same message a job would. What leaves is a key "
            + "that is only ever shown in a message to a room, never returned to the caller."),
        ["TrunkBuildMessages.QueueAsync"] = (1,
            "The ids of the projects that bind a repository, read so a failing trunk reaches every room that "
            + "carries any of them. A repository's trunk is nobody's issue and the rooms are the install's, so a "
            + "scoped writer must reach the same rooms anyone else would - the rule the trunk row's own visibility "
            + "follows. What leaves is a list of project ids, handed to the queue and never returned to the caller."),
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

using System.Text;
using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="PlaybookFile.Parse"/> against the file's own schema: the epic's
/// example (HA-423, "Architecture &gt; The file"), the block-scalar round trip,
/// and one test per refusal HA-459's AC1 names. No <see cref="HatchContext"/> -
/// <see cref="Parse"/> is a pure function of bytes and an already-loaded status
/// list, the way <see cref="ColumnsTests"/> tests a static helper with no rows
/// of its own.
/// </summary>
public class PlaybookFileTests
{
    private static List<EfHatchStatus> Statuses() =>
    [
        new EfHatchStatus { Id = 1, Name = "Breakdown", SortOrder = 10 },
        new EfHatchStatus { Id = 2, Name = "Backlog", SortOrder = 20 },
        new EfHatchStatus { Id = 3, Name = "In Progress", SortOrder = 30 },
        new EfHatchStatus { Id = 4, Name = "Review", SortOrder = 40 },
        new EfHatchStatus { Id = 5, Name = "Done", SortOrder = 50, IsTerminal = true },
    ];

    /// <summary>
    /// A C# raw string literal carries no line break after its last line of
    /// content, where a file on disk always does - padded back on here so a
    /// fixture behaves like the file it stands in for, rather than exercising
    /// an end-of-document edge case no editor produces.
    /// </summary>
    private static (IReadOnlyList<PlaybookCreateRequest>? Rows, string? Problem) Parse(
        string yaml, List<EfHatchStatus>? statuses = null, ModelFamilies? families = null) =>
        PlaybookFile.Parse(Encoding.UTF8.GetBytes(yaml.EndsWith('\n') ? yaml : yaml + "\n"), statuses ?? Statuses(), families);

    // ---- The epic's own example ----

    [Fact]
    public void TheEpicsOwnExample_ParsesToTheSameRowAPlaybookCreateRequestWould()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
                types: [epic]
                shape: any
                model: fable
                effort: max
                budget: 5
                briefLimit: 6000
                prompt: |
                  You are turning a captured idea into an epic...
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(problem);
        var row = Assert.Single(rows!);

        var expected = new PlaybookCreateRequest(
            1, 2, ["epic"], "You are turning a captured idea into an epic...\n", "fable", "max",
            "5", "6000", "any", null, null, null);

        Assert.Equal(expected.FromStatusId, row.FromStatusId);
        Assert.Equal(expected.ToStatusId, row.ToStatusId);
        Assert.Equal(expected.Types, row.Types);
        Assert.Equal(expected.Prompt, row.Prompt);
        Assert.Equal(expected.Model, row.Model);
        Assert.Equal(expected.Effort, row.Effort);
        Assert.Equal(expected.Budget, row.Budget);
        Assert.Equal(expected.BriefLimit, row.BriefLimit);
        Assert.Equal(expected.Shape, row.Shape);
        Assert.Equal(expected.DeniedTools, row.DeniedTools);
        Assert.Null(row.ProjectId);
        Assert.Null(row.IsCheck);
    }

    [Fact]
    public void TheBlockScalarPrompt_ComesThroughByteForByte()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
                prompt: |
                  Line one.
                  Line two, with a "quote" and a colon: still one string.

                  A blank line above, kept.
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(problem);
        Assert.Equal(
            "Line one.\nLine two, with a \"quote\" and a colon: still one string.\n\nA blank line above, kept.\n",
            Assert.Single(rows!).Prompt);
    }

    // ---- playbooks: [] and the top-level shape ----

    [Fact]
    public void AnEmptyList_IsValid_AndProducesNoRows()
    {
        var (rows, problem) = Parse("playbooks: []");

        Assert.Null(problem);
        Assert.Empty(rows!);
    }

    [Fact]
    public void PlaybooksMissing_IsRefused()
    {
        var (rows, problem) = Parse("foo: bar");

        Assert.Null(rows);
        Assert.Equal("needs a top-level \"playbooks\" key holding a list", problem);
    }

    [Fact]
    public void PlaybooksNotAList_IsRefused()
    {
        var (rows, problem) = Parse("playbooks: nope");

        Assert.Null(rows);
        Assert.Contains("needs a top-level \"playbooks\" key holding a list", problem);
    }

    [Fact]
    public void ASecondKeyBesidePlaybooks_IsRefused_NamingTheLineItIsOn()
    {
        var (rows, problem) = Parse("playbooks: []\nextra: 1");

        Assert.Null(rows);
        Assert.Equal("a playbooks file has one key, \"playbooks\" - not \"extra\" too (line 2)", problem);
    }

    [Fact]
    public void AColumnNoColumnHas_NamesTheLineTheEntryIsOn()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Nowhere
                prompt: hi
            """;

        var (_, problem) = Parse(yaml);

        Assert.Contains("(line 3)", problem);
    }

    // ---- File-level: size, encoding, syntax ----

    [Fact]
    public void OverTheSizeLimit_IsRefused()
    {
        var content = Encoding.UTF8.GetBytes("playbooks: []\n# " + new string('a', 512 * 1024));

        var (rows, problem) = PlaybookFile.Parse(content, Statuses());

        Assert.Null(rows);
        Assert.Equal("is larger than 512 KB", problem);
    }

    [Fact]
    public void NotValidUtf8_IsRefused()
    {
        byte[] content = [0x70, 0x6c, 0x61, 0xff, 0xfe];

        var (rows, problem) = PlaybookFile.Parse(content, Statuses());

        Assert.Null(rows);
        Assert.Equal("is not valid UTF-8", problem);
    }

    [Fact]
    public void NotValidYaml_IsRefused_WithTheLineYamlDotNetReports()
    {
        const string yaml = """
            playbooks:
              - from: [unterminated
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains("is not valid YAML", problem);
        Assert.Contains("(line ", problem);
    }

    // ---- Per entry: columns, from == to, duplicates ----

    [Fact]
    public void AColumnNoColumnHas_IsRefused()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Nowhere
                prompt: hi
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains("entry 1", problem);
        Assert.Contains("there is no column named \"Nowhere\"", problem);
    }

    [Fact]
    public void TwoColumnsSharingAName_IsRefused()
    {
        var statuses = new List<EfHatchStatus>
        {
            new() { Id = 1, Name = "To-Do", SortOrder = 10 },
            new() { Id = 2, Name = "ToDo", SortOrder = 20 },
        };

        const string yaml = """
            playbooks:
              - from: "to do"
                to: ToDo
                prompt: hi
            """;

        var (rows, problem) = Parse(yaml, statuses);

        Assert.Null(rows);
        Assert.Contains("entry 1", problem);
        Assert.Contains("two columns are both called \"to do\"", problem);
    }

    [Fact]
    public void ColumnsInAnotherAlphabet_AreMatchedOnTheirOwnLetters()
    {
        var statuses = new List<EfHatchStatus>
        {
            new() { Id = 1, Name = "В работе", SortOrder = 10 },
            new() { Id = 2, Name = "Готово", SortOrder = 20 },
            new() { Id = 3, Name = "faire", SortOrder = 30 },
            new() { Id = 4, Name = "À faire", SortOrder = 40 },
        };

        const string yaml = """
            playbooks:
              - from: в работе
                to: Готово
                prompt: hi
              - from: à faire
                to: faire
                prompt: hi
            """;

        var (rows, problem) = Parse(yaml, statuses);

        Assert.Null(problem);
        Assert.Equal([(1, 2), (4, 3)], rows!.Select(r => (r.FromStatusId, r.ToStatusId)));
    }

    [Fact]
    public void AColumnNamedOnlyInPunctuation_NamesNoColumn()
    {
        var statuses = new List<EfHatchStatus>
        {
            new() { Id = 1, Name = "Breakdown", SortOrder = 10 },
            new() { Id = 2, Name = "***", SortOrder = 20 },
        };

        const string yaml = """
            playbooks:
              - from: Breakdown
                to: "--"
                prompt: hi
            """;

        var (rows, problem) = Parse(yaml, statuses);

        Assert.Null(rows);
        Assert.Contains("there is no column named \"--\"", problem);
    }

    [Fact]
    public void FromEqualsTo_OutsideTheReviewColumn_IsRefused()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Breakdown
                prompt: hi
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains("entry 1", problem);
        Assert.Contains("a playbook moves an issue between two columns", problem);
    }

    [Fact]
    public void FromEqualsTo_OnTheReviewColumn_IsAllowed()
    {
        const string yaml = """
            playbooks:
              - from: Review
                to: Review
                prompt: hi
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(problem);
        Assert.Single(rows!);
    }

    [Fact]
    public void TwoEntries_WithTheSameTransitionTypesAndShape_AreRefused()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
                prompt: hi
              - from: Breakdown
                to: Backlog
                prompt: hi again
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains("entry 2", problem);
        Assert.Contains("there is already a playbook for that transition, those types and that shape", problem);
    }

    // ---- The file's own schema ----

    [Fact]
    public void AKeyNoRowHas_IsRefused()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
                prompt: hi
                efort: high
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains("entry 1", problem);
        Assert.Contains("\"efort\" is not a playbook field", problem);
    }

    [Fact]
    public void AProjectIdKey_IsRefused_TheFileNeverNamesAProject()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
                prompt: hi
                projectId: 1
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains("\"projectId\" is not a playbook field", problem);
    }

    // ---- The lifted field rules, wired through the file ----

    [Fact]
    public void ABadModel_IsRefusedInThePlaybooksPagesOwnWords()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
                prompt: hi
                model: nonsense-1
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains(
            "a model is one of haiku, sonnet, opus, fable, or a pinned id of a supported agent " +
            "(claude-opus-5 for claude; gpt-6-luna for codex; grok-4.7 for grok) - not \"nonsense-1\"",
            problem);
    }

    [Fact]
    public void APinnedId_OfASecondFamily_IsAcceptedWhenItsRowIsListed()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
                prompt: hi
                model: fake-1
            """;

        var families = new ModelFamilies([
            ModelFamilies.Default.Rows[0],
            new ModelFamily("fake", "^fake-[a-z0-9-]+$", "fake-1"),
        ]);

        var (rows, problem) = Parse(yaml, families: families);

        Assert.Null(problem);
        Assert.Equal("fake-1", Assert.Single(rows!).Model);
    }

    [Fact]
    public void ABadEffort_IsRefusedTheSameWay()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
                prompt: hi
                effort: ludicrous
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains("an effort is one of low, medium, high, xhigh, max - not \"ludicrous\"", problem);
    }

    [Fact]
    public void AMissingPrompt_IsRefusedTheSameWay()
    {
        const string yaml = """
            playbooks:
              - from: Breakdown
                to: Backlog
            """;

        var (rows, problem) = Parse(yaml);

        Assert.Null(rows);
        Assert.Contains("a playbook needs a prompt - it is the whole instruction the agent gets", problem);
    }
}

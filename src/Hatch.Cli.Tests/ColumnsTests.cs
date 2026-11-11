namespace Hatch.Cli.Tests;

/// <summary>
/// Finding a column by what it is called: the loose match on the letters and
/// digits, in any alphabet, and never a guess between two columns that read
/// alike.
/// </summary>
public sealed class ColumnsTests
{
    private static List<StatusDto> Board(params string[] names) =>
        names.Select((name, i) => Fixtures.Status(i + 1, name)).ToList();

    [Fact]
    public void Find_IgnoresCaseSpacesAndPunctuation_SoTodoReachesToDo()
    {
        var statuses = Board("Backlog", "To Do", "In Progress");

        Assert.Equal("To Do", Columns.Find(statuses, "todo")?.Name);
        Assert.Equal("In Progress", Columns.Find(statuses, "in-progress")?.Name);
    }

    [Fact]
    public void Find_MatchesACyrillicColumn_AndNotItsCyrillicNeighbour()
    {
        var statuses = Board("В работе", "Готово");

        Assert.Equal("Готово", Columns.Find(statuses, "Готово")?.Name);
        Assert.Equal("Готово", Columns.Find(statuses, "готово")?.Name);
        Assert.Equal("В работе", Columns.Find(statuses, "в работе")?.Name);
    }

    [Fact]
    public void Find_KeepsAnAccentedLetter_SoAFaireAndFaireAreTwoColumns()
    {
        var statuses = Board("faire", "À faire");

        Assert.Equal("À faire", Columns.Find(statuses, "à faire")?.Name);
        Assert.Equal("faire", Columns.Find(statuses, "faire")?.Name);
    }

    [Theory]
    [InlineData("!")]
    [InlineData("--")]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_NamesNothing_ForAWantWithNoLettersOrDigits(string want)
    {
        // A column with no letters or digits in its name folds to nothing too,
        // and nothing is not a name either of them is called.
        var statuses = Board("To Do", "***");

        Assert.Null(Columns.Find(statuses, want));
    }

    [Fact]
    public void Find_IsNull_WhereTwoColumnsReadAlike_AndMissingSaysWhich()
    {
        var statuses = Board("To-Do", "ToDo", "Done");

        Assert.Null(Columns.Find(statuses, "to do"));
        Assert.Equal(
            "\"to do\" could be any of To-Do, ToDo - rename one of them on the board",
            Columns.Missing(statuses, "to do"));
    }

    [Fact]
    public void Missing_NamesTheColumnsThereAre_WhereNoneMatches()
    {
        Assert.Equal(
            "no column called \"elsewhere\" - there is To Do, Done",
            Columns.Missing(Board("To Do", "Done"), "elsewhere"));
    }

    [Fact]
    public void AwaitingReview_IsTheColumnLeftOfTheFirstTerminalOne()
    {
        var statuses = new List<StatusDto>
        {
            Fixtures.Status(4, "Done", terminal: true), Fixtures.Status(1, "Backlog"), Fixtures.Status(3, "In Review"),
            Fixtures.Status(2, "In Progress"),
        };

        Assert.Equal("In Review", Columns.AwaitingReview(statuses)?.Name);
    }

    [Fact]
    public void AwaitingReview_StepsLeftOverProtectedColumns_AndOverNothingElse()
    {
        var statuses = new List<StatusDto>
        {
            Fixtures.Status(1, "Backlog"), Fixtures.Status(2, "In Review"),
            Fixtures.Status(3, "Approved") with { IsProtected = true },
            Fixtures.Status(4, "Merging") with { IsProtected = true },
            Fixtures.Status(5, "Done", terminal: true),
        };

        Assert.Equal("In Review", Columns.AwaitingReview(statuses)?.Name);
    }

    [Fact]
    public void AwaitingReview_LeavesDeferredColumnsOffTheBoard()
    {
        var statuses = new List<StatusDto>
        {
            Fixtures.Status(1, "Backlog"), Fixtures.Status(2, "In Review"), Fixtures.Status(3, "Parked", deferred: true),
            Fixtures.Status(4, "Done", terminal: true),
        };

        Assert.Equal("In Review", Columns.AwaitingReview(statuses)?.Name);
    }

    [Fact]
    public void AwaitingReview_IsTheRightmostColumn_WhereNoneIsTerminal()
    {
        Assert.Equal("In Review", Columns.AwaitingReview(Board("Backlog", "In Progress", "In Review"))?.Name);
    }

    [Fact]
    public void AwaitingReview_IsNull_WhereNothingIsLeftOfTheTerminalColumn()
    {
        Assert.Null(Columns.AwaitingReview([Fixtures.Status(1, "Done", terminal: true)]));
        Assert.Null(Columns.AwaitingReview([]));
    }
}

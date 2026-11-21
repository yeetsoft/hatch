using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="EfHatchIssue.Retitle"/> and <see cref="EfHatchIssue.PreviousTitlesNewestFirst"/>
/// in isolation, with no database - the write path itself is
/// <see cref="IssuesControllerTests"/>'s business.
/// </summary>
public class IssueRetitleTests
{
    [Fact]
    public void ANeverRetitledIssue_HasNoPreviousTitles()
    {
        var issue = Issue("the gasket");

        Assert.Empty(issue.PreviousTitlesNewestFirst());
    }

    [Fact]
    public void Retitle_SetsTheTitleAndKeepsTheOldOne()
    {
        var issue = Issue("the gasket");

        issue.Retitle("the sprocket");

        Assert.Equal("the sprocket", issue.Title);
        Assert.Equal(["the gasket"], issue.PreviousTitlesNewestFirst());
    }

    [Fact]
    public void ARoundTrip_LeavesOnlyTheTitleInBetween()
    {
        var issue = Issue("A");

        issue.Retitle("B");
        issue.Retitle("A");

        Assert.Equal("A", issue.Title);
        Assert.Equal(["B"], issue.PreviousTitlesNewestFirst());
    }

    [Fact]
    public void FourTitles_KeepTheTwoNotCurrent_NewestFirst()
    {
        var issue = Issue("A");

        issue.Retitle("B");
        issue.Retitle("A");
        issue.Retitle("C");

        Assert.Equal("C", issue.Title);
        Assert.Equal(["A", "B"], issue.PreviousTitlesNewestFirst());
    }

    [Fact]
    public void ALineBreakInTheOldTitle_IsFoldedToASpace()
    {
        var issue = Issue("the gasket\nand the sprocket");

        issue.Retitle("something else");

        Assert.Equal(["the gasket and the sprocket"], issue.PreviousTitlesNewestFirst());
    }

    private static EfHatchIssue Issue(string title) => new()
    {
        ProjectId = 1,
        Number = 1,
        Type = "story",
        Title = title,
        StatusId = 1,
        Rank = 1024,
        CreatedBy = "operator",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}

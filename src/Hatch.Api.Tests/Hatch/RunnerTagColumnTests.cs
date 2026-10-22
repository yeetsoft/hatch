using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

public class RunnerTagColumnTests
{
    [Fact]
    public void AFreshlyConstructedIssueDefaultsToAnEmptyArray()
    {
        var now = DateTimeOffset.UtcNow;
        var issue = new EfHatchIssue
        {
            ProjectId = 1,
            Number = 1,
            Type = "task",
            Title = "a task",
            StatusId = 1,
            Rank = 100,
            CreatedBy = "hatch",
            CreatedAt = now,
            UpdatedAt = now,
        };

        Assert.Equal("[]", issue.RunnerTags);
        Assert.Empty(RunnerTagColumn.Read("[]"));
    }

    [Fact]
    public void ReadOfWriteKeepsOrder()
    {
        var stored = RunnerTagColumn.Write(["a", "b"]);

        Assert.Equal(["a", "b"], RunnerTagColumn.Read(stored));
    }

    [Fact]
    public void WriteOfNoTagsIsAnEmptyArray()
    {
        Assert.Equal("[]", RunnerTagColumn.Write([]));
    }
}

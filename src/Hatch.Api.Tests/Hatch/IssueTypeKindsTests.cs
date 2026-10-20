using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one predicate that says which issue types run children the way an
/// epic does: <see cref="IssueTypeKinds.RunsChildren"/>.
/// </summary>
public class IssueTypeKindsTests
{
    [Theory]
    [InlineData("epic", true)]
    [InlineData("reflection", true)]
    [InlineData("story", false)]
    [InlineData("task", false)]
    [InlineData("bug", false)]
    [InlineData("nonsense", false)]
    public void RunsChildren_MatchesEpicAndReflectionOnly(string type, bool expected)
    {
        Assert.Equal(expected, IssueTypeKinds.RunsChildren(type));
    }
}

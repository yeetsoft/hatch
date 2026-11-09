using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The version rule and the satisfaction test, pinned once here so the
/// detector and the routes read the same answer.
/// </summary>
public class DependencyVersionTests
{
    [Theory]
    [InlineData("10")]
    [InlineData("10.0")]
    [InlineData("10.0.100")]
    public void ValidVersionsHaveNoProblem(string version)
    {
        Assert.Null(DependencyVersion.Problem(version));
    }

    [Theory]
    [InlineData("")]
    [InlineData("10.")]
    [InlineData(".10")]
    [InlineData("10.0.0.0")]
    [InlineData("v10")]
    [InlineData("10.x")]
    [InlineData("-1")]
    [InlineData(" 10")]
    [InlineData("1234567890")]
    public void InvalidVersionsNameTheRule(string version)
    {
        Assert.Equal(
            $"a version is one to three dotted whole numbers, like 10 or 10.0.100 - not \"{version}\"",
            DependencyVersion.Problem(version));
    }

    [Theory]
    [InlineData("10", "10.0.100", true)]
    [InlineData("10", "10", true)]
    [InlineData("22.11", "22.10.0", false)]
    [InlineData("22.11", "22.11.3", true)]
    [InlineData("7", null, false)]
    [InlineData("10.0", "10", false)]
    [InlineData("1", "10.0", true)]
    [InlineData("10", "10.0.100.5", true)]
    [InlineData("10", "abc", false)]
    [InlineData(null, "1.2.3", true)]
    [InlineData(null, null, true)]
    [InlineData(null, "abc", true)]
    [InlineData("10", "010.1", true)]
    [InlineData("10", "99999999999999999999", true)]
    [InlineData("", "10", false)]
    [InlineData("v10", "10", false)]
    [InlineData("10", "10..5", true)]
    [InlineData("10.5", "10..5", false)]
    [InlineData("10", ".5", false)]
    [InlineData("10", "", false)]
    [InlineData("0", "000", true)]
    [InlineData("22", "22", true)]
    [InlineData("22", "22.0.1", true)]
    [InlineData("22", "24.22.0", true)]
    [InlineData("22", "21.9.9", false)]
    [InlineData("10.0.100", "10.0.302", true)]
    [InlineData("10.0.100", "11.0.100", true)]
    [InlineData("10.0.100", "10.0.99", false)]
    [InlineData("7", "7.6.9", true)]
    [InlineData("22.1", "22", false)]
    [InlineData("10", null, false)]
    public void SatisfiesTreatsADeclaredVersionAsAMinimum(string? declared, string? detected, bool expected)
    {
        Assert.Equal(expected, DependencyVersion.Satisfies(declared, detected));
    }

    [Theory]
    [InlineData("10", "10.0.100", true)]
    [InlineData("22.11", "22.10.0", false)]
    [InlineData("1", "10.0", false)]
    [InlineData("10", "99999999999999999999", false)]
    [InlineData("10.0", "10", false)]
    [InlineData(null, "abc", true)]
    [InlineData("7", null, false)]
    public void MatchesReadsComponentsAsAnExactPrefix(string? declared, string? detected, bool expected)
    {
        Assert.Equal(expected, DependencyVersion.Matches(declared, detected));
    }
}

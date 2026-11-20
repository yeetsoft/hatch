using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="Principles.Read"/>'s reading of a description's
/// <c>## Principles</c> heading, in isolation from the ambition and the
/// dispatch that carry it - see <c>WorkControllerTests</c> for those.
/// </summary>
public class PrinciplesTests
{
    [Fact]
    public void AHeading_FollowedByProseThenBullets_ReadsOnlyTheBullets_InOrder()
    {
        var description = """
            Some prose up top, about nothing in particular.

            ## Principles

            A sentence before the bullets start.

            - per-node, not per-run
            - least surprise over least typing

            ## Something else

            - not a principle
            """;

        Assert.Equal(["per-node, not per-run", "least surprise over least typing"], Principles.Read(description));
    }

    [Fact]
    public void AHeading_AtTheVeryEndOfTheDescription_StillReadsItsBullets()
    {
        var description = """
            Some prose.

            ## Principles
            - one thing at a time
            - say why, not just what
            """;

        Assert.Equal(["one thing at a time", "say why, not just what"], Principles.Read(description));
    }

    [Fact]
    public void NoPrinciplesHeadingAnywhere_ReadsAsEmpty()
    {
        var description = """
            # An ambition

            Just prose, and a different heading.

            ## Non-goals

            - not this
            """;

        Assert.Empty(Principles.Read(description));
    }

    [Fact]
    public void ABulletCarryingInlineCode_IsKeptVerbatim()
    {
        var description = """
            ## Principles

            - use `per-node` state, never `per-run`
            """;

        Assert.Equal(["use `per-node` state, never `per-run`"], Principles.Read(description));
    }

    // ---- Append ----

    [Fact]
    public void ASectionInTheMiddle_GetsTheBulletAfterTheLastOne()
    {
        var description = """
            Some prose up top.

            ## Principles

            - per-node, not per-run

            ## Non-goals

            - not this
            """;

        var appended = Principles.Append(description, "least surprise over least typing");

        Assert.Equal(
            ["per-node, not per-run", "least surprise over least typing"],
            Principles.Read(appended));
        Assert.Contains("## Non-goals", appended);
        Assert.Contains("- not this", appended);
    }

    [Fact]
    public void ASectionAtTheEnd_GetsTheBulletAfterTheLastOne()
    {
        var description = """
            Some prose.

            ## Principles
            - one thing at a time
            - say why, not just what
            """;

        var appended = Principles.Append(description, "least surprise over least typing");

        Assert.Equal(
            ["one thing at a time", "say why, not just what", "least surprise over least typing"],
            Principles.Read(appended));
    }

    [Fact]
    public void AHeadingWithNoBullets_GetsTheBulletRightAfterIt()
    {
        var description = """
            ## Principles

            ## Non-goals

            - not this
            """;

        var appended = Principles.Append(description, "say why, not just what");

        Assert.Equal(["say why, not just what"], Principles.Read(appended));
        Assert.Contains("- not this", appended);
    }

    [Fact]
    public void NoHeadingAnywhere_AppendsOneAtTheEnd()
    {
        var description = """
            # An ambition

            Just prose, no heading of its own.
            """;

        var appended = Principles.Append(description, "say why, not just what");

        Assert.Equal(["say why, not just what"], Principles.Read(appended));
        Assert.Contains("Just prose, no heading of its own.", appended);
    }

    [Fact]
    public void AnEmptyDescription_GetsTheHeadingWithNoLeadingBlankLines()
    {
        var appended = Principles.Append("", "say why, not just what");

        Assert.Equal("## Principles\n\n- say why, not just what", appended);
    }

    [Fact]
    public void ACrlfDescription_IsRewrittenWithCrlf()
    {
        var description = "## Principles\r\n\r\n- one thing at a time";

        var appended = Principles.Append(description, "say why, not just what");

        Assert.Equal("## Principles\r\n\r\n- one thing at a time\r\n- say why, not just what", appended);
    }

    // ---- Contains ----

    [Fact]
    public void Contains_ComparesTrimmedAndWithoutCase()
    {
        var description = """
            ## Principles

            - least surprise over least typing
            """;

        Assert.True(Principles.Contains(description, "  LEAST SURPRISE OVER LEAST TYPING  "));
        Assert.False(Principles.Contains(description, "something else entirely"));
    }
}

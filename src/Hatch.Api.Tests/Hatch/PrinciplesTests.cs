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
}

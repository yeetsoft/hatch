using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a typed search becomes on its way to Postgres. No database: the rules
/// are about the string, and the string is what reaches <c>to_tsquery</c>.
/// </summary>
public class SearchQueryTests
{
    [Fact]
    public void Terms_ArePrefixedAndAnded()
    {
        Assert.Equal("drill:* & garage:*", SearchQuery.ToTsQuery("drill garage"));
    }

    [Fact]
    public void AnyWord_OrsTheTermsInstead()
    {
        Assert.Equal("drill:* | garage:*", SearchQuery.ToTsQuery("drill garage", anyWord: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ()")]
    public void BlankInput_GivesNull_WhichMeansNoQueryRatherThanNoResults(string? input)
    {
        Assert.Null(SearchQuery.ToTsQuery(input));
        Assert.Null(SearchQuery.ToTsQuery(input, anyWord: true));
    }

    [Fact]
    public void Operators_AreStripped_SoNoneReachesTheParser()
    {
        Assert.Equal("drill:* & garage:*", SearchQuery.ToTsQuery("drill!:* & (garage) | "));
    }

    [Fact]
    public void Terms_AreLowerCasedAndDeduplicated()
    {
        Assert.Equal("drill:*", SearchQuery.ToTsQuery("Drill DRILL drill"));
    }

    [Fact]
    public void TermsPastTheCap_AreDropped()
    {
        var input = string.Join(' ', Enumerable.Range(1, SearchQuery.MaxTerms + 4).Select(n => $"w{n}"));

        Assert.Equal(SearchQuery.MaxTerms, SearchQuery.ToTsQuery(input)!.Split(" & ").Length);
    }
}

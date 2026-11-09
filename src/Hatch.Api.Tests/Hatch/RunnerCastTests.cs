using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="RunnerCast.Suggest"/>, pinned once here so the API and the CLI
/// both inherit it rather than re-deriving it.
/// </summary>
public class RunnerCastTests
{
    [Fact]
    public void Suggest_returns_the_first_untaken_name_case_insensitively()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RunnerCast.All[0].ToUpperInvariant() };

        var suggested = RunnerCast.Suggest(taken);

        Assert.Equal(RunnerCast.All[1], suggested);
    }

    [Fact]
    public void Suggest_falls_back_to_the_first_name_with_a_suffix_once_every_name_is_taken()
    {
        var taken = new HashSet<string>(RunnerCast.All, StringComparer.OrdinalIgnoreCase);

        var suggested = RunnerCast.Suggest(taken);

        Assert.Equal($"{RunnerCast.All[0]} 2", suggested);
    }

    [Fact]
    public void Suggest_moves_to_the_next_suffix_once_the_previous_one_is_also_taken()
    {
        var taken = new HashSet<string>(RunnerCast.All, StringComparer.OrdinalIgnoreCase);
        var first = RunnerCast.Suggest(taken);
        taken.Add(first);

        var second = RunnerCast.Suggest(taken);

        Assert.Equal($"{RunnerCast.All[0]} 3", second);
    }

    [Fact]
    public void All_is_non_empty_with_no_two_entries_equal_case_insensitively()
    {
        Assert.NotEmpty(RunnerCast.All);
        Assert.Equal(RunnerCast.All.Count, RunnerCast.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

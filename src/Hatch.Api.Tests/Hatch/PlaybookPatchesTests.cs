using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What <see cref="PlaybookPatches.Apply"/> resolves, with no database: it is
/// pure, so a state and a request are all it needs. The refusals are
/// <see cref="PlaybooksControllerTests"/>'s business, through the route.
/// </summary>
public class PlaybookPatchesTests
{
    private static PlaybookState State(string? prompt = "custom", string? defaultPrompt = "prescribed") => new(
        FromStatusId: 1, ToStatusId: 2, Types: "task", Shape: "any",
        Prompt: prompt, DefaultPrompt: defaultPrompt, Model: "sonnet", Effort: "medium",
        Budget: 5, BriefLimit: null, DeniedTools: null, IsCheck: false, ProjectId: null);

    private static PlaybookPatchRequest Patch(string? prompt = null, string? model = null) =>
        new(FromStatusId: null, ToStatusId: null, Types: null, Prompt: prompt, Model: model, Effort: null);

    [Fact]
    public void A_prompt_equal_to_the_prescribed_one_clears_the_override()
    {
        var next = PlaybookPatches.Apply(State(), Patch(prompt: "  prescribed  "));

        Assert.Null(next.Prompt);
        Assert.Equal("prescribed", next.EffectivePrompt);
    }

    [Fact]
    public void A_blank_prompt_clears_the_override()
    {
        var next = PlaybookPatches.Apply(State(), Patch(prompt: "   "));

        Assert.Null(next.Prompt);
    }

    [Fact]
    public void A_field_left_null_in_the_request_is_untouched()
    {
        var current = State();

        var next = PlaybookPatches.Apply(current, Patch(model: "opus"));

        Assert.Equal("opus", next.Model);
        Assert.Equal(current with { Model = "opus" }, next);
    }
}

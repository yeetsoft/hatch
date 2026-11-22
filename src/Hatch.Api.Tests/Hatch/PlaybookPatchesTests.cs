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
        Budget: 5, BriefLimit: null, QuietLimitSeconds: null, DeniedTools: null, IsCheck: false, ProjectId: null);

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
    public void A_local_model_is_set_trimmed_kept_when_null_and_cleared_when_blank()
    {
        var set = PlaybookPatches.Apply(State(), new PlaybookPatchRequest(null, null, null, null, null, null, LocalModel: " fake:1b "));
        Assert.Equal("fake:1b", set.LocalModel);

        var kept = PlaybookPatches.Apply(set, Patch(model: "opus"));
        Assert.Equal("fake:1b", kept.LocalModel);

        var cleared = PlaybookPatches.Apply(set, new PlaybookPatchRequest(null, null, null, null, null, null, LocalModel: "   "));
        Assert.Null(cleared.LocalModel);
    }

    [Fact]
    public void AnswersQuestions_is_set_kept_when_null_and_cleared_when_false()
    {
        var set = PlaybookPatches.Apply(State(), new PlaybookPatchRequest(null, null, null, null, null, null, AnswersQuestions: true));
        Assert.True(set.AnswersQuestions);

        var kept = PlaybookPatches.Apply(set, Patch(model: "opus"));
        Assert.True(kept.AnswersQuestions);

        var cleared = PlaybookPatches.Apply(set, new PlaybookPatchRequest(null, null, null, null, null, null, AnswersQuestions: false));
        Assert.False(cleared.AnswersQuestions);
    }

    [Fact]
    public void WorksOutChanges_is_set_kept_when_null_and_cleared_when_false()
    {
        var set = PlaybookPatches.Apply(State(), new PlaybookPatchRequest(null, null, null, null, null, null, WorksOutChanges: true));
        Assert.True(set.WorksOutChanges);

        var kept = PlaybookPatches.Apply(set, Patch(model: "opus"));
        Assert.True(kept.WorksOutChanges);

        var cleared = PlaybookPatches.Apply(set, new PlaybookPatchRequest(null, null, null, null, null, null, WorksOutChanges: false));
        Assert.False(cleared.WorksOutChanges);
    }

    [Fact]
    public void ShapesAmbition_is_set_kept_when_null_and_cleared_when_false()
    {
        var set = PlaybookPatches.Apply(State(), new PlaybookPatchRequest(null, null, null, null, null, null, ShapesAmbition: true));
        Assert.True(set.ShapesAmbition);

        var kept = PlaybookPatches.Apply(set, Patch(model: "opus"));
        Assert.True(kept.ShapesAmbition);

        var cleared = PlaybookPatches.Apply(set, new PlaybookPatchRequest(null, null, null, null, null, null, ShapesAmbition: false));
        Assert.False(cleared.ShapesAmbition);
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, true, true, false, true)]
    [InlineData(false, true, false, true, true)]
    [InlineData(false, false, true, true, true)]
    [InlineData(true, true, true, true, true)]
    public void A_row_is_one_of_the_four_kinds_at_most(bool isCheck, bool answers, bool worksOut, bool shapes, bool refused)
    {
        Assert.Equal(refused, PlaybookPatches.MoreThanOneKind(isCheck, answers, worksOut, shapes));
    }

    [Fact]
    public void The_refusal_names_all_four_kinds()
    {
        Assert.Equal(
            "a playbook is a check, answers questions, works out changes or shapes an ambition - never more than one",
            PlaybookPatches.OneKindRefusal);
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

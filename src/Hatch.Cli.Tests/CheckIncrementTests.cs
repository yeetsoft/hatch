using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// A check increment: after a headless session under a check playbook ends,
/// the tree is verified and the server's verdict applied - or, where there is
/// nothing to apply or the tree moved, the increment fails through the same
/// <see cref="LetGo"/> path any other stall does. <c>work -i</c> takes the same
/// fork after its attached session, with a plain comment in place of the
/// counted-stall machinery.
/// </summary>
public sealed class CheckIncrementTests
{
    private const string Queue = "/api/work/queue";
    private const string Key = "AER-1";
    private const string Verdict = "/api/work/AER-1/verdict";

    private static readonly StatusDto FromStatus = Fixtures.Status(3, "In Review");
    private static readonly StatusDto FailTarget = Fixtures.Status(4, "In Progress");
    private static readonly StatusDto PassTarget = Fixtures.Status(5, "Done");

    private static WorkDto CheckWork(int letGo = 0) =>
        Fixtures.Work(Key, from: "In Review", letGo: letGo, isCheck: true, statuses: [FromStatus, FailTarget, PassTarget]);

    private static BranchEntry Entered(string path) =>
        new() { Path = path, Kind = BranchKind.Entered, Branch = "aer-1-thing", Sha = "abc123def", Head = "fed321cba" };

    private static void Board(Harness h, WorkDto work, IReadOnlyList<QuestionDto>? questionsAfter = null)
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row(Key) });
        h.Wire.Reply("POST", $"/api/issues/{Key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/issues/{Key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/issues/{Key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/work/{Key}", work);
        h.Wire.Json("POST", $"/api/issues/{Key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/issues/{Key}/questions", questionsAfter ?? Array.Empty<QuestionDto>());
        h.Wire.Json("POST", $"/api/issues/{Key}/comments", Fixtures.Comment());
        h.Wire.Json("PUT", $"/api/issues/{Key}/stall", Fixtures.Issue(Key));
        h.Workspace.Entry = (path, _) => Entered(path);
    }

    private static List<CommentCreateRequest> Written(Harness h) =>
        h.Wire.To("POST", $"/api/issues/{Key}/comments").Select(c => c.Read<CommentCreateRequest>()).ToList();

    // ---- Pass and first fail: the verdict moves the ticket ----

    [Fact]
    public async Task A_pass_applies_the_verdict_and_reports_the_move()
    {
        using var h = new Harness();
        Board(h, CheckWork());
        h.Wire.Json("POST", Verdict, Fixtures.Issue(Key, statusId: PassTarget.Id));

        var code = await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Equal(0, code);
        Assert.Single(h.Wire.To("POST", Verdict));
        Assert.Empty(h.Wire.To("PUT", $"/api/issues/{Key}/stall"));
        Assert.Contains(h.Say.Said, l => l == $"hatch: {Key} In Review -> Done");

        var released = Assert.Single(h.Wire.To("DELETE", $"/api/issues/{Key}/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_fail_applies_the_verdict_and_reports_the_move_back_to_implementation()
    {
        using var h = new Harness();
        Board(h, CheckWork());
        h.Wire.Json("POST", Verdict, Fixtures.Issue(Key, statusId: FailTarget.Id));

        var code = await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Equal(0, code);
        Assert.Single(h.Wire.To("POST", Verdict));
        Assert.Empty(h.Wire.To("PUT", $"/api/issues/{Key}/stall"));
        Assert.Contains(h.Say.Said, l => l == $"hatch: {Key} In Review -> In Progress");

        var released = Assert.Single(h.Wire.To("DELETE", $"/api/issues/{Key}/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    // ---- A dirty tree, or HEAD moved: the same boolean at this layer ----

    [Fact]
    public async Task A_dirty_tree_is_never_applied_and_fails_through_letgo_like_any_other_stall()
    {
        using var h = new Harness();
        Board(h, CheckWork());
        h.Workspace.TreeUnchangedAnswer = false;

        var code = await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Equal(0, code);
        Assert.Empty(h.Wire.To("POST", Verdict));
        Assert.Empty(h.Wire.To("PUT", $"/api/issues/{Key}/stall"));

        var comment = Assert.Single(Written(h));
        Assert.Contains("let it go", comment.Body, StringComparison.Ordinal);

        Assert.Contains($"leave {h.Root}", h.Workspace.Calls);
    }

    [Fact]
    public async Task A_head_that_moved_is_the_same_code_path_as_a_dirty_tree()
    {
        using var h = new Harness();
        Board(h, CheckWork());
        h.Workspace.TreeUnchangedAnswer = false;

        var code = await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Equal(0, code);
        Assert.Empty(h.Wire.To("POST", Verdict));
        var comment = Assert.Single(Written(h));
        Assert.Contains("let it go", comment.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_dirty_tree_in_a_row_is_flagged_and_marked_stalled()
    {
        using var h = new Harness();
        Board(h, CheckWork(letGo: 1));
        h.Workspace.TreeUnchangedAnswer = false;

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(h.Wire.To("POST", Verdict));
        var marked = Assert.Single(h.Wire.To("PUT", $"/api/issues/{Key}/stall"));
        Assert.Contains("left it in", marked.Read<StallRequest>().Why, StringComparison.Ordinal);
    }

    // ---- No verdict was recorded ----

    [Fact]
    public async Task A_session_that_ends_without_a_verdict_is_a_stall_and_never_moves_the_ticket()
    {
        using var h = new Harness();
        Board(h, CheckWork());
        h.Wire.Reply("POST", Verdict, HttpStatusCode.NotFound);

        var code = await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Equal(0, code);
        var comment = Assert.Single(Written(h));
        Assert.Contains("let it go", comment.Body, StringComparison.Ordinal);

        var released = Assert.Single(h.Wire.To("DELETE", $"/api/issues/{Key}/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
    }

    // ---- A held second fail: worked, because the apply call already asked its own question ----

    [Fact]
    public async Task A_held_second_fail_is_not_moved_but_counts_as_worked_because_it_already_asked()
    {
        using var h = new Harness();
        Board(h, CheckWork(letGo: 1), questionsAfter: [Fixtures.Question(9)]);
        h.Wire.Json("POST", Verdict, Fixtures.Issue(Key, statusId: FromStatus.Id));

        var code = await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Equal(0, code);
        Assert.Single(h.Wire.To("POST", Verdict));
        Assert.Empty(h.Wire.To("PUT", $"/api/issues/{Key}/stall"));
        Assert.Empty(Written(h));

        var released = Assert.Single(h.Wire.To("DELETE", $"/api/issues/{Key}/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    // ---- work -i: a plain comment either way, no LetGo or stall-counting ----

    [Fact]
    public async Task An_attached_check_session_with_a_dirty_tree_writes_a_plain_comment_and_never_applies()
    {
        using var h = new Harness();
        h.Wire.Json("GET", $"/api/work/{Key}", CheckWork());
        h.Wire.Reply("POST", $"/api/issues/{Key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/issues/{Key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/issues/{Key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("POST", $"/api/issues/{Key}/comments", Fixtures.Comment());
        h.Workspace.Entry = (path, _) => Entered(path);
        h.Workspace.TreeUnchangedAnswer = false;

        var code = await new WorkCommand(h.Runtime).RunAsync(["-i", Key], default);

        Assert.Equal(0, code);
        Assert.Single(h.Sessions.Attached);
        Assert.Empty(h.Wire.To("POST", Verdict));
        var comment = Assert.Single(Written(h));
        Assert.Contains("left the tree changed", comment.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_attached_check_session_with_a_clean_tree_applies_the_verdict()
    {
        using var h = new Harness();
        h.Wire.Json("GET", $"/api/work/{Key}", CheckWork());
        h.Wire.Reply("POST", $"/api/issues/{Key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/issues/{Key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/issues/{Key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("POST", Verdict, Fixtures.Issue(Key, statusId: PassTarget.Id));
        h.Workspace.Entry = (path, _) => Entered(path);

        var code = await new WorkCommand(h.Runtime).RunAsync(["-i", Key], default);

        Assert.Equal(0, code);
        Assert.Single(h.Sessions.Attached);
        Assert.Single(h.Wire.To("POST", Verdict));
        Assert.Empty(Written(h));
    }
}

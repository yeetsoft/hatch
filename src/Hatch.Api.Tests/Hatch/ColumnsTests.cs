using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="Columns.AwaitingReview"/> and <see cref="Columns.Target"/> measured
/// off hand-built boards - no <see cref="HatchContext"/> needed, the way
/// <see cref="IssueMomentTests"/> tests a static helper with no rows of its own.
///
/// <para>Every status below gets its own explicit, unique <see cref="EfHatchStatus.Id"/>:
/// built with no <c>SaveChangesAsync</c>, EF never assigns one, and a list where
/// every status shares the default <c>Id = 0</c> would make <see cref="Columns.Board"/>'s
/// and <see cref="Columns.Target"/>'s <c>Id</c> comparisons pass or fail for the
/// wrong reason.</para>
/// </summary>
public class ColumnsTests
{
    static EfHatchStatus S(int id, string name, int sortOrder, bool terminal = false, bool isProtected = false, bool mergesPullRequest = false, bool deferred = false) =>
        new() { Id = id, Name = name, SortOrder = sortOrder, IsTerminal = terminal, IsProtected = isProtected, MergesPullRequest = mergesPullRequest, IsDeferred = deferred };

    [Fact]
    public void PlainBoard_AwaitingReviewIsReview_ImplementationIsDoing()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var done = S(4, "Done", 40, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, done };

        Assert.Equal(review.Id, Columns.AwaitingReview(board)?.Id);
        Assert.Equal(doing.Id, Columns.Implementation(board)?.Id);
    }

    [Fact]
    public void OneProtectedColumnBeforeDone_AwaitingReviewStepsBackOverIt()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var approved = S(4, "Approved", 40, isProtected: true);
        var done = S(5, "Done", 50, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, approved, done };

        Assert.Equal(review.Id, Columns.AwaitingReview(board)?.Id);
        Assert.Equal(doing.Id, Columns.Implementation(board)?.Id);
    }

    [Fact]
    public void TwoProtectedColumnsBeforeDone_AwaitingReviewStepsBackOverBoth()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var approved = S(4, "Approved", 40, isProtected: true);
        var qa = S(5, "QA", 50, isProtected: true);
        var done = S(6, "Done", 60, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, approved, qa, done };

        Assert.Equal(review.Id, Columns.AwaitingReview(board)?.Id);
        Assert.Equal(doing.Id, Columns.Implementation(board)?.Id);
    }

    [Fact]
    public void ProtectedColumnMidBoard_MovesNothing()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20, isProtected: true);
        var review = S(3, "Review", 30);
        var done = S(4, "Done", 40, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, done };

        Assert.Equal(review.Id, Columns.AwaitingReview(board)?.Id);
        Assert.Equal(doing.Id, Columns.Implementation(board)?.Id);
    }

    [Fact]
    public void Target_FromReviewBeforeTerminal_DispatchesToItself()
    {
        var review = S(1, "Review", 10);
        var done = S(2, "Done", 20, terminal: true);
        var board = new List<EfHatchStatus> { review, done };

        Assert.Equal(review.Id, Columns.Target(board, review)?.Id);
    }

    [Fact]
    public void Target_FromReviewBeforeProtected_DispatchesToItself()
    {
        var review = S(1, "Review", 10);
        var approved = S(2, "Approved", 20, isProtected: true);
        var done = S(3, "Done", 30, terminal: true);
        var board = new List<EfHatchStatus> { review, approved, done };

        Assert.Equal(review.Id, Columns.Target(board, review)?.Id);
    }

    [Fact]
    public void Target_FromAProtectedColumn_AdvancesOrdinarilyToReview()
    {
        var doing = S(1, "Doing", 10, isProtected: true);
        var review = S(2, "Review", 20);
        var done = S(3, "Done", 30, terminal: true);
        var board = new List<EfHatchStatus> { doing, review, done };

        Assert.Equal(review.Id, Columns.Target(board, doing)?.Id);
    }

    [Fact]
    public void PlainBoard_SendBackIsTodo()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var done = S(4, "Done", 40, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, done };

        Assert.Equal(todo.Id, Columns.SendBack(board)?.Id);
    }

    [Fact]
    public void NoImplementationColumn_SendBackIsNull()
    {
        var review = S(1, "Review", 10);
        var done = S(2, "Done", 20, terminal: true);
        var board = new List<EfHatchStatus> { review, done };

        Assert.Null(Columns.Implementation(board));
        Assert.Null(Columns.SendBack(board));
    }

    [Fact]
    public void NoTickedColumn_MergesIsNull()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var done = S(4, "Done", 40, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, done };

        Assert.Null(Columns.Merges(board));
    }

    [Fact]
    public void OneTickedProtectedColumn_MergesNamesIt()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var merges = S(4, "Merges", 40, isProtected: true, mergesPullRequest: true);
        var done = S(5, "Done", 50, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, merges, done };

        Assert.Equal(merges.Id, Columns.Merges(board)?.Id);
    }

    [Fact]
    public void Approves_FlagOnFromReviewWithMergeColumnNext_NamesIt()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var merges = S(4, "Merges", 40, isProtected: true, mergesPullRequest: true);
        var done = S(5, "Done", 50, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, merges, done };

        Assert.Equal(merges.Id, Columns.Approves(board, review, true)?.Id);
    }

    [Fact]
    public void Approves_FlagOff_IsNull()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var merges = S(4, "Merges", 40, isProtected: true, mergesPullRequest: true);
        var done = S(5, "Done", 50, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, merges, done };

        Assert.Null(Columns.Approves(board, review, false));
    }

    [Fact]
    public void Approves_NoMergeColumnAfterReview_IsNull()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var done = S(4, "Done", 40, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, done };

        Assert.Null(Columns.Approves(board, review, true));
    }

    [Fact]
    public void Approves_FromNotReviewColumn_IsNull()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var merges = S(4, "Merges", 40, isProtected: true, mergesPullRequest: true);
        var done = S(5, "Done", 50, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, merges, done };

        Assert.Null(Columns.Approves(board, doing, true));
    }

    [Fact]
    public void Approves_NextColumnMergesButNotProtected_IsNull()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        // Terminal, so review stays the awaiting-review column and only the
        // protected test can refuse: an unprotected non-terminal column would
        // itself be what AwaitingReview measures.
        var done = S(4, "Done", 40, terminal: true, mergesPullRequest: true);
        var board = new List<EfHatchStatus> { todo, doing, review, done };

        Assert.Null(Columns.Approves(board, review, true));
    }

    [Fact]
    public void Approves_NextColumnProtectedButNotMerges_IsNull()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var gate = S(4, "Gate", 40, isProtected: true);
        var done = S(5, "Done", 50, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, gate, done };

        Assert.Null(Columns.Approves(board, review, true));
    }

    [Fact]
    public void RenamedBoard_SendBackIsMeasuredByPosition_NotByName()
    {
        var alpha = S(1, "Alpha", 10);
        var beta = S(2, "Beta", 20);
        var gamma = S(3, "Gamma", 30);
        var delta = S(4, "Delta", 40, terminal: true);
        var board = new List<EfHatchStatus> { alpha, beta, gamma, delta };

        // Shaped exactly like PlainBoard_SendBackIsTodo, with none of its
        // names - Implementation lands on "Beta", not on anything called
        // "Doing", and SendBack names "Alpha" the same way.
        Assert.Equal(beta.Id, Columns.Implementation(board)?.Id);
        Assert.Equal(alpha.Id, Columns.SendBack(board)?.Id);
    }

    [Fact]
    public void PlainBoard_ClosesIsDone()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var done = S(4, "Done", 40, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, done };

        Assert.Equal(done.Id, Columns.Closes(board, review)?.Id);
    }

    [Fact]
    public void OneProtectedColumnBeforeDone_ClosesStepsPastIt()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var approved = S(4, "Approved", 40, isProtected: true);
        var done = S(5, "Done", 50, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, review, approved, done };

        Assert.Equal(done.Id, Columns.Closes(board, review)?.Id);
    }

    [Fact]
    public void NoTerminalColumnAnywhere_ClosesIsNull()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var review = S(3, "Review", 30);
        var board = new List<EfHatchStatus> { todo, doing, review };

        Assert.Null(Columns.Closes(board, review));
    }

    [Fact]
    public void FromTheRightmostColumn_ClosesIsNull()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var done = S(3, "Done", 30, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, done };

        Assert.Null(Columns.Closes(board, done));
    }

    [Fact]
    public void FiveColumnBoardWithADeferredColumnMidBoard_AtOrPastImplementationIsMeasuredOffTheBoard()
    {
        var todo = S(1, "To Do", 10);
        var doing = S(2, "Doing", 20);
        var parked = S(3, "Parked", 30, deferred: true);
        var review = S(4, "Review", 40);
        var done = S(5, "Done", 50, terminal: true);
        var board = new List<EfHatchStatus> { todo, doing, parked, review, done };

        Assert.False(Columns.AtOrPastImplementation(board, todo.Id));
        Assert.True(Columns.AtOrPastImplementation(board, doing.Id));
        Assert.False(Columns.AtOrPastImplementation(board, parked.Id));
        Assert.True(Columns.AtOrPastImplementation(board, review.Id));
        Assert.True(Columns.AtOrPastImplementation(board, done.Id));

        Assert.Equal(new HashSet<int> { doing.Id, review.Id, done.Id }, Columns.AtOrPastImplementationIds(board));
    }

    static readonly DateTimeOffset Earlier = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClosedAt_IntoANonTerminalColumn_IsNullWhateverTheOtherArgumentsAre()
    {
        var doing = S(1, "Doing", 10);
        var review = S(2, "Review", 20);
        var done = S(3, "Done", 30, terminal: true);

        Assert.Null(Columns.ClosedAt(null, doing, Now, null));
        Assert.Null(Columns.ClosedAt(doing, review, Now, Earlier));
        Assert.Null(Columns.ClosedAt(done, doing, Now, Earlier));
    }

    [Fact]
    public void ClosedAt_IntoATerminalColumnFromAnythingElse_IsNow()
    {
        var review = S(1, "Review", 10);
        var done = S(2, "Done", 20, terminal: true);

        Assert.Equal(Now, Columns.ClosedAt(null, done, Now, null));
        Assert.Equal(Now, Columns.ClosedAt(review, done, Now, null));
        Assert.Equal(Now, Columns.ClosedAt(review, done, Now, Earlier));
    }

    [Fact]
    public void ClosedAt_BetweenTwoTerminalColumns_KeepsTheEarlierInstant()
    {
        var done = S(1, "Done", 10, terminal: true);
        var shipped = S(2, "Shipped", 20, terminal: true);

        Assert.Equal(Earlier, Columns.ClosedAt(done, shipped, Now, Earlier));
        Assert.Null(Columns.ClosedAt(done, shipped, Now, null));
    }

    [Fact]
    public void NoImplementationColumn_AtOrPastImplementationIdsIsEmpty()
    {
        var review = S(1, "Review", 10);
        var done = S(2, "Done", 20, terminal: true);
        var board = new List<EfHatchStatus> { review, done };

        Assert.Empty(Columns.AtOrPastImplementationIds(board));
        Assert.False(Columns.AtOrPastImplementation(board, review.Id));
        Assert.False(Columns.AtOrPastImplementation(board, done.Id));
    }
}

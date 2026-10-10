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
    static EfHatchStatus S(int id, string name, int sortOrder, bool terminal = false, bool isProtected = false) =>
        new() { Id = id, Name = name, SortOrder = sortOrder, IsTerminal = terminal, IsProtected = isProtected };

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
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// How <see cref="QuestionTrail.ReadAsync"/> pairs a question with its answer in
/// a range, against how it pairs them over the whole board. The one known
/// difference - a question that lapsed before the range and was answered by a
/// person from it on - is what <c>laterAnswerBeatsLapse</c> puts back.
/// </summary>
public class QuestionTrailTests
{
    private static readonly DateTimeOffset From = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);

    private sealed class Board
    {
        public required HatchContext Db { get; init; }
        public required long IssueId { get; init; }
    }

    private static async Task<Board> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = From.AddDays(-30) };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30 };
        db.AddRange(project, doing);
        await db.SaveChangesAsync();

        var issue = new EfHatchIssue
        {
            ProjectId = project.Id, Number = 1, Type = "task", Title = "task", Description = "", StatusId = doing.Id,
            Rank = 1, CreatedBy = "ann", CreatedAt = From.AddDays(-30), UpdatedAt = From.AddDays(-30),
        };
        db.Issues.Add(issue);
        await db.SaveChangesAsync();

        return new Board { Db = db, IssueId = issue.Id };
    }

    private static async Task<EfHatchComment> AskAsync(Board b, DateTimeOffset at)
    {
        var asked = new EfHatchComment
        {
            IssueId = b.IssueId, Author = "ann", Body = "which way?", Kind = EfHatchComment.Question, CreatedAt = at,
        };
        b.Db.Comments.Add(asked);
        await b.Db.SaveChangesAsync();
        return asked;
    }

    private static async Task AnswerAsync(Board b, EfHatchComment asked, string author, DateTimeOffset at, bool lapsed = false)
    {
        b.Db.Comments.Add(new EfHatchComment
        {
            IssueId = asked.IssueId, Author = author, Body = "that way", Kind = EfHatchComment.Answer,
            AnswersId = asked.Id, CreatedAt = at,
        });

        if (lapsed)
            b.Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = asked.IssueId, Actor = author, Kind = EfHatchIssueEvent.Answered, At = at,
                Payload = $"{{\"questionId\":{asked.Id},\"lapsed\":true}}",
            });

        await b.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task AQuestionThatLapsedBeforeTheRange_AndWasAnsweredInIt_IsAnsweredWhenLaterAnswerBeatsLapse()
    {
        var b = await NewAsync();
        var asked = await AskAsync(b, From.AddDays(-5));
        await AnswerAsync(b, asked, "Hatch", From.AddDays(-3), lapsed: true);
        await AnswerAsync(b, asked, "bo", From.AddDays(2));

        var whole = await QuestionTrail.ReadAsync(b.Db, null, null, default);
        var bounded = await QuestionTrail.ReadAsync(b.Db, From, To, default, laterAnswerBeatsLapse: true);

        var expected = new FlowReport.Question(b.IssueId, asked.Id, From.AddDays(-5), From.AddDays(2));
        Assert.Equal(expected, Assert.Single(whole));
        Assert.Equal(expected, Assert.Single(bounded));
    }

    [Fact]
    public async Task WithoutTheFlag_ThatQuestionIsStillLeftOut()
    {
        var b = await NewAsync();
        var asked = await AskAsync(b, From.AddDays(-5));
        await AnswerAsync(b, asked, "Hatch", From.AddDays(-3), lapsed: true);
        await AnswerAsync(b, asked, "bo", From.AddDays(2));

        Assert.Empty(await QuestionTrail.ReadAsync(b.Db, From, To, default));
    }

    [Fact]
    public async Task AQuestionALapseAndAPersonBothAnsweredBeforeTheRange_StaysLeftOut()
    {
        var b = await NewAsync();
        var asked = await AskAsync(b, From.AddDays(-5));
        await AnswerAsync(b, asked, "Hatch", From.AddDays(-3), lapsed: true);
        await AnswerAsync(b, asked, "bo", From.AddDays(-2));
        await AnswerAsync(b, asked, "bo", From.AddDays(2));

        Assert.Empty(await QuestionTrail.ReadAsync(b.Db, From, To, default, laterAnswerBeatsLapse: true));
    }

    [Fact]
    public async Task AQuestionAPersonAnsweredBeforeTheRange_AndAgainInIt_StaysLeftOut()
    {
        var b = await NewAsync();
        var asked = await AskAsync(b, From.AddDays(-5));
        await AnswerAsync(b, asked, "bo", From.AddDays(-3));
        await AnswerAsync(b, asked, "bo", From.AddDays(2));

        Assert.Empty(await QuestionTrail.ReadAsync(b.Db, From, To, default, laterAnswerBeatsLapse: true));
    }

    [Fact]
    public async Task AQuestionAskedAfterTheRange_IsNotPutBack()
    {
        var b = await NewAsync();
        var asked = await AskAsync(b, To.AddDays(1));
        await AnswerAsync(b, asked, "Hatch", From.AddDays(-3), lapsed: true);
        await AnswerAsync(b, asked, "bo", From.AddDays(2));

        Assert.Empty(await QuestionTrail.ReadAsync(b.Db, From, To, default, laterAnswerBeatsLapse: true));
    }

    [Fact]
    public async Task AQuestionThatLapsedInTheRange_IsLapsed_WhateverTheFlag()
    {
        var b = await NewAsync();
        var asked = await AskAsync(b, From.AddDays(1));
        await AnswerAsync(b, asked, "Hatch", From.AddDays(2), lapsed: true);

        var bounded = await QuestionTrail.ReadAsync(b.Db, From, To, default, laterAnswerBeatsLapse: true);

        Assert.Equal(
            new FlowReport.Question(b.IssueId, asked.Id, From.AddDays(1), null, From.AddDays(2)), Assert.Single(bounded));
    }
}

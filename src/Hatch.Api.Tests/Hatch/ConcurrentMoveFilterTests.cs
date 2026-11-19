using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ConcurrentMoveFilter"/>: the loser of a race to move an issue is
/// answered with a 409, and any other exception is left alone.
/// </summary>
public class ConcurrentMoveFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static ExceptionContext ContextFor(Exception exception) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = exception,
        };

    /// <summary>
    /// The exception EF itself raises: one context changes a row another has
    /// already deleted, which the in-memory provider refuses the way a
    /// concurrency token's <c>UPDATE</c> that matches no row is refused.
    /// </summary>
    private static async Task<DbUpdateConcurrencyException> ConcurrencyFailureAsync(Func<HatchContext, Task> change)
    {
        var options = new DbContextOptionsBuilder<HatchContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var seed = new HatchContext(options);
        var project = new EfHatchProject { Key = "AER", Name = "Aerie", CreatedAt = Now };
        seed.Add(project);
        await seed.SaveChangesAsync();
        seed.Add(new EfHatchIssue
        {
            ProjectId = project.Id, Number = 12, StatusId = 3, Type = "task", Title = "t", Rank = 1,
            CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        });
        await seed.SaveChangesAsync();

        await using var stale = new HatchContext(options);
        await change(stale);

        await using (var other = new HatchContext(options))
        {
            other.Issues.RemoveRange(other.Issues);
            other.Projects.RemoveRange(other.Projects);
            await other.SaveChangesAsync();
        }

        return await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [Fact]
    public async Task AConcurrencyFailureOverAnIssue_IsAConflict_WithNothingWritten()
    {
        var failure = await ConcurrencyFailureAsync(async db =>
        {
            var issue = await db.Issues.Include(i => i.Project).SingleAsync();
            issue.StatusId = 4;
        });
        var context = ContextFor(failure);

        new ConcurrentMoveFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        var conflict = Assert.IsType<ConflictObjectResult>(context.Result);
        Assert.Contains("AER-12", (string)conflict.Value!);
        Assert.Contains("nothing was written", (string)conflict.Value!);
    }

    [Fact]
    public async Task AConcurrencyFailureWithNoIssue_IsLeftUnhandled()
    {
        var failure = await ConcurrencyFailureAsync(async db =>
            (await db.Projects.SingleAsync()).Name = "Renamed");
        var context = ContextFor(failure);

        new ConcurrentMoveFilter().OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    [Fact]
    public void AnyOtherException_IsLeftUnhandled()
    {
        var context = ContextFor(new InvalidOperationException("boom"));

        new ConcurrentMoveFilter().OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }
}

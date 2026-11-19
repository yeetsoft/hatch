using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Answers the loser of a race to move an issue with a 409, not a 500.
/// </summary>
/// <remarks>
/// <see cref="EfHatchIssue.StatusId"/> is a concurrency token, so a request
/// that read an issue and wrote after another request had moved it has its
/// whole save refused. A route that can say something better catches that
/// itself (<c>WorkController.MergedWork</c> goes round again and answers the
/// gate's own sentence); every other route that moves an issue reaches this
/// filter. It is the same quiet 409 a runner already walks on from, and
/// nothing was written, so the caller reads the issue again if it still cares.
/// An exception with no issue among its entries - the issue-number counter's,
/// an invite's - is not this filter's and passes on untouched.
/// </remarks>
public sealed class ConcurrentMoveFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateConcurrencyException { Entries: var entries }) return;
        if (entries.Select(e => e.Entity).OfType<EfHatchIssue>().FirstOrDefault() is not { } issue) return;

        var name = issue.Project is { } project
            ? IssueKey.Format(project.Key, issue.Number)
            : "The issue";
        context.Result = new ConflictObjectResult(
            $"{name} changed column while this request ran, so nothing was written - read it again.");
        context.ExceptionHandled = true;
    }
}

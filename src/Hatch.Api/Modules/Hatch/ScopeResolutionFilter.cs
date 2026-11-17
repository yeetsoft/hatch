using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Resolves the caller's scope once per request, after the role gate and
/// before the action, so every query the action makes runs against a scope
/// that is already known.
/// </summary>
/// <remarks>
/// Authorization filters run before action filters, so a caller the role gate
/// refuses never costs a scope read. The filter acts on this module's
/// controllers and no others: a route in the core schema has no filtered
/// table to guard. No query filter does I/O - this is the one place that
/// reads, and <see cref="RequestScope"/> is where the answer is kept. The
/// resolver is fetched inside the branch so a core route does not build a
/// context it will never use.
/// </remarks>
public sealed class ScopeResolutionFilter : IAsyncActionFilter
{
    private static readonly string ModuleNamespace = typeof(ScopeResolutionFilter).Namespace!;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor { ControllerTypeInfo.Namespace: { } ns }
            && ns == ModuleNamespace)
            await context.HttpContext.RequestServices.GetRequiredService<ICallerScope>()
                .ResolveAsync(context.HttpContext.RequestAborted);

        await next();
    }
}

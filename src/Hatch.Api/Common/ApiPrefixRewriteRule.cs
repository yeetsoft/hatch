using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;

namespace Hatch.Api.Common;

/// <summary>
/// The Hatch module used to answer at <c>/api/hatch/&lt;name&gt;</c> and now answers
/// at <c>/api/&lt;name&gt;</c>. API keys, scripts and a <c>hatch</c> binary built
/// before that change keep calling the old prefix, so it is served in place: the
/// request's path is rewritten to the new one and carries on down the same
/// pipeline, wall included, with its method, body, headers and query string as
/// they arrived.
///
/// <para>Rewritten, never redirected. A redirect was tried first and took every
/// runner down with it: HttpClient drops the <c>Authorization</c> header when it
/// follows any redirect - same origin or not, 301 or 308 - so an older binary
/// arrived at the new address keyless, read a <c>401</c> off
/// <c>/me</c>, and its loop refused to start. A rewrite has no second hop to
/// lose the key on.</para>
///
/// <para>The capture follows <c>/+</c> and must not itself start with a slash or a
/// backslash, the same guard the <c>/apps/hatch</c> rule carries, so the
/// rewritten path is always a single <c>/api/&lt;name&gt;</c>.
/// <c>/api/hatch-revision</c> is a route of its own and never matches, since the
/// prefix must be followed by a slash.</para>
/// </summary>
public sealed partial class ApiPrefixRewriteRule : IRule
{
    [GeneratedRegex(@"^/api/hatch/+([^/\\].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OldPrefix();

    public void ApplyRule(RewriteContext context)
    {
        var request = context.HttpContext.Request;
        var match = OldPrefix().Match(request.Path.Value ?? "");
        if (!match.Success) return;

        request.Path = new PathString("/api/" + match.Groups[1].Value);
        context.Result = RuleResult.SkipRemainingRules;
    }
}

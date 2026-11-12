using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;

namespace Hatch.Api.Common;

/// <summary>
/// The Hatch module used to answer at <c>/api/hatch/&lt;name&gt;</c> and now answers
/// at <c>/api/&lt;name&gt;</c>. API keys, scripts and a <c>hatch</c> binary built
/// before that change keep calling the old prefix, so it answers a permanent
/// redirect to the same path and query string rather than a 404.
///
/// <para>GET and HEAD answer 301; every other method answers 308. A plain
/// <c>AddRedirect</c> is 301 for every method, and a client turns a 301 on a POST
/// into a GET - the write of an old script would silently become a read. A 308
/// repeats the method and the body, and HttpClient carries a same-origin
/// <c>Authorization</c> header across it.</para>
///
/// <para>The capture follows <c>/+</c> and must not itself start with a slash or a
/// backslash, the same guard the <c>/apps/hatch</c> rule carries: a bare
/// <c>(.*)</c> would echo <c>/api/hatch//x.example</c> into a <c>//</c> Location,
/// which a browser resolves as another host. <c>/api/hatch-revision</c> is a
/// route of its own and never matches, since the prefix must be followed by a
/// slash.</para>
/// </summary>
public sealed partial class ApiPrefixRedirectRule : IRule
{
    [GeneratedRegex(@"^/api/hatch/+([^/\].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OldPrefix();

    public void ApplyRule(RewriteContext context)
    {
        var request = context.HttpContext.Request;
        var match = OldPrefix().Match(request.Path.Value ?? "");
        if (!match.Success) return;

        var response = context.HttpContext.Response;
        response.StatusCode = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            ? StatusCodes.Status301MovedPermanently
            : StatusCodes.Status308PermanentRedirect;
        response.Headers.Location =
            request.PathBase.Add(new PathString("/api/" + match.Groups[1].Value)).ToUriComponent()
            + request.QueryString.ToUriComponent();
        context.Result = RuleResult.EndResponse;
    }
}

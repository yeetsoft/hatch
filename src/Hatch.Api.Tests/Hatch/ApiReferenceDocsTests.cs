using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The API reference page, held to the routes the controllers expose. A route
/// added under <c>/api/hatch</c> without a row on the page is a red build
/// naming the page and the route, so the page cannot drift quietly the way a
/// doc nobody is made to open does.
/// </summary>
/// <remarks>
/// One direction only: a page row with no route behind it is not checked here.
/// </remarks>
public partial class ApiReferenceDocsTests
{
    private const string Page = "src/Hatch.Web/apps/hatch/docs/api-reference.md";
    private const string Prefix = "/api/hatch";

    [Fact]
    public void EveryApiHatchRouteHasARowOnTheApiReference()
    {
        var rows = PageRows();
        var missing = Routes().Where(r => !rows.Contains(r)).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(
            missing.Count == 0,
            $"api-reference.md has no row for: {string.Join(", ", missing)}. Add one to the table for its resource.");
    }

    /// <summary>
    /// Guards the enumeration itself: a reflection that found nothing, or a
    /// regex that matched no rows, would leave the test above green or flood
    /// its message with every route.
    /// </summary>
    [Fact]
    public void TheEnumerationFindsTheRoutes()
    {
        var routes = Routes();
        var rows = PageRows();

        Assert.True(routes.Count > 100, $"The reflection found {routes.Count} api/hatch routes, too few to be the whole surface.");
        Assert.True(rows.Count > 100, $"The parse of {Page} found {rows.Count} rows, too few to be the whole page.");
    }

    /// <summary>Every <c>METHOD /path</c> the actions expose, the <c>/api/hatch</c> prefix stripped.</summary>
    private static HashSet<string> Routes()
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName);

            foreach (var action in actions)
            {
                foreach (var attribute in action.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var path = Normalise(Combine(prefix, attribute.Template));

                    // A bare "/api/hatch-revision" shares the text but not the segment.
                    if (path != Prefix && !path.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase)) continue;

                    var rest = path[Prefix.Length..];
                    foreach (var method in attribute.HttpMethods) routes.Add($"{method} {(rest.Length == 0 ? "/" : rest)}");
                }
            }
        }

        return routes;
    }

    /// <summary>A template starting <c>/</c> or <c>~/</c> stands alone; the rest hang off the controller's route.</summary>
    private static string Combine(string prefix, string? template)
    {
        if (string.IsNullOrEmpty(template)) return prefix;
        if (template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal)) return template;
        return $"{prefix}/{template}";
    }

    private static string Normalise(string path) =>
        "/" + PlaceholderPattern().Replace(path.Trim('~').Trim('/'), "{}").Trim('/');

    /// <summary>
    /// The page's rows as <c>METHOD /path</c>. Every table has Method first
    /// except <i>A ticket's own fields</i>, whose Field column leads, so one
    /// optional cell is allowed before it.
    /// </summary>
    private static HashSet<string> PageRows()
    {
        var rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(Doc()))
        {
            var match = RowPattern().Match(line);
            if (!match.Success) continue;

            var path = match.Groups[2].Value;
            var query = path.IndexOf('?');
            if (query >= 0) path = path[..query];

            rows.Add($"{match.Groups[1].Value} {PlaceholderPattern().Replace(path, "{}")}");
        }

        return rows;
    }

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^\|\s*(?:[^|`]*\|\s*)?(GET|POST|PUT|PATCH|DELETE)\s*\|\s*`(/[^`]*)`")]
    private static partial Regex RowPattern();

    /// <summary>
    /// Walks up from the test binary to the repository, rather than assuming
    /// how deep <c>bin/Debug/net10.0</c> happens to be today.
    /// </summary>
    private static string Doc()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, Page.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not find {Page} above {AppContext.BaseDirectory}.");
    }
}

using Hatch.Api.Common;
using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Hatch.Api.Tests;

/// <summary>
/// HA-2319's criteria: the Hatch module answers at <c>/api/&lt;name&gt;</c>, and
/// the old <c>/api/hatch/&lt;name&gt;</c> prefix redirects permanently - 301 for a
/// read, 308 for everything else so a write keeps its method and body - with the
/// query string intact, never touching <c>/api/hatch-revision</c>.
///
/// <para>The rule is exercised on its own first, with no host and no database;
/// the cases that go through the real pipeline share
/// <see cref="RootMountFixture"/>.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ApiPrefixRedirectTests(RootMountFixture fixture) : IClassFixture<RootMountFixture>
{
    private static (RuleResult Result, HttpResponse Response) Apply(string method, string path, string query = "", string pathBase = "")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.PathBase = pathBase;
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);

        var context = new RewriteContext
        {
            HttpContext = http,
            StaticFileProvider = new NullFileProvider(),
            Logger = NullLogger.Instance,
        };
        new ApiPrefixRedirectRule().ApplyRule(context);
        return (context.Result, http.Response);
    }

    [Theory]
    [InlineData("GET", "/api/hatch/issues/AER-12", "?x=1", 301, "/api/issues/AER-12?x=1")]
    [InlineData("HEAD", "/api/hatch/issues", "", 301, "/api/issues")]
    [InlineData("POST", "/api/hatch/issues/AER-12/comments", "", 308, "/api/issues/AER-12/comments")]
    [InlineData("PUT", "/api/hatch/issues/AER-12/dreams", "?a=1&b=2", 308, "/api/issues/AER-12/dreams?a=1&b=2")]
    [InlineData("PATCH", "/api/hatch/issues/AER-12", "", 308, "/api/issues/AER-12")]
    [InlineData("DELETE", "/api/hatch/issues/AER-12/dependencies/AER-3", "", 308, "/api/issues/AER-12/dependencies/AER-3")]
    [InlineData("GET", "/API/Hatch/issues", "", 301, "/api/issues")]
    [InlineData("GET", "/api/hatch/", "", 301, "/api/")]
    public void TheOldPrefixRedirectsToTheSamePathAndQuery(string method, string path, string query, int status, string location)
    {
        var (result, response) = Apply(method, path, query);

        Assert.Equal(RuleResult.EndResponse, result);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(location, response.Headers.Location.ToString());
    }

    [Fact]
    public void ThePathBaseIsKept()
    {
        var (_, response) = Apply("GET", "/api/hatch/issues", pathBase: "/h");

        Assert.Equal("/h/api/issues", response.Headers.Location.ToString());
    }

    /// <summary>A `//` in the Location is a protocol-relative URL - the extra slashes must not survive into it.</summary>
    [Theory]
    [InlineData("/api/hatch//x.example")]
    [InlineData("/api/hatch///x.example")]
    public void ATailBeginningWithSlashesStaysAPathOnThisHost(string path)
    {
        var (result, response) = Apply("GET", path);

        Assert.Equal(RuleResult.EndResponse, result);
        Assert.Equal("/api/x.example", response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/api/hatch-revision")]
    [InlineData("/api/hatch-revision/x")]
    [InlineData("/api/hatch")]
    [InlineData("/api/hatchery/issues")]
    [InlineData("/api/issues")]
    [InlineData("/api/hatch/\\x.example")]
    [InlineData("/apps/hatch/issues")]
    public void NothingElseIsTouched(string path)
    {
        var (result, response) = Apply("GET", path);

        Assert.Equal(RuleResult.ContinueRules, result);
        Assert.False(response.Headers.ContainsKey("Location"));
    }

    [SkippableTheory]
    [InlineData("/api/hatch/issues/AER-12?x=1", "/api/issues/AER-12?x=1")]
    [InlineData("/api/hatch/projects", "/api/projects")]
    public async Task TheRealPipelineAnswers301ForARead(string from, string to)
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.NoRedirectClient.GetAsync(from);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(to, response.Headers.Location?.OriginalString);
    }

    [SkippableTheory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task TheRealPipelineAnswers308ForAWrite(string method)
    {
        fixture.SkipIfUnavailable();
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/hatch/issues/AER-12/comments?x=1")
        {
            Content = new StringContent("{}"),
        };
        var response = await fixture.NoRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal("/api/issues/AER-12/comments?x=1", response.Headers.Location?.OriginalString);
    }

    [SkippableFact]
    public async Task ATailBeginningWithSlashesNeverBecomesAProtocolRelativeLocation()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.NoRedirectClient.GetAsync("/api/hatch//x.example");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/api/x.example", response.Headers.Location?.OriginalString);
    }

    [SkippableFact]
    public async Task TheRevisionRouteIsNotRedirected()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.NoRedirectClient.GetAsync("/api/hatch-revision");

        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>With the wall off a keyless call reads as the local person, so the new address answers directly.</summary>
    [SkippableFact]
    public async Task TheNewPrefixAnswersDirectly()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.NoRedirectClient.GetAsync("/api/statuses");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
    }
}

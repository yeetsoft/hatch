using Hatch.Api.Common;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Hatch.Api.Tests;

/// <summary>
/// The Hatch module answers at <c>/api/&lt;name&gt;</c> (HA-2319), and the old
/// <c>/api/hatch/&lt;name&gt;</c> prefix is served in place - rewritten, never
/// redirected, because HttpClient drops a bearer key across any redirect and
/// every runner on an older binary arrived at the new address keyless.
///
/// <para>The rule is exercised on its own first, with no host and no database;
/// the cases that go through the real pipeline share
/// <see cref="RootMountFixture"/>.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ApiPrefixRewriteTests(RootMountFixture fixture) : IClassFixture<RootMountFixture>
{
    private static (RuleResult Result, HttpContext Http) Apply(string method, string path, string query = "", string pathBase = "")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.PathBase = pathBase;
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        http.Request.Headers.Authorization = "Bearer hatch_ak_test";

        var context = new RewriteContext
        {
            HttpContext = http,
            StaticFileProvider = new NullFileProvider(),
            Logger = NullLogger.Instance,
        };
        new ApiPrefixRewriteRule().ApplyRule(context);
        return (context.Result, http);
    }

    [Theory]
    [InlineData("GET", "/api/hatch/me", "/api/me")]
    [InlineData("GET", "/api/hatch/issues/AER-12", "/api/issues/AER-12")]
    [InlineData("HEAD", "/api/hatch/issues", "/api/issues")]
    [InlineData("POST", "/api/hatch/issues/AER-12/comments", "/api/issues/AER-12/comments")]
    [InlineData("PUT", "/api/hatch/issues/AER-12/dreams", "/api/issues/AER-12/dreams")]
    [InlineData("PATCH", "/api/hatch/issues/AER-12", "/api/issues/AER-12")]
    [InlineData("DELETE", "/api/hatch/issues/AER-12/dependencies/AER-3", "/api/issues/AER-12/dependencies/AER-3")]
    [InlineData("GET", "/API/Hatch/issues", "/api/issues")]
    [InlineData("GET", "/api/hatch/", "/api/")]
    [InlineData("GET", "/api/hatch//x.example", "/api/x.example")]
    [InlineData("GET", "/api/hatch///x.example", "/api/x.example")]
    public void TheOldPrefixIsServedInPlace_MethodQueryAndKeyUntouched(string method, string path, string rewritten)
    {
        var (result, http) = Apply(method, path, "?a=1&b=2");

        Assert.Equal(RuleResult.SkipRemainingRules, result);
        Assert.Equal(rewritten, http.Request.Path.Value);
        Assert.Equal(method, http.Request.Method);
        Assert.Equal("?a=1&b=2", http.Request.QueryString.Value);
        Assert.Equal("Bearer hatch_ak_test", http.Request.Headers.Authorization.ToString());
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        Assert.False(http.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public void ThePathBaseIsKept()
    {
        var (_, http) = Apply("GET", "/api/hatch/issues", pathBase: "/h");

        Assert.Equal("/h", http.Request.PathBase.Value);
        Assert.Equal("/api/issues", http.Request.Path.Value);
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
        var (result, http) = Apply("GET", path);

        Assert.Equal(RuleResult.ContinueRules, result);
        Assert.Equal(path, http.Request.Path.Value);
    }

    /// <summary>The old address answers exactly what the new one does, with no redirect in between.</summary>
    [SkippableTheory]
    [InlineData("/api/hatch/statuses", "/api/statuses")]
    [InlineData("/api/hatch/me", "/api/me")]
    [InlineData("/api/hatch/projects?x=1", "/api/projects?x=1")]
    [InlineData("/api/hatch/no-such-route", "/api/no-such-route")]
    public async Task TheRealPipelineAnswersTheOldAddressAsTheNew(string old, string current)
    {
        fixture.SkipIfUnavailable();
        var before = await fixture.NoRedirectClient.GetAsync(old);
        var after = await fixture.NoRedirectClient.GetAsync(current);

        Assert.Equal(after.StatusCode, before.StatusCode);
        Assert.Null(before.Headers.Location);
        Assert.Equal(await after.Content.ReadAsStringAsync(), await before.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The regression itself: a key sent to the old prefix arrives with the
    /// request. <c>/me</c> answers <c>kind: key</c> and the key's own name only
    /// when it read the bearer header - the same answer at either address -
    /// where a redirect would have dropped the header and answered the local
    /// person instead.
    /// </summary>
    [SkippableFact]
    public async Task AKeySentToTheOldPrefixIsReadAsThatKey()
    {
        fixture.SkipIfUnavailable();
        var name = $"rewrite-{Guid.NewGuid():N}";
        ApiKeyCreated? created;
        using (var scope = fixture.Services.CreateScope())
            created = await scope.ServiceProvider.GetRequiredService<IAuthService>().CreateApiKeyAsync(name, null, default);
        Assert.NotNull(created);

        foreach (var path in new[] { "/api/hatch/me", "/api/me" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.Secret);
            var response = await fixture.NoRedirectClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var me = await response.Content.ReadFromJsonAsync<MeDto>();
            Assert.Equal("key", me?.Kind);
            Assert.Equal(name, me?.Name);
        }
    }

    [SkippableTheory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task TheRealPipelineNeverRedirectsAWrite(string method)
    {
        fixture.SkipIfUnavailable();
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/hatch/issues/AER-12/comments?x=1")
        {
            Content = new StringContent("{}"),
        };
        var response = await fixture.NoRedirectClient.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [SkippableFact]
    public async Task TheRevisionRouteIsUntouched()
    {
        fixture.SkipIfUnavailable();
        var response = await fixture.NoRedirectClient.GetAsync("/api/hatch-revision");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}

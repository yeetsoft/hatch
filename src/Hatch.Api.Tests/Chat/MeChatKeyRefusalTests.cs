using System.Net;
using System.Reflection;
using System.Text;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// A person's Slack link is the person's own: a key never reads or writes one,
/// and the guarantee does not depend on the wall being up. The first test needs
/// no database and holds every action, so a route cannot be added open to a key
/// unnoticed; the second asks the real wall; the third is the wall off.
/// </summary>
[Collection(global::Hatch.Api.Tests.Jobs.QuartzSchedulerCollection.Name)]
public class MeChatKeyRefusalTests
{
    private static readonly string[] Actions =
    [
        "ConfirmLink", "DeleteLink", "Get", "GetMembers", "GetSuggestion", "PostLink",
    ];

    [Fact]
    public void EveryMeChatAction_RequiresAUser_AndAcceptsNoKey()
    {
        var actions = typeof(MeChatController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        Assert.Equal(Actions.Order(), actions.Select(a => a.Name).Order());
        foreach (var action in actions)
        {
            // The action's own attribute: the class carries none, so nothing is inherited by accident.
            var role = action.GetCustomAttribute<RequireRoleAttribute>();

            Assert.True(role is not null, $"{action.Name} asks for no role of its own");
            Assert.True(role!.Minimum == PersonRole.User, $"{action.Name} asks for {role.Minimum}, not a user");
            Assert.False(role.AcceptKey, $"{action.Name} accepts an API key");
        }

        Assert.Null(typeof(MeChatController).GetCustomAttribute<RequireRoleAttribute>());
    }

    [SkippableFact(Timeout = 120_000)]
    public async Task AKey_IsRefused_OnEveryRoute_ButAPersonReadsTheirOwnList()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset - the wall runs on the real host over Postgres. Run `make test-api-db`.");

        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(100), () => string.Join(Environment.NewLine, logs.Messages));

        await using var host = await WalledHost.StartAsync(deadline, logs);

        // An admin's own key: the role is the person's, so only the route's
        // refusal of keys can be what answers.
        using var key = await host.AsKeyAsync(await host.MintKeyAsync("me-chat-key", host.Some));
        var routes = new (HttpMethod Method, string Path, string? Json)[]
        {
            (HttpMethod.Get, "/api/me/chat", null),
            (HttpMethod.Get, "/api/me/chat/workspaces/1/suggestion", null),
            (HttpMethod.Get, "/api/me/chat/workspaces/1/members", null),
            (HttpMethod.Post, "/api/me/chat/links", """{"workspaceId":1,"memberId":"U1"}"""),
            (HttpMethod.Post, "/api/me/chat/links/confirm", """{"secret":"not-a-secret"}"""),
            (HttpMethod.Delete, "/api/me/chat/links/1", null),
        };

        var failures = new List<string>();
        foreach (var (method, path, json) in routes)
        {
            using var message = new HttpRequestMessage(method, path);
            if (json is not null) message.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await deadline.BoundAsync($"{method} {path}", () => key.SendAsync(message));
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.Forbidden || !body.Contains(RoleDecision.KeyNotAccepted))
                failures.Add($"{method} {path} answered {(int)response.StatusCode} {body}, expected 403 {RoleDecision.KeyNotAccepted}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        // The control: the same wall lets a person through, so the 403s above
        // were the key's and not a route nothing can reach.
        using var person = await host.AsPersonAsync(host.Some);
        using var list = await deadline.BoundAsync("the list as a person", () => person.GetAsync("/api/me/chat"));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("linksOn", await list.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The role gate sleeps where the wall is off, so the action refuses a
    /// program by itself: every action, before it touches anything else, which
    /// is why the controller here is built with nothing behind it.
    /// </summary>
    [Fact]
    public async Task WithTheWallOff_AProgramIsStillRefused_ByTheActionItself()
    {
        var key = new EfApiKey { Name = "hatch", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = DateTimeOffset.UnixEpoch };
        var controller = new MeChatController(
            null!, null!, null!, new MeScopeTests.StubCaller(null, null, key), null!, null!, null!, null!, null!, null!, null!);

        var refusals = new ObjectResult?[]
        {
            (await controller.Get(default)).Result as ObjectResult,
            (await controller.GetSuggestion(1, default)).Result as ObjectResult,
            (await controller.GetMembers(1, default)).Result as ObjectResult,
            (await controller.PostLink(new MeChatLinkRequest(1, "U1"), default)).Result as ObjectResult,
            (await controller.ConfirmLink(new MeChatConfirmRequest("x"), default)).Result as ObjectResult,
            await controller.DeleteLink(1, default) as ObjectResult,
        };

        Assert.Equal(Actions.Length, refusals.Length);
        Assert.All(refusals, r =>
        {
            Assert.NotNull(r);
            Assert.Equal(403, r!.StatusCode);
            Assert.Equal(MeChatController.NotAnAgent, r.Value);
        });
    }
}

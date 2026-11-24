using System.Net;
using System.Reflection;
using System.Text;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// A key never reaches a chat workspace or a room: a bot token can speak for
/// the install, so every route is an admin's and a person's alone. The first
/// test needs no database and holds every action, so a route cannot be added
/// open to a key unnoticed; the second asks the real wall.
/// </summary>
[Collection(global::Hatch.Api.Tests.Jobs.QuartzSchedulerCollection.Name)]
public class ChatKeyRefusalTests
{
    private static readonly string[] Actions =
    [
        "ConnectWorkspace", "CreateRoom", "GetChannels", "GetDelivery", "GetLinks", "GetManifest", "GetRooms", "GetWorkspaces",
        "PatchRoom", "PostTestMessage", "PutProjects", "RemoveLink", "RemoveRoom", "RemoveWorkspace", "ReplaceToken",
    ];

    [Fact]
    public void EveryChatAction_RequiresAnAdmin_AndAcceptsNoKey()
    {
        var actions = typeof(ChatController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        Assert.Equal(Actions.Order(), actions.Select(a => a.Name).Order());
        foreach (var action in actions)
        {
            var role = action.GetCustomAttribute<RequireRoleAttribute>()
                ?? action.DeclaringType!.GetCustomAttribute<RequireRoleAttribute>();

            Assert.True(role is not null, $"{action.Name} asks for no role at all");
            Assert.True(role!.Minimum == PersonRole.Admin, $"{action.Name} asks for {role.Minimum}, not an admin");
            Assert.False(role.AcceptKey, $"{action.Name} accepts an API key");
        }
    }

    [SkippableFact(Timeout = 120_000)]
    public async Task AKey_IsRefused_OnEveryRoute_ButAnAdminReadsTheManifest()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset - the wall runs on the real host over Postgres. Run `make test-api-db`.");

        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(100), () => string.Join(Environment.NewLine, logs.Messages));

        await using var host = await WalledHost.StartAsync(deadline, logs);

        // An admin's own key: the role is the person's, so only the route's
        // refusal of keys can be what answers.
        using var key = await host.AsKeyAsync(await host.MintKeyAsync("chat-key", host.Some));
        var routes = new (HttpMethod Method, string Path, string? Json)[]
        {
            (HttpMethod.Get, "/api/chat/workspaces", null),
            (HttpMethod.Post, "/api/chat/workspaces", """{"token":"xoxb-1-not-sent-anywhere"}"""),
            (HttpMethod.Put, "/api/chat/workspaces/1/token", """{"token":"xoxb-1-not-sent-anywhere"}"""),
            (HttpMethod.Delete, "/api/chat/workspaces/1", null),
            (HttpMethod.Get, "/api/chat/manifest", null),
            (HttpMethod.Get, "/api/chat/delivery", null),
            (HttpMethod.Put, "/api/chat/workspaces/1/projects", """{"projectIds":[]}"""),
            (HttpMethod.Get, "/api/chat/workspaces/1/channels", null),
            (HttpMethod.Get, "/api/chat/workspaces/1/rooms", null),
            (HttpMethod.Post, "/api/chat/workspaces/1/rooms", """{"channelId":"C1","channelName":"general","changelog":false,"questions":false,"problems":false,"unownedProblems":false,"allProjects":true}"""),
            (HttpMethod.Patch, "/api/chat/rooms/1", """{}"""),
            (HttpMethod.Delete, "/api/chat/rooms/1", null),
            (HttpMethod.Post, "/api/chat/rooms/1/test", null),
            (HttpMethod.Get, "/api/chat/links", null),
            (HttpMethod.Delete, "/api/chat/links/1", null),
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

        // The control: the same wall lets an admin through, so the 403s above
        // were the key's and not a route nothing can reach.
        using var admin = await host.AsPersonAsync(host.Some);
        using var manifest = await deadline.BoundAsync("manifest as an admin", () => admin.GetAsync("/api/chat/manifest"));
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Contains("display_information", await manifest.Content.ReadAsStringAsync());
    }
}

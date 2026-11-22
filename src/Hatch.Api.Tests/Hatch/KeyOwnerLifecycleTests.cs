using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Models.People;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A key stops with its owner, on a real database and the real host with the
/// wall up. Deleting a person revokes their live keys in the same save as the
/// delete, and only a real database proves that save and the <c>SET NULL</c>
/// that clears each key's owner. Setting a person to <c>pending</c> refuses
/// their keys at the role gate without touching them.
/// Needs Postgres; run it with `make test-api-db`.
/// </summary>
[Collection(global::Hatch.Api.Tests.Jobs.QuartzSchedulerCollection.Name)]
public sealed class KeyOwnerLifecycleTests
{
    [SkippableFact(Timeout = 120_000)]
    public async Task DeletingAPerson_RevokesTheirLiveKeysAndNoOthers()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset. Run `make test-api-db`.");

        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(90), () => string.Join(Environment.NewLine, logs.Messages));
        await using var host = await WalledHost.StartAsync(deadline, logs);

        using var admin = await host.AsPersonAsync(host.Some);

        // The only admin deleting themself is refused, and nothing is revoked.
        var adminKey = await host.MintKeyAsync("owned-by-the-admin", host.Some);
        using var adminKeyClient = await host.AsKeyAsync(adminKey);
        using var refused = await deadline.BoundAsync("deleting the last admin", () => admin.DeleteAsync($"/api/people/{host.Some.Id}"));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Null((await host.KeyRowAsync(adminKey)).RevokedAt);
        using var stillLive = await deadline.BoundAsync("the admin's key", () => adminKeyClient.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.OK, stillLive.StatusCode);

        // A user with a live key, a key revoked beforehand, and a key with no owner.
        var user = await host.AddPersonAsync("key owner", PersonRole.User);
        var live = await host.MintKeyAsync("owned-and-live", user);
        var old = await host.MintKeyAsync("owned-and-revoked", user);
        await host.RevokeAsync(old);
        var oldRevokedAt = (await host.KeyRowAsync(old)).RevokedAt;
        Assert.NotNull(oldRevokedAt);
        var unowned = await host.MintKeyAsync("owned-by-nobody", owner: null);

        using var liveClient = await host.AsKeyAsync(live);
        using var unownedClient = await host.AsKeyAsync(unowned);
        using var before = await deadline.BoundAsync("the live key", () => liveClient.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var beforeDelete = DateTimeOffset.UtcNow;
        using var deleted = await deadline.BoundAsync("deleting the user", () => admin.DeleteAsync($"/api/people/{user.Id}"));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var after = await deadline.BoundAsync("the live key, after", () => liveClient.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        var liveRow = await host.KeyRowAsync(live);
        Assert.NotNull(liveRow.RevokedAt);
        Assert.True(liveRow.RevokedAt >= beforeDelete.AddSeconds(-5));
        Assert.Null(liveRow.OwnerPersonId);

        var oldRow = await host.KeyRowAsync(old);
        Assert.Equal(oldRevokedAt, oldRow.RevokedAt);
        Assert.Null(oldRow.OwnerPersonId);

        using var unownedResponse = await deadline.BoundAsync("the unowned key", () => unownedClient.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.OK, unownedResponse.StatusCode);

        // Read as raw JSON: the list carries enums this case has no use for.
        var runners = await deadline.BoundAsync("listing runners", () => admin.GetFromJsonAsync<JsonElement>("/api/runners"));
        foreach (var name in new[] { live.Name, old.Name })
        {
            var listed = Assert.Single(runners.EnumerateArray(), r => r.GetProperty("name").GetString() == name);
            Assert.NotEqual(JsonValueKind.Null, listed.GetProperty("revokedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, listed.GetProperty("ownerPersonId").ValueKind);
        }
    }

    [SkippableFact(Timeout = 120_000)]
    public async Task APendingOwner_RefusesTheKeyWithoutRevokingIt_AndASecondRoleChangeRestoresIt()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset. Run `make test-api-db`.");

        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(90), () => string.Join(Environment.NewLine, logs.Messages));
        await using var host = await WalledHost.StartAsync(deadline, logs);

        using var admin = await host.AsPersonAsync(host.Some);
        var user = await host.AddPersonAsync("soon pending", PersonRole.User);
        var key = await host.MintKeyAsync("owned-by-a-user", user);
        using var client = await host.AsKeyAsync(key);

        using var working = await deadline.BoundAsync("the key, working", () => client.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.OK, working.StatusCode);

        using var demoted = await deadline.BoundAsync("setting pending", () => admin.PutAsJsonAsync($"/api/people/{user.Id}", new PersonWriteRequest(user.Name, "pending")));
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);

        foreach (var path in new[] { "/api/runner", "/api/board" })
        {
            using var refused = await deadline.BoundAsync($"the key on {path}", () => client.GetAsync(path));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("pending_approval", await refused.Content.ReadAsStringAsync());
        }

        var row = await host.KeyRowAsync(key);
        Assert.Null(row.RevokedAt);
        Assert.Equal(user.Id, row.OwnerPersonId);
        Assert.Equal(key.Name, row.Name);

        using var restored = await deadline.BoundAsync("setting user", () => admin.PutAsJsonAsync($"/api/people/{user.Id}", new PersonWriteRequest(user.Name, "user")));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);

        using var again = await deadline.BoundAsync("the key, again", () => client.GetAsync("/api/runner"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }
}

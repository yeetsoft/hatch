using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// An admin connects, lists, re-tokens and removes chat workspaces. The
/// provider is a fake answering from a token-to-workspace table, so nothing
/// here calls a chat service; <see cref="SlackChatProviderTests"/> holds that
/// half. The in-memory context does not enforce the unique index, so the race
/// it guards is in <c>ChatWorkspaceCascadeTests</c>, on Postgres.
/// </summary>
public class ChatControllerTests
{
    private const string AcmeToken = FakeChatProvider.AcmeToken;
    private const string AcmeNewToken = FakeChatProvider.AcmeNewToken;
    private const string GlobexToken = FakeChatProvider.GlobexToken;
    private const string RefusedToken = FakeChatProvider.RefusedToken;

    [Fact]
    public async Task TwoWorkspaces_Connect_AndBothAreListed_OldestFirst()
    {
        var h = NewHarness();

        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var globex = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(GlobexToken), default));

        var listed = Value(await h.Chat.GetWorkspaces(default));

        Assert.Equal([acme.Id, globex.Id], listed.Select(w => w.Id));
        Assert.Equal(["Acme", "Globex"], listed.Select(w => w.Name));
        Assert.Equal(["TACME", "TGLOBEX"], listed.Select(w => w.ExternalId));
        Assert.All(listed, w => Assert.Equal("fake", w.Provider));
        Assert.All(listed, w => Assert.True(w.HasToken));
        Assert.Equal(Now, acme.CreatedAt);
    }

    [Fact]
    public async Task ABlankToken_IsABadRequest_AndCallsNothing()
    {
        var h = NewHarness();

        foreach (var token in new string?[] { null, "", "   " })
        {
            var result = await h.Chat.ConnectWorkspace(new ChatTokenRequest(token), default);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        Assert.Equal(0, h.Provider.Checks);
        Assert.Empty(h.Db.ChatWorkspaces);
    }

    [Fact]
    public async Task ARefusedToken_AddsNothing_AndSaysTheProvidersSentence()
    {
        var h = NewHarness();

        var result = await h.Chat.ConnectWorkspace(new ChatTokenRequest(RefusedToken), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(FakeChatProvider.RefusalSentence, refusal.Value);
        Assert.Empty(h.Db.ChatWorkspaces);
    }

    [Fact]
    public async Task AWorkspaceAlreadyConnected_IsAConflict_NamingTheReplaceRoute()
    {
        var h = NewHarness();
        await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default);

        var again = await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeNewToken), default);

        var conflict = Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Contains("replace its token", (string)conflict.Value!);
        Assert.Single(h.Db.ChatWorkspaces);
        Assert.Equal(AcmeToken, SecretProtector.Unprotect((await h.Db.ChatWorkspaces.SingleAsync()).Token));
    }

    [Fact]
    public async Task ReplacingATokenForTheSameWorkspace_ChangesTheTokenNameAndUpdatedAt_AndNothingElse()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        h.Provider.Workspaces[AcmeNewToken] = ("TACME", "Acme Renamed");
        h.Time.Advance(TimeSpan.FromHours(1));

        var replaced = Value(await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(AcmeNewToken), default));

        var row = await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync();
        Assert.Equal(AcmeNewToken, SecretProtector.Unprotect(row.Token));
        Assert.Equal("Acme Renamed", row.Name);
        Assert.Equal(Now.AddHours(1), row.UpdatedAt);
        Assert.Equal(acme.Id, row.Id);
        Assert.Equal("TACME", row.ExternalId);
        Assert.Equal("fake", row.Provider);
        Assert.Equal(Now, row.CreatedAt);
        Assert.Equal(new ChatWorkspaceDto(acme.Id, "fake", "TACME", "Acme Renamed", true, Now), replaced);
    }

    [Fact]
    public async Task ReplacingWithATokenForAnotherWorkspace_IsAConflict_AndKeepsTheOldToken()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));

        var result = await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(GlobexToken), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("another workspace", (string)conflict.Value!);
        var row = await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync();
        Assert.Equal(AcmeToken, SecretProtector.Unprotect(row.Token));
        Assert.Equal("Acme", row.Name);
        Assert.Equal(Now, row.UpdatedAt);
    }

    [Fact]
    public async Task ReplacingAToken_ThatIsRefused_KeepsTheOldToken()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));

        var result = await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(RefusedToken), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(AcmeToken, SecretProtector.Unprotect((await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync()).Token));
    }

    [Fact]
    public async Task ReplacingTheTokenOfAnUnknownWorkspace_IsNotFound()
    {
        var h = NewHarness();

        var result = await h.Chat.ReplaceToken(404, new ChatTokenRequest(AcmeToken), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(0, h.Provider.Checks);
    }

    [Fact]
    public async Task Removing_DeletesTheRow_AndAnUnknownIdIsNotFound()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var globex = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(GlobexToken), default));

        Assert.IsType<NoContentResult>(await h.Chat.RemoveWorkspace(acme.Id, default));
        Assert.IsType<NotFoundResult>(await h.Chat.RemoveWorkspace(acme.Id, default));

        Assert.Equal([globex.Id], (await h.Db.ChatWorkspaces.AsNoTracking().ToListAsync()).Select(w => w.Id));
    }

    [Fact]
    public async Task TheStoredToken_IsNotThePlaintext_AndUnprotectsBackToIt()
    {
        var h = NewHarness();
        await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default);

        var stored = (await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync()).Token;

        Assert.NotEqual(AcmeToken, stored);
        Assert.DoesNotContain("acmeSecretPart", stored);
        Assert.Equal(AcmeToken, SecretProtector.Unprotect(stored));
    }

    [Fact]
    public async Task TheToken_IsTrimmedBeforeItIsChecked()
    {
        var h = NewHarness();

        Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest($"  {AcmeToken}\n"), default));

        Assert.Equal(AcmeToken, SecretProtector.Unprotect((await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync()).Token));
    }

    [Fact]
    public void TheManifest_IsTheProvidersJson_UnderItsName()
    {
        var h = NewHarness();

        var manifest = Value(h.Chat.GetManifest());

        Assert.Equal("fake", manifest.Provider);
        Assert.Equal(FakeChatProvider.ManifestText, manifest.Manifest);
    }

    [Fact]
    public async Task NoResponseAndNoLogLine_HoldsTheTokenOrAnyPartOfIt_OnSuccessOrAnyRefusal()
    {
        var h = NewHarness();
        var bodies = new List<string>();

        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        bodies.Add(Text(acme));
        bodies.Add(Text(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default)));
        bodies.Add(Text(await h.Chat.ConnectWorkspace(new ChatTokenRequest(RefusedToken), default)));
        bodies.Add(Text(await h.Chat.ConnectWorkspace(new ChatTokenRequest(" "), default)));
        bodies.Add(Text(await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(AcmeNewToken), default)));
        bodies.Add(Text(await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(GlobexToken), default)));
        bodies.Add(Text(await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(RefusedToken), default)));
        bodies.Add(Text(await h.Chat.ReplaceToken(404, new ChatTokenRequest(AcmeToken), default)));
        bodies.Add(Text(await h.Chat.GetWorkspaces(default)));
        bodies.Add(Text(h.Chat.GetManifest()));
        await h.Chat.RemoveWorkspace(acme.Id, default);

        Assert.NotEmpty(h.Log.Lines);
        foreach (var token in new[] { AcmeToken, AcmeNewToken, GlobexToken, RefusedToken })
        foreach (var part in Parts(token))
        {
            Assert.DoesNotContain(bodies, b => b.Contains(part, StringComparison.Ordinal));
            Assert.DoesNotContain(h.Log.Lines, l => l.Contains(part, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AWrite_LogsWhoDidWhatToWhich_AndNothingMore()
    {
        var h = NewHarness();

        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));

        var line = Assert.Single(h.Log.Lines);
        Assert.Contains("Root", line);
        Assert.Contains(acme.Id.ToString(), line);
    }

    [Fact]
    public void TheDto_HasNoTokenMember()
    {
        Assert.DoesNotContain(typeof(ChatWorkspaceDto).GetProperties(), p => p.Name.Contains("Token") && p.Name != "HasToken");
    }

    /// <summary>Every run of eight characters, so "any part of it" means a part long enough to be the token and not a prefix like <c>xoxb-</c>.</summary>
    private static IEnumerable<string> Parts(string token) =>
        Enumerable.Range(0, token.Length - 7).Select(i => token.Substring(i, 8));

    private static string Text(object? result) => result switch
    {
        ActionResult<ChatWorkspaceDto> a => Text(a.Result ?? (object?)a.Value),
        ActionResult<ChatManifestDto> a => Text(a.Result ?? (object?)a.Value),
        ActionResult<IReadOnlyList<ChatWorkspaceDto>> a => Text(a.Result ?? (object?)a.Value),
        ObjectResult o => Text(o.Value),
        string s => s,
        null => "",
        _ => JsonSerializer.Serialize(result, result.GetType()),
    };

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name}");

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required ChatController Chat { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeChatProvider Provider { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required CapturingLogger Log { get; init; }
    }

    private static Harness NewHarness()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var provider = new FakeChatProvider();
        var time = new FakeTimeProvider(Now);
        var log = new CapturingLogger();

        return new Harness
        {
            Chat = new ChatController(db, provider, new StubCaller("Root"), time, log)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            },
            Db = db,
            Provider = provider,
            Time = time,
            Log = log,
        };
    }

    private sealed class CapturingLogger : ILogger<ChatController>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{formatter(state, exception)} {exception}");
    }

    private sealed class StubCaller(string name) : ICallerIdentity
    {
        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);
        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult<EfPerson?>(null);
        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult<EfApiKey?>(null);
        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);
        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<string> ActorNameAsync(CancellationToken ct) => Task.FromResult(name);
    }
}

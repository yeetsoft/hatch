using System.Reflection;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Settings;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Hatch's own two settings, written from Hatch's own page.
///
/// Backed by the real <see cref="SiteSettingsService"/> rather than a stub,
/// because the property worth proving is the one a stub would hide: a write
/// followed by a read on the same request sees the new value, which is what
/// makes the battery reflect a new token without a restart.
/// </summary>
public class SettingsControllerTests
{
    [Fact]
    public async Task WithNothingSet_BothFieldsAreEmpty()
    {
        var settings = await NewFixture().Controller.GetHatchSettings(default);

        Assert.Equal("", settings.ClaudeSubscriptionToken);
        Assert.Equal("", settings.LocalPersonName);
    }

    [Fact]
    public async Task ASavedToken_ReadsBackAsDotsAndNeverAsItself()
    {
        var fixture = NewFixture();

        var settings = await fixture.PutAsync(new(ClaudeSubscriptionToken: "sk-ant-oat-1", null));

        Assert.DoesNotContain("sk-ant-oat-1", settings.ClaudeSubscriptionToken);
        Assert.NotEqual("", settings.ClaudeSubscriptionToken);
    }

    /// <summary>Stored the way every other secret-valued setting is: never in cleartext, even in the row.</summary>
    [Fact]
    public async Task ASavedToken_IsProtectedAtRestAndReachesTheCredential()
    {
        var fixture = NewFixture();

        await fixture.PutAsync(new(ClaudeSubscriptionToken: "sk-ant-oat-1", null));

        var stored = fixture.Db.SiteSettings.Single(s => s.Key == SiteSettingKeys.ClaudeSubscriptionToken).Value;
        Assert.DoesNotContain("sk-ant-oat-1", stored);
        Assert.Equal("sk-ant-oat-1", await fixture.Credential.GetAsync("claude", default));
    }

    /// <summary>The whole of "clearing it removes it": a value sent, not a second verb.</summary>
    [Fact]
    public async Task ClearingTheToken_LeavesItReadingExactlyLikeOneNobodySet()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new(ClaudeSubscriptionToken: "sk-ant-oat-1", null));

        var settings = await fixture.PutAsync(new(ClaudeSubscriptionToken: "", null));

        Assert.Equal("", settings.ClaudeSubscriptionToken);
        Assert.Null(await fixture.Credential.GetAsync("claude", default));
    }

    /// <summary>A second save replaces rather than appends - there is one row, and it is the one the admin page wrote to.</summary>
    [Fact]
    public async Task SavingAgain_ReplacesTheTokenInPlace()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new(ClaudeSubscriptionToken: "first", null));

        await fixture.PutAsync(new(ClaudeSubscriptionToken: "second", null));

        Assert.Single(fixture.Db.SiteSettings.Where(s => s.Key == SiteSettingKeys.ClaudeSubscriptionToken));
        Assert.Equal("second", await fixture.Credential.GetAsync("claude", default));
    }

    /// <summary>
    /// Criterion 6, as a test: this is the same site setting the admin page
    /// wrote, under the same key and with the same protection, so a token set
    /// before any of this landed is already set here.
    /// </summary>
    [Fact]
    public async Task ATokenSetBeforeThisPageExisted_ReadsBackOnIt()
    {
        var fixture = NewFixture((SiteSettingKeys.ClaudeSubscriptionToken, SecretProtector.Protect("set-by-the-admin-page")));

        var settings = await fixture.Controller.GetHatchSettings(default);

        Assert.NotEqual("", settings.ClaudeSubscriptionToken);
        Assert.Equal("set-by-the-admin-page", await fixture.Credential.GetAsync("claude", default));
    }

    // ---- The site's default token caps ----

    [Fact]
    public async Task DefaultTokenCaps_NothingSet_AreNull()
    {
        var settings = await NewFixture().Controller.GetHatchSettings(default);

        Assert.Null(settings.DefaultMaxTokens);
        Assert.Null(settings.DefaultMaxTokensPerHour);
    }

    [Fact]
    public async Task DefaultTokenCaps_Stored_ReadBackAsLongs()
    {
        var fixture = NewFixture(
            (SiteSettingKeys.RunnerDefaultMaxTokens, "2000000"),
            (SiteSettingKeys.RunnerDefaultMaxTokensPerHour, "200000"));

        var settings = await fixture.Controller.GetHatchSettings(default);

        Assert.Equal(2_000_000L, settings.DefaultMaxTokens);
        Assert.Equal(200_000L, settings.DefaultMaxTokensPerHour);
    }

    /// <summary>A cleared default is stored as the empty string, and a hand-edited row can say anything: both read as unset, never as a cap of zero.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("x")]
    [InlineData("-5")]
    public async Task DefaultTokenCaps_ClearedOrJunk_ReadAsUnset(string stored)
    {
        var fixture = NewFixture(
            (SiteSettingKeys.RunnerDefaultMaxTokens, stored),
            (SiteSettingKeys.RunnerDefaultMaxTokensPerHour, stored));

        var settings = await fixture.Controller.GetHatchSettings(default);

        Assert.Null(settings.DefaultMaxTokens);
        Assert.Null(settings.DefaultMaxTokensPerHour);
    }

    // ---- The site's default token caps, written ----

    private static HatchSettingsWriteRequest Caps(string? max = null, string? perHour = null) =>
        new(null, null, DefaultMaxTokens: max, DefaultMaxTokensPerHour: perHour);

    [Fact]
    public async Task ADefaultCapSet_ReadsBackInTheResponseAndTheSnapshot()
    {
        var fixture = NewFixture();

        var settings = await fixture.PutAsync(Caps("2M", "200k"));

        Assert.Equal(2_000_000L, settings.DefaultMaxTokens);
        Assert.Equal(200_000L, settings.DefaultMaxTokensPerHour);
        var snapshot = await fixture.Settings.GetAsync(default);
        Assert.Equal(2_000_000L, snapshot.RunnerDefaultMaxTokens);
        Assert.Equal(200_000L, snapshot.RunnerDefaultMaxTokensPerHour);
    }

    [Fact]
    public async Task AnOmittedDefaultCap_IsLeftAlone()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(Caps("2M", "200k"));

        var settings = await fixture.PutAsync(new(null, LocalPersonName: "Grace"));

        Assert.Equal(2_000_000L, settings.DefaultMaxTokens);
        Assert.Equal(200_000L, settings.DefaultMaxTokensPerHour);
        Assert.Equal(2_000_000L, (await fixture.Settings.GetAsync(default)).RunnerDefaultMaxTokens);
    }

    [Fact]
    public async Task SavingADefaultCapAgain_ReplacesItInPlace()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(Caps("500k"));

        var settings = await fixture.PutAsync(Caps("2M"));

        Assert.Equal(2_000_000L, settings.DefaultMaxTokens);
        Assert.Single(fixture.Db.SiteSettings.Where(s => s.Key == SiteSettingKeys.RunnerDefaultMaxTokens));
    }

    [Fact]
    public async Task ClearingADefaultCap_PutsItBackToUnset_AndLeavesTheOtherAlone()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(Caps("2M", "200k"));

        var settings = await fixture.PutAsync(Caps(max: ""));

        Assert.Null(settings.DefaultMaxTokens);
        Assert.Equal(200_000L, settings.DefaultMaxTokensPerHour);
        Assert.Null((await fixture.Settings.GetAsync(default)).RunnerDefaultMaxTokens);

        settings = await fixture.PutAsync(Caps(perHour: "  "));

        Assert.Null(settings.DefaultMaxTokensPerHour);
    }

    [Fact]
    public async Task SettingOneDefaultCap_DoesNotDisturbTheOther()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(Caps(max: "2M"));

        var settings = await fixture.PutAsync(Caps(perHour: "1.5M"));

        Assert.Equal(2_000_000L, settings.DefaultMaxTokens);
        Assert.Equal(1_500_000L, settings.DefaultMaxTokensPerHour);
    }

    public static TheoryData<bool, string> BadCounts()
    {
        var data = new TheoryData<bool, string>();
        foreach (var perHour in new[] { false, true })
            foreach (var bad in new[] { "0", "x", "2.5k", "-1" })
                data.Add(perHour, bad);
        return data;
    }

    [Theory]
    [MemberData(nameof(BadCounts))]
    public async Task ABadCount_IsRefusedByNameAndWritesNothing(bool perHour, string bad)
    {
        var fixture = NewFixture();

        var answer = await fixture.Controller.PutHatchSettings(perHour ? Caps(perHour: bad) : Caps(bad), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(answer.Result);
        var text = Assert.IsType<string>(refusal.Value);
        Assert.Contains(perHour ? "the default max tokens per hour" : "the default max tokens takes", text);
        Assert.Contains(TokenCount.Refusal, text);
        Assert.Empty(fixture.Db.SiteSettings);
    }

    [Fact]
    public async Task AGoodValueBesideABadOne_WritesNeither()
    {
        var fixture = NewFixture();

        var answer = await fixture.Controller.PutHatchSettings(
            new(null, LocalPersonName: "Ada", DefaultMaxTokens: "2M", DefaultMaxTokensPerHour: "x"), default);

        Assert.IsType<BadRequestObjectResult>(answer.Result);
        Assert.Empty(fixture.Db.SiteSettings);

        answer = await fixture.Controller.PutHatchSettings(
            new(null, LocalPersonName: "Ada", DefaultMaxTokens: "0"), default);

        Assert.IsType<BadRequestObjectResult>(answer.Result);
        Assert.Empty(fixture.Db.SiteSettings);
    }

    // ---- ... and by whom ----

    private static EfPerson APerson(PersonRole role) =>
        new() { Id = Guid.NewGuid(), Name = "Nathan", Role = role, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

    private static EfApiKey AKey() =>
        new() { Name = "hatch", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = DateTime.UtcNow };

    private static async Task AssertAdminsOnly(Fixture fixture, HatchSettingsWriteRequest request)
    {
        var answer = await fixture.Controller.PutHatchSettings(request, default);

        var refusal = Assert.IsType<ObjectResult>(answer.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.Equal("the default token budget is an admin's to set", refusal.Value);
        Assert.Empty(fixture.Db.SiteSettings);
    }

    [Fact]
    public async Task WithTheWallUp_AUserMayNotSetADefault()
    {
        var caller = new StubCallerIdentity { Person = APerson(PersonRole.User) };
        var fixture = NewFixture(true, caller);

        await AssertAdminsOnly(fixture, Caps(max: "2M"));
        await AssertAdminsOnly(fixture, Caps(perHour: "200k"));
        await AssertAdminsOnly(fixture, Caps(max: ""));
    }

    /// <summary>The 403 is first, so somebody who may not set a default learns nothing about what would have been accepted.</summary>
    [Fact]
    public async Task WithTheWallUp_AUserSendingABadCountIsRefusedAsNotAnAdminNotAsBad()
    {
        var caller = new StubCallerIdentity { Person = APerson(PersonRole.User) };

        await AssertAdminsOnly(NewFixture(true, caller), Caps(max: "x"));
    }

    [Fact]
    public async Task WithTheWallUp_AUsersNameOnlyPutStillSucceeds()
    {
        var caller = new StubCallerIdentity { Person = APerson(PersonRole.User) };
        var fixture = NewFixture(true, caller);

        var settings = await fixture.PutAsync(new(null, LocalPersonName: "Ada"));

        Assert.Equal("Ada", settings.LocalPersonName);
    }

    [Fact]
    public async Task WithTheWallUp_AnAdminMaySetADefault()
    {
        var caller = new StubCallerIdentity { Person = APerson(PersonRole.Admin) };
        var fixture = NewFixture(true, caller);

        var settings = await fixture.PutAsync(Caps("2M", "200k"));

        Assert.Equal(2_000_000L, settings.DefaultMaxTokens);
        Assert.Equal(200_000L, settings.DefaultMaxTokensPerHour);
    }

    [Fact]
    public async Task WithTheWallUp_NobodyAtAllMayNotSetADefault()
    {
        await AssertAdminsOnly(NewFixture(true), Caps(max: "2M"));
    }

    [Fact]
    public async Task WithTheWallDown_NobodySpecialMaySetADefault()
    {
        var settings = await NewFixture().PutAsync(Caps("2M"));

        Assert.Equal(2_000_000L, settings.DefaultMaxTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AKey_MayNotSetADefault_WallUpOrDown(bool wall)
    {
        var caller = new StubCallerIdentity { ApiKey = AKey() };

        await AssertAdminsOnly(NewFixture(wall, caller), Caps(max: "2M"));
    }

    /// <summary>A keyless runner that named itself with the runner header is an agent whether or not it carries a credential.</summary>
    [Fact]
    public async Task AWallOffRunner_MayNotSetADefault()
    {
        var caller = new StubCallerIdentity { Local = new Actor(ActorKind.Key, Guid.NewGuid(), "runner") };

        await AssertAdminsOnly(NewFixture(false, caller), Caps(perHour: "200k"));
    }

    // ---- The agent keys, held exactly as the token is ----

    private static HatchSettingsWriteRequest Write(string key, string? value) => key switch
    {
        SiteSettingKeys.CodexApiKey => new(null, null, CodexApiKey: value),
        SiteSettingKeys.XaiApiKey => new(null, null, XaiApiKey: value),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private static string Read(HatchSettingsDto dto, string key) => key switch
    {
        SiteSettingKeys.CodexApiKey => dto.CodexApiKey,
        SiteSettingKeys.XaiApiKey => dto.XaiApiKey,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private static string? Plaintext(SiteSettingsSnapshot snapshot, string key) => key switch
    {
        SiteSettingKeys.CodexApiKey => snapshot.CodexApiKey,
        SiteSettingKeys.XaiApiKey => snapshot.XaiApiKey,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    [Theory]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task AnAgentKeyNobodySet_ReadsEmpty(string key)
    {
        var settings = await NewFixture().Controller.GetHatchSettings(default);

        Assert.Equal("", Read(settings, key));
    }

    [Theory]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task ASavedAgentKey_ReadsBackAsDotsAndNeverAsItself(string key)
    {
        var fixture = NewFixture();

        var written = await fixture.PutAsync(Write(key, "sk-agent-1"));
        var read = await fixture.Controller.GetHatchSettings(default);

        foreach (var dto in new[] { written, read })
        {
            Assert.NotEqual("", Read(dto, key));
            Assert.DoesNotContain("sk-agent-1", System.Text.Json.JsonSerializer.Serialize(dto));
        }
    }

    [Theory]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task ASavedAgentKey_IsProtectedAtRestUnderItsOwnKey(string key)
    {
        var fixture = NewFixture();

        await fixture.PutAsync(Write(key, "sk-agent-1"));

        var stored = fixture.Db.SiteSettings.Single(s => s.Key == key).Value;
        Assert.DoesNotContain("sk-agent-1", stored);
        Assert.Equal("sk-agent-1", SecretProtector.Unprotect(stored));
        Assert.Equal("sk-agent-1", Plaintext(await fixture.Settings.GetAsync(default), key));
    }

    [Theory]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task ClearingAnAgentKey_LeavesItReadingExactlyLikeOneNobodySet(string key)
    {
        var fixture = NewFixture();
        await fixture.PutAsync(Write(key, "sk-agent-1"));

        var settings = await fixture.PutAsync(Write(key, ""));

        Assert.Equal("", Read(settings, key));
        Assert.True(string.IsNullOrWhiteSpace(Plaintext(await fixture.Settings.GetAsync(default), key)));
    }

    [Theory]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task AnOmittedAgentKey_IsLeftAlone(string key)
    {
        var fixture = NewFixture();
        await fixture.PutAsync(Write(key, "sk-agent-1"));

        var settings = await fixture.PutAsync(new(null, LocalPersonName: "Grace"));

        Assert.NotEqual("", Read(settings, key));
        Assert.Equal("sk-agent-1", Plaintext(await fixture.Settings.GetAsync(default), key));
    }

    [Theory]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task SavingAnAgentKeyAgain_ReplacesItInPlace(string key)
    {
        var fixture = NewFixture();
        await fixture.PutAsync(Write(key, "first"));

        await fixture.PutAsync(Write(key, "second"));

        Assert.Single(fixture.Db.SiteSettings.Where(s => s.Key == key));
        Assert.Equal("second", Plaintext(await fixture.Settings.GetAsync(default), key));
    }

    /// <summary>A key the admin page's generic Upsert wrote - same row, same protection - is already set here.</summary>
    [Theory]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task AnAgentKeySetByTheAdminPage_ReadsBackOnIt(string key)
    {
        var fixture = NewFixture((key, SecretProtector.Protect("set-by-the-admin-page")));

        var settings = await fixture.Controller.GetHatchSettings(default);

        Assert.NotEqual("", Read(settings, key));
        Assert.Equal("set-by-the-admin-page", Plaintext(await fixture.Settings.GetAsync(default), key));
    }

    /// <summary>Three credentials in one table: writing one must not touch the other two.</summary>
    [Fact]
    public async Task WritingOneCredential_DoesNotDisturbTheOtherTwo()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new("claude-1", null, CodexApiKey: "codex-1", XaiApiKey: "xai-1"));

        await fixture.PutAsync(new(null, null, CodexApiKey: "codex-2"));
        var snapshot = await fixture.Settings.GetAsync(default);

        Assert.Equal("claude-1", snapshot.ClaudeSubscriptionToken);
        Assert.Equal("codex-2", snapshot.CodexApiKey);
        Assert.Equal("xai-1", snapshot.XaiApiKey);

        await fixture.PutAsync(new(null, null, XaiApiKey: ""));
        snapshot = await fixture.Settings.GetAsync(default);

        Assert.Equal("claude-1", snapshot.ClaudeSubscriptionToken);
        Assert.Equal("codex-2", snapshot.CodexApiKey);
        Assert.True(string.IsNullOrWhiteSpace(snapshot.XaiApiKey));
    }

    /// <summary>Not a secret, so it comes back to be edited rather than redacted.</summary>
    [Fact]
    public async Task TheName_ReadsBackAsItself()
    {
        var fixture = NewFixture();

        var settings = await fixture.PutAsync(new(null, LocalPersonName: "Ada"));

        Assert.Equal("Ada", settings.LocalPersonName);
        Assert.Equal("Ada", (await fixture.Settings.GetAsync(default)).LocalPersonName);
    }

    [Fact]
    public async Task ClearingTheName_PutsItBackToNobodyHavingSaid()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new(null, LocalPersonName: "Ada"));

        var settings = await fixture.PutAsync(new(null, LocalPersonName: ""));

        Assert.Equal("", settings.LocalPersonName);
        Assert.Null((await fixture.Settings.GetAsync(default)).LocalPersonName);
    }

    /// <summary>The bulk-edit convention: a field left out is a field left alone, so one form's Save cannot wipe the other's.</summary>
    [Fact]
    public async Task AnOmittedField_IsLeftAlone()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new(ClaudeSubscriptionToken: "sk-ant-oat-1", LocalPersonName: "Ada"));

        var settings = await fixture.PutAsync(new(null, LocalPersonName: "Grace"));

        Assert.Equal("Grace", settings.LocalPersonName);
        Assert.Equal("sk-ant-oat-1", await fixture.Credential.GetAsync("claude", default));
    }

    [Fact]
    public async Task ARequestNamingNeitherField_WritesNothing()
    {
        var fixture = NewFixture();

        await fixture.PutAsync(new(null, null));

        Assert.Empty(fixture.Db.SiteSettings);
    }

    /// <summary>
    /// Wherever the wall is up the name comes from the grant, so the page has
    /// no field to draw - the route still answers, and it answers that.
    /// </summary>
    [Fact]
    public async Task WithTheWallUp_TheNameDoesNotApply()
    {
        var fixture = NewFixture(authEnabled: true);

        Assert.False((await fixture.Controller.GetHatchSettings(default)).LocalPersonNameApplies);
    }

    [Fact]
    public async Task WithTheWallDown_TheNameApplies()
    {
        Assert.True((await NewFixture().Controller.GetHatchSettings(default)).LocalPersonNameApplies);
    }

    // ---- The token itself, for the container runner ----

    /// <summary>
    /// The one endpoint in Hatch that hands a live secret back out, and what
    /// makes it usable: a container holding nothing but git and the hatch
    /// binary reverses the wrapper locally, with no second round trip and no
    /// openssl.
    /// </summary>
    [Fact]
    public async Task TheTokenRoute_AnswersItWrappedTheWayTheStoreWrapsIt()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new(ClaudeSubscriptionToken: "sk-ant-oat-1", null));

        var answer = await fixture.Controller.GetClaudeToken(default);

        var dto = Assert.IsType<CredentialDto>(answer.Value);
        Assert.DoesNotContain("sk-ant-oat-1", dto.ProtectedToken);
        Assert.Equal("sk-ant-oat-1", SecretProtector.Unprotect(dto.ProtectedToken));
    }

    /// <summary>
    /// Nothing set is not a failure - it is the state a friend is in between
    /// starting the stack and pasting a token, and the entrypoint waits it out
    /// (criterion 4). So the same 204 the battery answers with, and not a 404.
    /// </summary>
    [Fact]
    public async Task WithNoTokenSaved_TheTokenRouteAnswersNoContent()
    {
        var answer = await NewFixture().Controller.GetClaudeToken(default);

        Assert.IsType<NoContentResult>(answer.Result);
    }

    /// <summary>A token cleared reads exactly like one nobody set, here too.</summary>
    [Fact]
    public async Task WithTheTokenCleared_TheTokenRouteAnswersNoContent()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new(ClaudeSubscriptionToken: "sk-ant-oat-1", null));
        await fixture.PutAsync(new(ClaudeSubscriptionToken: "", null));

        Assert.IsType<NoContentResult>((await fixture.Controller.GetClaudeToken(default)).Result);
    }

    /// <summary>
    /// The second gate, and the one the attribute cannot express: wherever the
    /// wall is up nobody gets the token - not a key, not an
    /// administrator at a browser. A route this far inside the filter has
    /// already been told the caller is allowed, so this refusal is the action's
    /// own and it is unconditional.
    /// </summary>
    [Fact]
    public async Task WithTheWallUp_TheTokenRouteRefusesEverybody()
    {
        var fixture = NewFixture(authEnabled: true);
        await fixture.PutAsync(new(ClaudeSubscriptionToken: "sk-ant-oat-1", null));

        var answer = await fixture.Controller.GetClaudeToken(default);

        var refusal = Assert.IsType<ObjectResult>(answer.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.IsType<AuthErrorDto>(refusal.Value);
        Assert.DoesNotContain("sk-ant-oat-1", System.Text.Json.JsonSerializer.Serialize(refusal.Value));
    }

    // ---- credentials/{agent}: the same answer for every agent ----

    private static HatchSettingsWriteRequest WriteAgent(string agent, string? value) => agent switch
    {
        "claude" => new(ClaudeSubscriptionToken: value, null),
        "codex" => new(null, null, CodexApiKey: value),
        "grok" => new(null, null, XaiApiKey: value),
        _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, null),
    };

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task TheCredentialRoute_AnswersItWrappedTheWayTheStoreWrapsIt(string agent)
    {
        var fixture = NewFixture();
        await fixture.PutAsync(WriteAgent(agent, "sk-secret-1"));

        var answer = await fixture.Controller.GetCredential(agent, default);

        var dto = Assert.IsType<CredentialDto>(answer.Value);
        Assert.DoesNotContain("sk-secret-1", dto.ProtectedToken);
        Assert.Equal("sk-secret-1", SecretProtector.Unprotect(dto.ProtectedToken));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task WithNoCredentialSaved_TheCredentialRouteAnswersNoContent(string agent)
    {
        var answer = await NewFixture().Controller.GetCredential(agent, default);

        Assert.IsType<NoContentResult>(answer.Result);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task WithTheCredentialCleared_TheCredentialRouteAnswersNoContent(string agent)
    {
        var fixture = NewFixture();
        await fixture.PutAsync(WriteAgent(agent, "sk-secret-1"));
        await fixture.PutAsync(WriteAgent(agent, ""));

        Assert.IsType<NoContentResult>((await fixture.Controller.GetCredential(agent, default)).Result);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task WithTheWallUp_TheCredentialRouteRefusesEverybody(string agent)
    {
        var fixture = NewFixture(authEnabled: true);
        await fixture.PutAsync(WriteAgent(agent, "sk-secret-1"));

        var answer = await fixture.Controller.GetCredential(agent, default);

        var refusal = Assert.IsType<ObjectResult>(answer.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
        Assert.IsType<AuthErrorDto>(refusal.Value);
        Assert.DoesNotContain("sk-secret-1", System.Text.Json.JsonSerializer.Serialize(refusal.Value));
    }

    /// <summary>An agent this Hatch has no credential for is not "none saved": it is a route that does not exist.</summary>
    [Theory]
    [InlineData("gemini")]
    [InlineData("Claude")]
    public async Task AnUnknownAgent_IsNotFound(string agent)
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new("claude-1", null, CodexApiKey: "codex-1", XaiApiKey: "xai-1"));

        var answer = await fixture.Controller.GetCredential(agent, default);

        Assert.IsType<NotFoundResult>(answer.Result);
    }

    /// <summary>The wall is checked before the agent is, so a refused caller learns nothing about which agents exist.</summary>
    [Fact]
    public async Task WithTheWallUp_AnUnknownAgentIsRefusedNotNotFound()
    {
        var answer = await NewFixture(authEnabled: true).Controller.GetCredential("gemini", default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(answer.Result).StatusCode);
    }

    /// <summary>
    /// The split itself, asserted rather than described: the credential routes take
    /// a key because a keyless runner is its ordinary caller, and
    /// the two routes beside it still take a person and nothing else. Read off
    /// the attributes because that is where the decision lives - and because
    /// RequireRoleAttribute is AllowMultiple = false, so an attribute added at
    /// the class level later would silently replace all three.
    /// </summary>
    [Fact]
    public void TheCredentialRoutesTakeAKeyAndTheOtherTwoDoNot()
    {
        Assert.True(Guard(nameof(SettingsController.GetClaudeToken)).AcceptKey);
        Assert.True(Guard(nameof(SettingsController.GetCredential)).AcceptKey);
        Assert.False(Guard(nameof(SettingsController.GetHatchSettings)).AcceptKey);
        Assert.False(Guard(nameof(SettingsController.PutHatchSettings)).AcceptKey);

        static RequireRoleAttribute Guard(string action) =>
            typeof(SettingsController).GetMethod(action)!.GetCustomAttribute<RequireRoleAttribute>()
            ?? throw new Xunit.Sdk.XunitException($"{action} carries no [RequireAdmin] of its own.");
    }

    private static Fixture NewFixture(params (string Key, string Value)[] seed) => NewFixture(false, null, seed);

    private static Fixture NewFixture(bool authEnabled, params (string Key, string Value)[] seed) =>
        NewFixture(authEnabled, null, seed);

    private static Fixture NewFixture(bool authEnabled, ICallerIdentity? caller, params (string Key, string Value)[] seed)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options);
        db.SiteSettings.AddRange(seed.Select(s => new EfSiteSetting { Key = s.Key, Value = s.Value }));
        db.SaveChanges();

        // A FakeTimeProvider that never advances, so the snapshot's TTL never
        // expires on its own: anything these tests see refreshed was refreshed
        // by the controller's own Invalidate, which is the point.
        var settings = new SiteSettingsService(new TestDbContextFactory(options), new FakeTimeProvider());

        var credential = new SiteSettingAgentCredential(settings);
        var controller = new SettingsController(
            db, settings, credential, Options.Create(new AuthOptions { Enabled = authEnabled }),
            caller ?? new StubCallerIdentity());

        return new Fixture(controller, db, settings, credential);
    }

    private sealed record Fixture(
        SettingsController Controller,
        AppDbContext Db,
        ISiteSettingsService Settings,
        IAgentCredential Credential)
    {
        /// <summary>The <c>PUT</c>, for a test that expects it to be accepted: a refusal fails the test here.</summary>
        public async Task<HatchSettingsDto> PutAsync(HatchSettingsWriteRequest request)
        {
            var answer = await Controller.PutHatchSettings(request, default);
            return answer.Value ?? throw new Xunit.Sdk.XunitException(
                $"the PUT was refused: {answer.Result}");
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new AppDbContext(options));
    }
}

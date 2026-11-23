using System.Reflection;
using System.Security.Cryptography;
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
using Microsoft.Extensions.Logging;
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
        var fixture = NewFixture((SiteSettingKeys.ClaudeSubscriptionToken, V1("set-by-the-admin-page")));

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
        SiteSettingKeys.ClaudeSubscriptionToken => snapshot.ClaudeSubscriptionToken,
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
        Assert.StartsWith("v2:", stored, StringComparison.Ordinal);
        Assert.Equal("sk-agent-1", fixture.Protector.Unprotect(stored, SecretStores.SiteSetting(key)));
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
        var fixture = NewFixture((key, V1("set-by-the-admin-page")));

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
    public async Task TheTokenRoute_AnswersItOnTheV1Wire()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(new(ClaudeSubscriptionToken: "sk-ant-oat-1", null));

        var answer = await fixture.Controller.GetClaudeToken(default);

        var dto = Assert.IsType<CredentialDto>(answer.Value);
        Assert.DoesNotContain("sk-ant-oat-1", dto.ProtectedToken);
        Assert.Equal("v1:OwpZAgZafgYVEX5U", dto.ProtectedToken);
        Assert.Equal("sk-ant-oat-1", CredentialWire.Unwrap(dto.ProtectedToken));
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
    public async Task TheCredentialRoute_AnswersItOnTheV1Wire(string agent)
    {
        var fixture = NewFixture();
        await fixture.PutAsync(WriteAgent(agent, "sk-secret-1"));

        var answer = await fixture.Controller.GetCredential(agent, default);

        var dto = Assert.IsType<CredentialDto>(answer.Value);
        Assert.DoesNotContain("sk-secret-1", dto.ProtectedToken);
        Assert.Equal(CredentialWire.Wrap("sk-secret-1"), dto.ProtectedToken);
        Assert.Equal("sk-secret-1", CredentialWire.Unwrap(dto.ProtectedToken));
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

    // ---- Sealing at rest, and the Hatch with no key ----

    [Theory]
    [InlineData(SiteSettingKeys.ClaudeSubscriptionToken)]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task ASavedSecret_IsStoredSealedForItsOwnRow(string key)
    {
        var fixture = NewFixture();

        await fixture.PutAsync(Request(key, "sk-agent-1"));

        var stored = fixture.Db.SiteSettings.Single(s => s.Key == key).Value;
        Assert.Matches(@"^v2:[^:.]+\.[^:.]+\.[^:.]+$", stored);
        Assert.Equal("sk-agent-1", fixture.Protector.Unprotect(stored, $"site-setting/{key}"));
        Assert.Equal("sk-agent-1", Plaintext(await fixture.Settings.GetAsync(default), key));
    }

    /// <summary>A stored value copied into another setting's row is not that setting's value.</summary>
    [Fact]
    public async Task AStoredValueCopiedIntoAnotherSettingsRow_DoesNotOpen()
    {
        var fixture = NewFixture();
        await fixture.PutAsync(Request(SiteSettingKeys.CodexApiKey, "sk-codex"));
        var stored = fixture.Db.SiteSettings.Single(s => s.Key == SiteSettingKeys.CodexApiKey).Value;

        // The same key as the one that sealed it, so only the row name differs.
        var sameKey = NewFixture(fixture.Cipher, false, null, (SiteSettingKeys.XaiApiKey, stored));

        Assert.Null((await sameKey.Settings.GetAsync(default)).XaiApiKey);
        Assert.Equal("", (await sameKey.Controller.GetHatchSettings(default)).XaiApiKey);
    }

    [Fact]
    public async Task TwoSavesOfTheSameSecret_StoreDifferentText()
    {
        var fixture = NewFixture();

        await fixture.PutAsync(Request(SiteSettingKeys.XaiApiKey, "sk-agent-1"));
        var first = fixture.Db.SiteSettings.Single(s => s.Key == SiteSettingKeys.XaiApiKey).Value;
        await fixture.PutAsync(Request(SiteSettingKeys.XaiApiKey, "sk-agent-1"));
        var second = fixture.Db.SiteSettings.Single(s => s.Key == SiteSettingKeys.XaiApiKey).Value;

        Assert.NotEqual(first, second);
    }

    /// <summary>What was stored before sealing keeps reading, with a key or without one.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AV1AndAnUntaggedValue_StillRead(bool withKey)
    {
        var seed = new[]
        {
            (SiteSettingKeys.CodexApiKey, V1("v1-codex")),
            (SiteSettingKeys.XaiApiKey, SecretObfuscator.Obfuscate("untagged-xai")),
        };
        var fixture = withKey ? NewFixture(seed) : NewFixtureWithNoKey(seed);

        var snapshot = await fixture.Settings.GetAsync(default);

        Assert.Equal("v1-codex", snapshot.CodexApiKey);
        Assert.Equal("untagged-xai", snapshot.XaiApiKey);
    }

    [Fact]
    public async Task ASecretSealedUnderThePreviousKey_StillReads()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var sealedBefore = new SecretProtector(new SecretCipher(oldKey, null));
        Assert.True(sealedBefore.TrySeal("sk-old", SecretStores.SiteSetting(SiteSettingKeys.CodexApiKey), out var stored));

        var fixture = NewFixture(new SecretCipher(RandomNumberGenerator.GetBytes(32), oldKey), false, null,
            (SiteSettingKeys.CodexApiKey, stored));

        Assert.Equal("sk-old", (await fixture.Settings.GetAsync(default)).CodexApiKey);
    }

    [Fact]
    public async Task TheDto_SaysWhetherSecretsCanBeSaved()
    {
        Assert.True((await NewFixture().Controller.GetHatchSettings(default)).CanSaveSecrets);
        Assert.False((await NewFixtureWithNoKey().Controller.GetHatchSettings(default)).CanSaveSecrets);
    }

    /// <summary>The refusal writes nothing at all: a name sent in the same body is not saved either.</summary>
    [Theory]
    [InlineData(SiteSettingKeys.ClaudeSubscriptionToken)]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task WithNoKey_ASecretToStoreIsRefusedAndNothingIsWritten(string key)
    {
        var fixture = NewFixtureWithNoKey();
        var body = Request(key, "sk-agent-1") with { LocalPersonName = "Ada", DefaultMaxTokens = "2M" };

        var answer = await fixture.Controller.PutHatchSettings(body, default);

        var refused = Assert.IsType<BadRequestObjectResult>(answer.Result);
        Assert.Contains("HATCH_SECRET_KEY", Assert.IsType<string>(refused.Value));
        Assert.Empty(fixture.Db.SiteSettings);
    }

    [Theory]
    [InlineData(SiteSettingKeys.ClaudeSubscriptionToken)]
    [InlineData(SiteSettingKeys.CodexApiKey)]
    [InlineData(SiteSettingKeys.XaiApiKey)]
    public async Task WithNoKey_AnEmptyStringStillClearsASecret(string key)
    {
        var fixture = NewFixtureWithNoKey((key, V1("set-before")));

        await fixture.PutAsync(Request(key, ""));

        Assert.Equal("", fixture.Db.SiteSettings.Single(s => s.Key == key).Value);
        Assert.Null(Plaintext(await fixture.Settings.GetAsync(default), key));
    }

    [Fact]
    public async Task WithNoKey_ARequestWithNoSecretInItIsNotRefused()
    {
        var fixture = NewFixtureWithNoKey();

        var settings = await fixture.PutAsync(new(null, "Ada", DefaultMaxTokens: "2M", DefaultMaxTokensPerHour: "200k"));

        Assert.Equal("Ada", settings.LocalPersonName);
        Assert.Equal(2_000_000L, settings.DefaultMaxTokens);
        Assert.Equal(200_000L, settings.DefaultMaxTokensPerHour);
        Assert.False(settings.CanSaveSecrets);
    }

    [Fact]
    public async Task WithNoKey_SecretsAlreadyStoredKeepReading()
    {
        var fixture = NewFixtureWithNoKey((SiteSettingKeys.ClaudeSubscriptionToken, V1("old-token")));

        Assert.Equal("old-token", await fixture.Credential.GetAsync("claude", default));
    }

    // ---- A stored secret that cannot be opened ----

    /// <summary>Every row the snapshot opens a secret from, not only the three the page edits.</summary>
    public static TheoryData<string> EverySecretRow => new()
    {
        SiteSettingKeys.GoogleClientSecret,
        SiteSettingKeys.AnthropicApiKey,
        SiteSettingKeys.ClaudeSubscriptionToken,
        SiteSettingKeys.CodexApiKey,
        SiteSettingKeys.XaiApiKey,
    };

    public static TheoryData<string> ThePageSecretRows => new()
    {
        SiteSettingKeys.ClaudeSubscriptionToken,
        SiteSettingKeys.CodexApiKey,
        SiteSettingKeys.XaiApiKey,
    };

    /// <summary>
    /// None of these opens, so each is unreadable and none throws. Untagged text
    /// that happens to be valid base64 is left out on purpose: <c>v1</c> has no
    /// integrity check, so it "opens" to junk rather than failing.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySecretRow))]
    public async Task WhateverASecretRowHolds_TheSnapshotReadsAndTheRowIsUnreadable(string key)
    {
        var cipher = NewCipher();
        Assert.True(new SecretProtector(cipher).TrySeal("sk-other", SecretStores.SiteSetting("another-row"), out var forAnotherRow));
        Assert.True(new SecretProtector(cipher).TrySeal("sk-own", SecretStores.SiteSetting(key), out var own));
        Assert.True(new SecretProtector(NewCipher()).TrySeal("sk-x", SecretStores.SiteSetting(key), out var underAnotherKey));

        var garbage = new[]
        {
            "v2:",
            "v2:a.b.c",
            forAnotherRow,
            underAnotherKey,
            own[..^4],
            "v3:xyz",
            "v1:%%%",
            "not base64!!",
            ":",
            new string('x', 10 * 1024) + "!",
            "v2:" + new string('x', 10 * 1024),
            "v2:\u0001\u0002\u0003\u0000",
            "\u0001\u0002\u0003",
        };

        foreach (var stored in garbage)
        {
            var fixture = NewFixture(cipher, false, null, (key, stored));

            var snapshot = await fixture.Settings.GetAsync(default);

            var label = stored.Length > 24 ? stored[..24] + "..." : stored;
            Assert.True(PlaintextOf(snapshot, key) is null, $"'{label}' should not have opened");
            Assert.True(snapshot.IsUnreadable(key), $"'{label}' should read as unreadable");
        }
    }

    [Theory]
    [MemberData(nameof(EverySecretRow))]
    public async Task ABlankRow_IsUnsetNotUnreadable(string key)
    {
        foreach (var stored in new[] { "", "   ", "\t\n" })
        {
            var snapshot = await NewFixture((key, stored)).Settings.GetAsync(default);

            Assert.Null(PlaintextOf(snapshot, key));
            Assert.False(snapshot.IsUnreadable(key));
        }
    }

    /// <summary>The key is lost, not the value: everything that uses it sees "not set", and the page is told why.</summary>
    [Theory]
    [MemberData(nameof(ThePageSecretRows))]
    public async Task ASecretSealedUnderAKeyThatIsGone_IsUnreadableAndHandedOffAsNothing(string key)
    {
        var sealer = NewFixture();
        await sealer.PutAsync(Request(key, "sk-agent-1"));
        var stored = sealer.Db.SiteSettings.Single(s => s.Key == key).Value;

        var fixture = NewFixtureWithNoKey((key, stored));
        var settings = await fixture.Controller.GetHatchSettings(default);

        Assert.Equal(SecretStates.Unreadable, StateIn(settings, key));
        Assert.Equal("", key == SiteSettingKeys.ClaudeSubscriptionToken ? settings.ClaudeSubscriptionToken : Read(settings, key));
        Assert.Null(PlaintextOf(await fixture.Settings.GetAsync(default), key));
        Assert.IsType<NoContentResult>((await fixture.Controller.GetCredential(AgentOf(key), default)).Result);
        if (key == SiteSettingKeys.ClaudeSubscriptionToken)
            Assert.IsType<NoContentResult>((await fixture.Controller.GetClaudeToken(default)).Result);
    }

    [Theory]
    [MemberData(nameof(ThePageSecretRows))]
    public async Task TheStates_AreUnsetThenSetThenUnsetWhenCleared(string key)
    {
        var fixture = NewFixture();
        Assert.Equal(new SecretStatesDto("unset", "unset", "unset"), (await fixture.Controller.GetHatchSettings(default)).SecretStates);

        var set = await fixture.PutAsync(Request(key, "sk-agent-1"));
        Assert.Equal(SecretStates.Set, StateIn(set, key));
        Assert.Equal(2, new[] { SiteSettingKeys.ClaudeSubscriptionToken, SiteSettingKeys.CodexApiKey, SiteSettingKeys.XaiApiKey }
            .Count(other => other != key && StateIn(set, other) == SecretStates.Unset));

        var cleared = await fixture.PutAsync(Request(key, ""));
        Assert.Equal(SecretStates.Unset, StateIn(cleared, key));
    }

    [Theory]
    [MemberData(nameof(ThePageSecretRows))]
    public async Task ASaveOverAnUnreadableValue_ReadsAsSet(string key)
    {
        var fixture = NewFixture((key, "v3:xyz"));
        Assert.Equal(SecretStates.Unreadable, StateIn(await fixture.Controller.GetHatchSettings(default), key));

        var saved = await fixture.PutAsync(Request(key, "sk-agent-2"));

        Assert.Equal(SecretStates.Set, StateIn(saved, key));
        Assert.Equal("sk-agent-2", PlaintextOf(await fixture.Settings.GetAsync(default), key));
    }

    [Theory]
    [MemberData(nameof(ThePageSecretRows))]
    public async Task RemovingAnUnreadableValue_ReadsAsUnset(string key)
    {
        var fixture = NewFixture((key, "v3:xyz"));

        var removed = await fixture.PutAsync(Request(key, ""));

        Assert.Equal(SecretStates.Unset, StateIn(removed, key));
        Assert.False((await fixture.Settings.GetAsync(default)).IsUnreadable(key));
    }

    [Fact]
    public async Task AnUnreadableSecret_StillReadsAsEmptyDotsAndSetsNoneOfTheOthers()
    {
        var fixture = NewFixture(
            (SiteSettingKeys.ClaudeSubscriptionToken, "v3:xyz"),
            (SiteSettingKeys.CodexApiKey, V1("sk-codex")));

        var settings = await fixture.Controller.GetHatchSettings(default);

        Assert.Equal("", settings.ClaudeSubscriptionToken);
        Assert.NotEqual("", settings.CodexApiKey);
        Assert.Equal(new SecretStatesDto("unreadable", "set", "unset"), settings.SecretStates);
    }

    [Fact]
    public async Task AnUnreadableSecret_IsWarnedAboutByNameOnceUntilItsTextChanges()
    {
        var fixture = NewFixture((SiteSettingKeys.ClaudeSubscriptionToken, "v3:xyz"));

        await fixture.Settings.GetAsync(default);
        fixture.Settings.Invalidate();
        await fixture.Settings.GetAsync(default);

        var warning = Assert.Single(fixture.Log.Lines);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("ClaudeSubscriptionToken", warning.Text);

        fixture.Db.SiteSettings.Single(s => s.Key == SiteSettingKeys.ClaudeSubscriptionToken).Value = "v3:abc";
        fixture.Db.SaveChanges();
        fixture.Settings.Invalidate();
        await fixture.Settings.GetAsync(default);
        fixture.Settings.Invalidate();
        await fixture.Settings.GetAsync(default);

        Assert.Equal(2, fixture.Log.Lines.Count);
    }

    [Fact]
    public async Task ASecretThatReadsFineAgain_WarnsAgainWhenItBreaksAgain()
    {
        var fixture = NewFixture((SiteSettingKeys.XaiApiKey, "v3:xyz"));
        await fixture.Settings.GetAsync(default);

        await fixture.PutAsync(Request(SiteSettingKeys.XaiApiKey, "sk-agent-1"));
        await fixture.Settings.GetAsync(default);
        Assert.Single(fixture.Log.Lines);

        fixture.Db.SiteSettings.Single(s => s.Key == SiteSettingKeys.XaiApiKey).Value = "v3:xyz";
        fixture.Db.SaveChanges();
        fixture.Settings.Invalidate();
        await fixture.Settings.GetAsync(default);

        Assert.Equal(2, fixture.Log.Lines.Count);
        Assert.All(fixture.Log.Lines, line => Assert.Contains("XaiApiKey", line.Text));
    }

    [Fact]
    public async Task ASecretThatIsUnsetOrBlank_IsNotWarnedAbout()
    {
        var fixture = NewFixture((SiteSettingKeys.CodexApiKey, ""), (SiteSettingKeys.XaiApiKey, "  "));

        await fixture.Settings.GetAsync(default);

        Assert.Empty(fixture.Log.Lines);
    }

    [Fact]
    public async Task TheLog_NeverHoldsTheStoredTextOrAPlaintext()
    {
        var sealer = NewFixture();
        await sealer.PutAsync(Request(SiteSettingKeys.CodexApiKey, "sk-plain-secret"));
        var sealedText = sealer.Db.SiteSettings.Single(s => s.Key == SiteSettingKeys.CodexApiKey).Value;
        var fixture = NewFixtureWithNoKey(
            (SiteSettingKeys.CodexApiKey, sealedText),
            (SiteSettingKeys.XaiApiKey, "v3:do-not-log-this-text"));

        await fixture.Settings.GetAsync(default);

        Assert.Equal(2, fixture.Log.Lines.Count);
        foreach (var line in fixture.Log.Lines)
        {
            Assert.DoesNotContain("sk-plain-secret", line.Text);
            Assert.DoesNotContain("do-not-log-this-text", line.Text);
            Assert.DoesNotContain(sealedText, line.Text);
            Assert.DoesNotContain(sealedText[3..], line.Text);
        }
    }

    private static string StateIn(HatchSettingsDto dto, string key) => key switch
    {
        SiteSettingKeys.ClaudeSubscriptionToken => dto.SecretStates.ClaudeSubscriptionToken,
        SiteSettingKeys.CodexApiKey => dto.SecretStates.CodexApiKey,
        SiteSettingKeys.XaiApiKey => dto.SecretStates.XaiApiKey,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private static string AgentOf(string key) => key switch
    {
        SiteSettingKeys.ClaudeSubscriptionToken => "claude",
        SiteSettingKeys.CodexApiKey => "codex",
        SiteSettingKeys.XaiApiKey => "grok",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private static string? PlaintextOf(SiteSettingsSnapshot snapshot, string key) => key switch
    {
        SiteSettingKeys.GoogleClientSecret => snapshot.GoogleClientSecret,
        SiteSettingKeys.AnthropicApiKey => snapshot.AnthropicApiKey,
        _ => Plaintext(snapshot, key),
    };

    private static HatchSettingsWriteRequest Request(string key, string? value) =>
        key == SiteSettingKeys.ClaudeSubscriptionToken ? new(value, null) : Write(key, value);

    /// <summary>
    /// A value as <c>v1</c> wrote it, made on purpose: nothing writes <c>v1</c>
    /// any more, but every row stored before sealing is one.
    /// </summary>
    private static string V1(string plaintext) => $"v1:{SecretObfuscator.Obfuscate(plaintext)}";

    private static SecretCipher NewCipher() => new(RandomNumberGenerator.GetBytes(32), null);

    private static Fixture NewFixture(params (string Key, string Value)[] seed) => NewFixture(false, null, seed);

    private static Fixture NewFixture(bool authEnabled, params (string Key, string Value)[] seed) =>
        NewFixture(authEnabled, null, seed);

    private static Fixture NewFixture(bool authEnabled, ICallerIdentity? caller, params (string Key, string Value)[] seed) =>
        NewFixture(NewCipher(), authEnabled, caller, seed);

    /// <summary>A Hatch with no secret key set.</summary>
    private static Fixture NewFixtureWithNoKey(params (string Key, string Value)[] seed) =>
        NewFixture(new SecretCipher(null, null), false, null, seed);

    private static Fixture NewFixture(SecretCipher cipher, bool authEnabled, ICallerIdentity? caller, params (string Key, string Value)[] seed)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options);
        db.SiteSettings.AddRange(seed.Select(s => new EfSiteSetting { Key = s.Key, Value = s.Value }));
        db.SaveChanges();

        var protector = new SecretProtector(cipher);

        // A FakeTimeProvider that never advances, so the snapshot's TTL never
        // expires on its own: anything these tests see refreshed was refreshed
        // by the controller's own Invalidate, which is the point.
        var log = new CapturingLogger();
        var settings = new SiteSettingsService(new TestDbContextFactory(options), new FakeTimeProvider(), protector, log);

        var credential = new SiteSettingAgentCredential(settings);
        var controller = new SettingsController(
            db, settings, credential, Options.Create(new AuthOptions { Enabled = authEnabled }),
            caller ?? new StubCallerIdentity(), protector);

        return new Fixture(controller, db, settings, credential, protector, cipher, log);
    }

    /// <summary>Hand-rolled, as in the other tests here: a level and the formatted line, nothing more.</summary>
    private sealed class CapturingLogger : ILogger<SiteSettingsService>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, $"{formatter(state, exception)} {exception}"));
    }

    private sealed record Fixture(
        SettingsController Controller,
        AppDbContext Db,
        ISiteSettingsService Settings,
        IAgentCredential Credential,
        SecretProtector Protector,
        SecretCipher Cipher,
        CapturingLogger Log)
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

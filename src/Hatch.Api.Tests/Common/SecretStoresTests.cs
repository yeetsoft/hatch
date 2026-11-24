using Hatch.Api.Common;
using Hatch.Api.Ef;

namespace Hatch.Api.Tests.Common;

/// <summary>The one list of where a sealed value is stored, and the row names built from it.</summary>
public class SecretStoresTests
{
    [Fact]
    public void A_site_setting_is_named_by_its_key()
    {
        Assert.Equal("site-setting/ClaudeSubscriptionToken", SecretStores.SiteSetting(SiteSettingKeys.ClaudeSubscriptionToken));
    }

    [Fact]
    public void A_chat_workspace_is_named_by_its_provider_and_the_services_own_id()
    {
        Assert.Equal("chat-workspace/slack/T123", SecretStores.ChatWorkspace("slack", "T123"));
    }

    [Fact]
    public void A_chat_workspace_app_token_is_named_apart_from_the_bot_token()
    {
        Assert.Equal("chat-workspace-app-token/slack/T123", SecretStores.ChatWorkspaceAppToken("slack", "T123"));
        Assert.NotEqual(SecretStores.ChatWorkspace("slack", "T123"), SecretStores.ChatWorkspaceAppToken("slack", "T123"));
    }

    [Fact]
    public void The_list_has_the_three_stores_and_each_names_a_row_the_same_way_the_helpers_do()
    {
        Assert.Equal(["SiteSettings", "ChatWorkspaces", "ChatWorkspaces"], SecretStores.All.Select(s => s.Table));
        Assert.Equal(["Value", "Token", "AppToken"], SecretStores.All.Select(s => s.Column));
        Assert.Equal([null, "hatch", "hatch"], SecretStores.All.Select(s => s.Schema));
        Assert.Equal([["Key"], ["Provider", "ExternalId"], ["Provider", "ExternalId"]], SecretStores.All.Select(s => s.IdColumns));
        Assert.Equal("site-setting/XaiApiKey", SecretStores.SiteSettings.RowName(["XaiApiKey"]));
        Assert.Equal("chat-workspace/slack/T123", SecretStores.ChatWorkspaces.RowName(["slack", "T123"]));
        Assert.Equal("chat-workspace-app-token/slack/T123", SecretStores.ChatWorkspaceAppTokens.RowName(["slack", "T123"]));
    }

    [Fact]
    public void The_five_secret_settings_are_listed()
    {
        Assert.Equal(
            ["GoogleClientSecret", "AnthropicApiKey", "ClaudeSubscriptionToken", "CodexApiKey", "XaiApiKey"],
            SiteSettingKeys.Secrets);
    }

    [Fact]
    public void Only_the_site_settings_store_is_limited_to_the_secret_keys()
    {
        Assert.Equal(SiteSettingKeys.Secrets, SecretStores.SiteSettings.OnlyKeys);
        Assert.Null(SecretStores.ChatWorkspaces.OnlyKeys);
        Assert.Null(SecretStores.ChatWorkspaceAppTokens.OnlyKeys);
    }
}

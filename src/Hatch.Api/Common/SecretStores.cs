using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Common;

/// <summary>
/// One place a sealed value is stored: the table, the column, and how a row of
/// it is named. The name is the associated data the value is sealed under, so a
/// value copied onto another row does not open there.
/// </summary>
/// <param name="Schema">The schema the table is in, or null for the default one.</param>
/// <param name="Table">The table, as the database names it.</param>
/// <param name="Column">The column the sealed value is in.</param>
/// <param name="IdColumns">The columns <paramref name="RowName"/> is built from, in the order it takes them: what names a row to a walk that has only the table to go on.</param>
/// <param name="RowName">The name of a row, from the values that identify it: the key for a site setting, the provider and external id for a workspace.</param>
/// <param name="OnlyKeys">When set, a walk of this store reads only the rows whose first id column is one of these values; null means every row.</param>
public sealed record SecretStore(string? Schema, string Table, string Column, string[] IdColumns, Func<string[], string> RowName, string[]? OnlyKeys = null);

/// <summary>
/// Every place a sealed value is stored, listed once. A save seals through this
/// list, and so does anything that walks every sealed value. Row names are
/// built here and nowhere else.
/// </summary>
public static class SecretStores
{
    /// <summary>The site settings in <see cref="SiteSettingKeys.Secrets"/>, one row each, named by key.</summary>
    public static readonly SecretStore SiteSettings = new(null, "SiteSettings", nameof(EfSiteSetting.Value), [nameof(EfSiteSetting.Key)], id => SiteSetting(id[0]), OnlyKeys: SiteSettingKeys.Secrets);

    /// <summary>The chat workspace bot token, named by provider and the service's own id for the workspace.</summary>
    public static readonly SecretStore ChatWorkspaces = new("hatch", "ChatWorkspaces", nameof(EfHatchChatWorkspace.Token), [nameof(EfHatchChatWorkspace.Provider), nameof(EfHatchChatWorkspace.ExternalId)], id => ChatWorkspace(id[0], id[1]));

    /// <summary>Every store, in no order that matters.</summary>
    public static readonly IReadOnlyList<SecretStore> All = [SiteSettings, ChatWorkspaces];

    /// <summary>The row name of the site setting <paramref name="key"/>: <c>site-setting/&lt;key&gt;</c>.</summary>
    public static string SiteSetting(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return $"site-setting/{key}";
    }

    /// <summary>
    /// The row name of a chat workspace: <c>chat-workspace/&lt;provider&gt;/&lt;external id&gt;</c>.
    /// The service's id rather than the database id, since a new row has none
    /// until it is saved and these two never change for a workspace.
    /// </summary>
    public static string ChatWorkspace(string provider, string externalId)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(externalId);
        return $"chat-workspace/{provider}/{externalId}";
    }
}

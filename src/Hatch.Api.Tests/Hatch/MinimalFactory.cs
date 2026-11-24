using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The three settings and nothing else, plus somewhere to log. The content
/// root is a scratch directory rather than the Hatch.Api project's: a
/// developer's own <c>.env.json</c> sits in the latter, and a test whose
/// answer depends on what is in it is not asserting "nothing else is
/// configured".
/// </summary>
/// <param name="webRoot">
/// Applied with <c>UseWebRoot</c> when given. Left out, the web root stays
/// the scratch content root's own empty <c>wwwroot</c> - which is what
/// <see cref="MinimalStartupTests"/> asserts against, so a caller that wants
/// that absence is a caller that passes nothing.
/// </param>
/// <param name="walled">
/// Sets <c>Auth:Enabled</c>. False, the default, is the install a friend's
/// <c>docker run</c> gives and what <see cref="MinimalStartupTests"/> asserts;
/// true puts the wall up, which is how <see cref="WalledHost"/> reaches the
/// Hatch routes as a scoped person or a key.
/// </param>
/// <param name="services">
/// Applied after <c>Program.cs</c> has registered its own services, so what it
/// registers replaces them: a case that needs a fake of something the host
/// reaches out with (a chat service, say) removes the real registration and adds
/// its own. Left out, the host is exactly the one that ships.
/// </param>
/// <param name="secretKey">
/// Sets <c>Hatch:SecretKey</c>. Left out, the host has no secret key, which is
/// the install nobody has configured and the one that refuses to save a secret.
/// </param>
/// <param name="settings">
/// Applied with <c>UseSetting</c>, after the ones above - a case that wants
/// one of the four <c>Database:*</c> pool settings, say, sets it here rather
/// than this constructor growing a parameter per setting.
/// </param>
internal sealed class MinimalFactory(
    string hatch, string quartz, CapturedLogs logs, string? webRoot = null, bool walled = false,
    Action<IServiceCollection>? services = null, string? secretKey = null,
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(MinimalDatabases.ScratchContentRoot());
        if (webRoot is not null) builder.UseWebRoot(webRoot);

        builder.UseSetting("ConnectionStrings:Hatch", hatch);
        builder.UseSetting("ConnectionStrings:Quartz", quartz);
        builder.UseSetting("Auth:Enabled", walled ? "true" : "false");
        if (secretKey is not null) builder.UseSetting("Hatch:SecretKey", secretKey);
        if (settings is not null)
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);

        builder.ConfigureLogging(logging => logging.AddProvider(logs));

        if (services is not null) builder.ConfigureTestServices(services);
    }
}

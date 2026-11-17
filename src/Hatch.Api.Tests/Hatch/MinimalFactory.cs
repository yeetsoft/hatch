using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
internal sealed class MinimalFactory(string hatch, string quartz, CapturedLogs logs, string? webRoot = null, bool walled = false)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(MinimalDatabases.ScratchContentRoot());
        if (webRoot is not null) builder.UseWebRoot(webRoot);

        builder.UseSetting("ConnectionStrings:Hatch", hatch);
        builder.UseSetting("ConnectionStrings:Quartz", quartz);
        builder.UseSetting("Auth:Enabled", walled ? "true" : "false");

        builder.ConfigureLogging(logging => logging.AddProvider(logs));
    }
}

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
internal sealed class MinimalFactory(string hatch, string quartz, CapturedLogs logs, string? webRoot = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(MinimalDatabases.ScratchContentRoot());
        if (webRoot is not null) builder.UseWebRoot(webRoot);

        builder.UseSetting("ConnectionStrings:Hatch", hatch);
        builder.UseSetting("ConnectionStrings:Quartz", quartz);
        builder.UseSetting("Auth:Enabled", "false");

        builder.ConfigureLogging(logging => logging.AddProvider(logs));
    }
}

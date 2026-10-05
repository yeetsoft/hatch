using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules;

public record AssetLinkStatement(
    [property: JsonPropertyName("relation")] string[] Relation,
    [property: JsonPropertyName("target")] AssetLinkTarget Target);

public record AssetLinkTarget(
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("package_name")] string PackageName,
    [property: JsonPropertyName("sha256_cert_fingerprints")] string[] Sha256CertFingerprints);

/// <summary>
/// Android's Digital Asset Links statement list for this install - the file
/// Android's own verifier fetches, unauthenticated, to decide which app may
/// open this install's links without the user granting it by hand. Platform
/// config, alongside the module seam the same way AppsController is, and
/// gated the same way: no role, because the caller is Android itself rather
/// than a signed-in person.
/// </summary>
[ApiController]
[Route("/.well-known/assetlinks.json")]
public class AssetLinksController(IOptions<AppsOptions> options) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AssetLinkStatement[]> Get()
    {
        var packages = options.Value.AssetLinks.Where(p => !p.IsUnconfigured).ToArray();
        if (packages.Length == 0) return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";

        return packages
            .Select(p => new AssetLinkStatement(
                ["delegate_permission/common.handle_all_urls"],
                new AssetLinkTarget("android_app", p.PackageName,
                    [.. p.Sha256CertFingerprints.Select(f => f.ToUpperInvariant())])))
            .ToArray();
    }
}

using Hatch.Api.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Modules;

/// <summary>
/// Covers the shape Android's verifier actually fetches, and the blank-slot
/// case compose.yaml's unset default binds to - the one a naive
/// packages.Length == 0 check would miss, serving a bogus 200 instead of the
/// 404 an unconfigured install promises.
/// </summary>
public class AssetLinksControllerTests
{
    [Fact]
    public void Get_ReturnsNotFound_WhenNoPackagesConfigured()
        => Assert.IsType<NotFoundResult>(Controller().Get().Result);

    [Fact]
    public void Get_ReturnsNotFound_ForASlotLeftEntirelyBlank()
        => Assert.IsType<NotFoundResult>(Controller(
            new AssetLinkPackage { PackageName = "", Sha256CertFingerprints = [""] }).Get().Result);

    [Fact]
    public void Get_ReturnsOneStatement_ForOneConfiguredPackage()
    {
        var result = Controller(
            new AssetLinkPackage { PackageName = "com.example.app", Sha256CertFingerprints = ["aa:bb"] }).Get();

        var statements = Assert.IsType<AssetLinkStatement[]>(result.Value);
        Assert.Single(statements);
        Assert.Equal(["delegate_permission/common.handle_all_urls"], statements[0].Relation);
        Assert.Equal("android_app", statements[0].Target.Namespace);
        Assert.Equal("com.example.app", statements[0].Target.PackageName);
        Assert.Equal(["AA:BB"], statements[0].Target.Sha256CertFingerprints);
    }

    [Fact]
    public void Get_ReturnsStatementsInOrder_ForTwoConfiguredPackages()
    {
        var result = Controller(
            new AssetLinkPackage { PackageName = "com.example.one", Sha256CertFingerprints = ["aa"] },
            new AssetLinkPackage { PackageName = "com.example.two", Sha256CertFingerprints = ["bb"] }).Get();

        var statements = Assert.IsType<AssetLinkStatement[]>(result.Value);
        Assert.Equal(2, statements.Length);
        Assert.Equal("com.example.one", statements[0].Target.PackageName);
        Assert.Equal("com.example.two", statements[1].Target.PackageName);
    }

    [Fact]
    public void Get_FiltersOutTheBlankSlot_AlongsideAConfiguredPackage()
    {
        var result = Controller(
            new AssetLinkPackage { PackageName = "", Sha256CertFingerprints = [""] },
            new AssetLinkPackage { PackageName = "com.example.app", Sha256CertFingerprints = ["aa"] }).Get();

        var statements = Assert.IsType<AssetLinkStatement[]>(result.Value);
        Assert.Single(statements);
        Assert.Equal("com.example.app", statements[0].Target.PackageName);
    }

    private static AssetLinksController Controller(params AssetLinkPackage[] assetLinks) =>
        new(Options.Create(new AppsOptions { AssetLinks = assetLinks }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
}

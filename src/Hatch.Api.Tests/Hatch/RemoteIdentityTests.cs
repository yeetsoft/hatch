using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one rule for when two spellings of a remote are the same repository.
/// Every host here is a reserved name (<c>example.com</c>) rather than a real
/// forge or account - docs/ethos.md.
/// </summary>
public class RemoteIdentityTests
{
    [Theory]
    [InlineData("https://example.com/Owner/Repo.git")]
    [InlineData("ssh://git@example.com/Owner/Repo")]
    [InlineData("git@example.com:Owner/Repo.git")]
    [InlineData("example.com/Owner/Repo/")]
    public void FourSpellingsOfTheSameRemote_FoldToOneString(string remote)
    {
        var (canonical, error) = RemoteIdentity.Canonical(remote);

        Assert.Null(error);
        Assert.Equal("example.com/owner/repo", canonical);
    }

    [Fact]
    public void APort_IsKept()
    {
        var (canonical, error) = RemoteIdentity.Canonical("ssh://git@example.com:2222/owner/repo");

        Assert.Null(error);
        Assert.Equal("example.com:2222/owner/repo", canonical);
    }

    [Fact]
    public void ATrailingDotGitAndATrailingSlash_AreBothDropped()
    {
        var withDotGit = RemoteIdentity.Canonical("https://example.com/owner/repo.git");
        var withSlash = RemoteIdentity.Canonical("https://example.com/owner/repo/");

        Assert.Equal("example.com/owner/repo", withDotGit.Canonical);
        Assert.Equal("example.com/owner/repo", withSlash.Canonical);
    }

    [Fact]
    public void Case_IsFolded()
    {
        var (canonical, _) = RemoteIdentity.Canonical("HTTPS://EXAMPLE.COM/Owner/Repo");

        Assert.Equal("example.com/owner/repo", canonical);
    }

    [Fact]
    public void ALocalPath_FoldsToItselfWithCaseIntact()
    {
        var (canonical, error) = RemoteIdentity.Canonical("/Users/Someone/Checkouts/Repo");

        Assert.Null(error);
        Assert.Equal("/Users/Someone/Checkouts/Repo", canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public void EmptyAndUnparseable_AreBothRefused(string remote)
    {
        var (canonical, error) = RemoteIdentity.Canonical(remote);

        Assert.Null(canonical);
        Assert.NotNull(error);
    }
}

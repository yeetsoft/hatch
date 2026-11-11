using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The recipes Hatch ships, through the real renderer: the exact text each
/// renders to, and that neither takes a version. CI builds and runs them.
/// </summary>
public class ShippedRecipesTests
{
    private static readonly string Make = """
# make: GNU make from apt, for a build that shells out to `make`.
RUN ok= ; for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && apt-get install -y --no-install-recommends make; then ok=1; break; fi; \
      echo "apt: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/*
""".ReplaceLineEndings("\n") + "\n";

    private static readonly string Docker = """
# docker: the client only (docker-ce-cli and the compose plugin) from Docker's own apt repo. No daemon: a container uses the host's socket.
RUN mkdir -p -m 755 /etc/apt/keyrings \
 && . /etc/os-release \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 "https://download.docker.com/linux/$ID/gpg" \
      -o /etc/apt/keyrings/docker.asc \
 && chmod a+r /etc/apt/keyrings/docker.asc \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/$ID $VERSION_CODENAME stable" \
      > /etc/apt/sources.list.d/docker.list \
 && for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin; then ok=1; break; fi; \
      echo "apt: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/*
""".ReplaceLineEndings("\n") + "\n";

    [Fact]
    public void MakeRendersExactlyItsFragment()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("make", null)]);

        Assert.Null(problem);
        Assert.Equal(Make, text);
        Assert.DoesNotContain("TARGETARCH", text);
    }

    [Fact]
    public void DockerRendersExactlyItsFragment()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("docker", null)]);

        Assert.Null(problem);
        Assert.Equal(Docker, text);
        Assert.DoesNotContain("TARGETARCH", text);
    }

    [Theory]
    [InlineData("make")]
    [InlineData("docker")]
    public void ARecipeWithNoVersionRefusesAPin(string id)
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency(id, "1")]);

        Assert.Null(text);
        Assert.Equal($"`{id}` installs whatever its package provides - declare it without a version", problem);
    }
}

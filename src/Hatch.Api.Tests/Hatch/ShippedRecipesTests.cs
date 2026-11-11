using System.Xml.Linq;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The recipes Hatch ships, through the real renderer: the exact text each
/// renders to, and what a declared version changes in the ones that take one.
/// CI builds and runs them.
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

    private static readonly string Node = """
# node: the newest release of one Node.js major from nodejs.org, checked against that release's SHASUMS256.txt and unpacked into /usr/local. A tarball rather than apt, which ships an older major.
ARG NODE_VERSION=22
ARG TARGETARCH
RUN case "$TARGETARCH" in \
      amd64) arch=x64 ;; \
      arm64) arch=arm64 ;; \
      *) echo "hatch recipe node: no build for TARGETARCH=$TARGETARCH" >&2; exit 1 ;; \
    esac \
 && base=https://nodejs.org/dist/latest-v${NODE_VERSION%%.*}.x \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 "$base/SHASUMS256.txt" -o /tmp/SHASUMS256.txt \
 && file=$(awk -v arch="$arch" '$2 ~ "^node-v[0-9.]+-linux-" arch "\\.tar\\.gz$" {print $2}' /tmp/SHASUMS256.txt) \
 && [ -n "$file" ] \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 "$base/$file" -o "/tmp/$file" \
 && (cd /tmp && grep " $file\$" SHASUMS256.txt | sha256sum -c -) \
 && tar -xzf "/tmp/$file" -C /usr/local --strip-components=1 --no-same-owner \
 && rm -f "/tmp/$file" /tmp/SHASUMS256.txt /usr/local/CHANGELOG.md /usr/local/LICENSE /usr/local/README.md
""".ReplaceLineEndings("\n") + "\n";

    private static readonly string Dotnet = """
# dotnet: the .NET SDK from Microsoft's dotnet-install.sh, which picks the architecture itself. A channel for a major or major.minor, an exact version for three components.
ARG DOTNET_VERSION=10.0
ENV DOTNET_ROOT=/usr/share/dotnet DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && case "$DOTNET_VERSION" in \
      *.*.*) flag=--version; value=$DOTNET_VERSION ;; \
      *.*) flag=--channel; value=$DOTNET_VERSION ;; \
      *) flag=--channel; value=$DOTNET_VERSION.0 ;; \
    esac \
 && bash /tmp/dotnet-install.sh "$flag" "$value" --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh \
 && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
""".ReplaceLineEndings("\n") + "\n";

    private static readonly string Pwsh = """
# pwsh: PowerShell from its GitHub release tarball into /opt/microsoft/powershell/<major>. A major means the newest release, which GitHub's releases/latest redirect names without a token; a full version names its own tag. DOTNET_SYSTEM_GLOBALIZATION_INVARIANT keeps the base free of ICU.
ARG PWSH_VERSION=7
ARG TARGETARCH
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
RUN case "$TARGETARCH" in \
      amd64) arch=x64 ;; \
      arm64) arch=arm64 ;; \
      *) echo "hatch recipe pwsh: no build for TARGETARCH=$TARGETARCH" >&2; exit 1 ;; \
    esac \
 && major=${PWSH_VERSION%%.*} \
 && case "$PWSH_VERSION" in \
      *.*.*) version=$PWSH_VERSION ;; \
      *.*) echo "hatch recipe pwsh: declare a major (7) or a full version (7.4.6), not $PWSH_VERSION" >&2; exit 1 ;; \
      *) tag=$(curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 -o /dev/null -w '%{url_effective}' https://github.com/PowerShell/PowerShell/releases/latest) \
         && version=${tag##*/v} ;; \
    esac \
 && [ "${version%%.*}" = "$major" ] || { echo "hatch recipe pwsh: the newest release is $version, not a $major.x" >&2; exit 1; } \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 "https://github.com/PowerShell/PowerShell/releases/download/v$version/powershell-$version-linux-$arch.tar.gz" -o /tmp/pwsh.tar.gz \
 && mkdir -p /opt/microsoft/powershell/$major \
 && tar -xzf /tmp/pwsh.tar.gz -C /opt/microsoft/powershell/$major --no-same-owner \
 && rm /tmp/pwsh.tar.gz \
 && chmod +x /opt/microsoft/powershell/$major/pwsh \
 && ln -s /opt/microsoft/powershell/$major/pwsh /usr/local/bin/pwsh
""".ReplaceLineEndings("\n") + "\n";

    private static readonly string Playwright = """
# playwright-browsers: Playwright's chromium and webkit with their system libraries, into /ms-playwright. The version must match the Microsoft.Playwright package the tests run, whose browser revisions it pins. Needs node, for npx.
ARG PLAYWRIGHT_VERSION=1.63.0
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN ok= ; for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && npx --yes playwright@$PLAYWRIGHT_VERSION install --with-deps chromium webkit; then ok=1; break; fi; \
      echo "playwright: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/* \
 && chmod -R a+rX /ms-playwright
""".ReplaceLineEndings("\n") + "\n";

    private static readonly string Postgres = """
# postgres: the server and client from PostgreSQL's own apt repo (PGDG), for one major, with its binaries linked onto PATH. The service is not started: a build that needs a database runs `pg_ctl` itself.
ARG POSTGRES_VERSION=17
RUN major=${POSTGRES_VERSION%%.*} \
 && mkdir -p -m 755 /etc/apt/keyrings \
 && . /etc/os-release \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 https://www.postgresql.org/media/keys/ACCC4CF8.asc \
      -o /etc/apt/keyrings/postgresql.asc \
 && chmod a+r /etc/apt/keyrings/postgresql.asc \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/postgresql.asc] https://apt.postgresql.org/pub/repos/apt $VERSION_CODENAME-pgdg main" \
      > /etc/apt/sources.list.d/pgdg.list \
 && ok= \
 && for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && apt-get install -y --no-install-recommends postgresql-$major postgresql-client-$major; then ok=1; break; fi; \
      echo "apt: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/* \
 && ln -s /usr/lib/postgresql/$major/bin/* /usr/local/bin/
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

    [Fact]
    public void NodeRendersExactlyItsFragment()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("node", null)]);

        Assert.Null(problem);
        Assert.Equal(Node, text);
        AssertBothArchitectures(text);
    }

    [Fact]
    public void DotnetRendersExactlyItsFragment()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("dotnet", null)]);

        Assert.Null(problem);
        Assert.Equal(Dotnet, text);
        Assert.DoesNotContain("TARGETARCH", text);
    }

    [Fact]
    public void PwshRendersExactlyItsFragment()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("pwsh", null)]);

        Assert.Null(problem);
        Assert.Equal(Pwsh, text);
        AssertBothArchitectures(text);
    }

    [Fact]
    public void ADeclaredNodeVersionChangesOnlyItsDefault()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("node", "20")]);

        Assert.Null(problem);
        Assert.Equal(Node.Replace("ARG NODE_VERSION=22", "ARG NODE_VERSION=20"), text);
    }

    [Fact]
    public void ADeclaredDotnetVersionChangesOnlyItsDefault()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("dotnet", "9.0")]);

        Assert.Null(problem);
        Assert.Equal(Dotnet.Replace("ARG DOTNET_VERSION=10.0", "ARG DOTNET_VERSION=9.0"), text);
    }

    [Fact]
    public void ADeclaredPwshVersionChangesOnlyItsDefault()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("pwsh", "7.4.6")]);

        Assert.Null(problem);
        Assert.Equal(Pwsh.Replace("ARG PWSH_VERSION=7", "ARG PWSH_VERSION=7.4.6"), text);
    }

    [Fact]
    public void NodeDefaultIsTheMajorInNvmrc()
    {
        var nvmrc = File.ReadAllText(Repository("src/Hatch.Web/.nvmrc")).Trim().TrimStart('v');

        Assert.Equal($"ARG NODE_VERSION={nvmrc}", FirstArg(Node));
    }

    [Fact]
    public void DotnetDefaultIsTheTargetFrameworkOfContracts()
    {
        var framework = XDocument.Load(Repository("src/Hatch.Contracts/Hatch.Contracts.csproj"))
            .Descendants("TargetFramework").Single().Value;

        Assert.Equal($"ARG DOTNET_VERSION={framework.Replace("net", "")}", FirstArg(Dotnet));
    }

    [Fact]
    public void PlaywrightBrowsersRendersExactlyItsFragment()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("playwright-browsers", null)]);

        Assert.Null(problem);
        Assert.Equal(DependencyRecipes.Recipe("node") + Playwright, text);
        Assert.DoesNotContain("TARGETARCH", Playwright);
    }

    [Fact]
    public void PostgresRendersExactlyItsFragment()
    {
        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("postgres", null)]);

        Assert.Null(problem);
        Assert.Equal(Postgres, text);
        Assert.DoesNotContain("TARGETARCH", text);
    }

    [Fact]
    public void ADeclaredPlaywrightVersionChangesOnlyItsDefault()
    {
        var expected = Playwright.Replace("ARG PLAYWRIGHT_VERSION=1.63.0", "ARG PLAYWRIGHT_VERSION=1.50.0");
        Assert.NotEqual(Playwright, expected);

        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("playwright-browsers", "1.50.0")]);

        Assert.Null(problem);
        Assert.Equal(DependencyRecipes.Recipe("node") + expected, text);
    }

    [Fact]
    public void ADeclaredPostgresVersionChangesOnlyItsDefault()
    {
        var expected = Postgres.Replace("ARG POSTGRES_VERSION=17", "ARG POSTGRES_VERSION=16");
        Assert.NotEqual(Postgres, expected);

        var (text, problem) = DependencyRecipes.Render([new DeclaredDependency("postgres", "16")]);

        Assert.Null(problem);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void PlaywrightBrowsersAloneRendersNodeFirst()
    {
        var expected = DependencyRecipes.Recipe("node") + Playwright;

        var (alone, aloneProblem) = DependencyRecipes.Render([new DeclaredDependency("playwright-browsers", null)]);
        var (both, bothProblem) = DependencyRecipes.Render([
            new DeclaredDependency("playwright-browsers", null),
            new DeclaredDependency("node", null)]);

        Assert.Null(aloneProblem);
        Assert.Null(bothProblem);
        Assert.Equal(expected, alone);
        Assert.Equal(expected, both);
    }

    [Fact]
    public void PlaywrightDefaultIsTheNuGetPackageVersion()
    {
        var version = XDocument.Load(Repository("src/Hatch.Api.Tests/Hatch.Api.Tests.csproj"))
            .Descendants("PackageReference")
            .Single(reference => (string?)reference.Attribute("Include") == "Microsoft.Playwright")
            .Attribute("Version")!.Value;

        Assert.Equal($"ARG PLAYWRIGHT_VERSION={version}", FirstArg(Playwright));
    }

    private static void AssertBothArchitectures(string text)
    {
        Assert.Contains("case \"$TARGETARCH\" in", text);
        Assert.Contains("amd64)", text);
        Assert.Contains("arm64)", text);
        Assert.Contains("*) echo \"hatch recipe ", text);
        Assert.Contains("no build for TARGETARCH=", text);
    }

    private static string FirstArg(string recipe) =>
        recipe.Split('\n').First(line => line.StartsWith("ARG ", StringComparison.Ordinal));

    /// <summary>
    /// Walks up from the test binary to the repository, rather than assuming
    /// how deep <c>bin/Debug/net10.0</c> happens to be today.
    /// </summary>
    private static string Repository(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}.");
    }
}

using System.Text;
using Hatch.Contracts;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>image dockerfile</c> - the text a checkout's declaration renders to, read
/// from a temp directory standing in for the checkout - and <c>image build</c>,
/// which feeds that text to a fake <c>docker</c> or <c>podman</c>.
/// </summary>
public sealed class ImageCommandTests : IDisposable
{
    private const string Json = """{"dependencies":[{"id":"dotnet","version":"10"},"make","node","playwright-browsers"]}""";

    private readonly string _root = Directory.CreateTempSubdirectory("hatch-image-").FullName;
    private readonly Transcript _say = new();

    private string Said => string.Concat(_say.Said);

    private string Complained => string.Join("\n", _say.Complained);

    private ImageCommand Command(
        string? variable = null,
        Func<IReadOnlyList<DeclaredDependency>, (string?, IReadOnlyList<DeclaredDependency>?, string?)>? renderer = null) =>
        new(_say)
        {
            Root = _root,
            Fold = name => name == "HATCH_RUNNER_BASE_IMAGE" && variable is not null
                ? (variable, Settings.Layer.Environment)
                : (null, Settings.Layer.Unset),
            Renderer = renderer ?? DependencyRecipes.Render,
        };

    private readonly List<ImageCommand.ToolCall> _calls = [];

    private List<ImageCommand.ToolCall> Builds => _calls.Where(c => c.Args[0] == "build").ToList();

    /// <summary>
    /// A command with a fake tool: <paramref name="answer"/> says what each call prints and exits with, and every
    /// call is kept in <see cref="_calls"/>. <paramref name="onPath"/> is what is installed.
    /// </summary>
    private ImageCommand Building(
        Func<ImageCommand.ToolCall, (int, string)>? answer = null,
        string? origin = "git@github.com:Owner/Repo.git",
        params string[] onPath) =>
        Command() with
        {
            Tool = call =>
            {
                _calls.Add(call);
                var (exit, output) = (answer ?? (_ => (0, "")))(call);
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) call.OnLine?.Invoke(line);
                return (exit, output);
            },
            OnPath = name => (onPath.Length == 0 ? ["docker"] : onPath).Contains(name),
            Origin = _ =>
            {
                _originAsked++;
                return origin;
            },
        };

    private int _originAsked;

    private const string Manifest3 = """
        Name:      docker.io/library/debian:bookworm
        MediaType: application/vnd.oci.image.index.v1+json

        Manifests:
          Name:        docker.io/library/debian:bookworm@sha256:aaa
          MediaType:   application/vnd.oci.image.manifest.v1+json
          Platform:    linux/amd64

          Name:        docker.io/library/debian:bookworm@sha256:bbb
          MediaType:   application/vnd.oci.image.manifest.v1+json
          Platform:    linux/arm64

          Name:        docker.io/library/debian:bookworm@sha256:ccc
          MediaType:   application/vnd.oci.image.manifest.v1+json
          Platform:    linux/arm/v7

          Name:        docker.io/library/debian:bookworm@sha256:ddd
          MediaType:   application/vnd.oci.image.manifest.v1+json
          Platform:    unknown/unknown
        """;

    private void Declare(string content)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".hatch"));
        File.WriteAllText(Path.Combine(_root, ".hatch", "dependencies.json"), content);
    }

    private void Fragment(string content) =>
        File.WriteAllBytes(Path.Combine(_root, ".hatch", "runner.Dockerfile"), new UTF8Encoding(false).GetBytes(content));

    private static string Baked(string json) =>
        "RUN mkdir -p /etc/hatch && cat > /etc/hatch/dependencies.json <<'DEPENDENCIES_JSON_END'\n" +
        json + "\n" +
        "DEPENDENCIES_JSON_END\n" +
        "LABEL hatch.dependencies=\"" + json.Replace("\"", "\\\"") + "\"\n";

    private string Fixture() =>
        """{"dependencies":["playwright-browsers","make",{"id":"dotnet","version":"10"}]}""";

    [Fact]
    public void It_prints_from_then_the_recipes_in_id_order_then_the_baked_block_and_nothing_else()
    {
        Declare(Fixture());

        Assert.Equal(0, Command().Run(["dockerfile", "--base", "debian:bookworm"]));

        // dotnet is pinned to 10 in the fixture, so its recipe's default 10.0 becomes 10; node comes in bare.
        var expected = "FROM debian:bookworm\n" +
            DependencyRecipes.Recipe("dotnet")!.Replace("ARG DOTNET_VERSION=10.0", "ARG DOTNET_VERSION=10", StringComparison.Ordinal) +
            DependencyRecipes.Recipe("make") +
            DependencyRecipes.Recipe("node") +
            DependencyRecipes.Recipe("playwright-browsers") +
            Baked(Json);
        Assert.Equal(expected, Said);
    }

    [Fact]
    public void The_same_declaration_in_any_order_renders_the_same_bytes()
    {
        Declare(Fixture());
        Command().Run(["dockerfile", "--base", "debian:bookworm"]);
        var first = Said;

        var again = new Transcript();
        Declare("""{"dependencies":[{"version":"10","id":"dotnet"},"playwright-browsers","make","node"]}""");
        new ImageCommand(again)
        {
            Root = _root,
            Fold = _ => (null, Settings.Layer.Unset),
        }.Run(["dockerfile", "--base", "debian:bookworm"]);

        Assert.Equal(first, string.Concat(again.Said));
    }

    [Fact]
    public void A_runner_dockerfile_sits_verbatim_between_the_recipes_and_the_baked_block()
    {
        Declare(Fixture());
        const string own = "﻿# mine\r\nRUN echo hi\r\nUSER root\n";
        Fragment(own);

        Assert.Equal(0, Command().Run(["dockerfile", "--base", "debian:bookworm"]));

        var recipes = Said.IndexOf("# make", StringComparison.Ordinal);
        var at = Said.IndexOf(own, StringComparison.Ordinal);
        Assert.True(at > recipes);
        Assert.Equal(Baked(Json), Said[(at + own.Length)..]);
    }

    [Fact]
    public void A_fragment_with_no_final_newline_gets_one_between_it_and_the_baked_block_and_its_bytes_are_untouched()
    {
        Declare(Fixture());
        Fragment("USER root");

        Command().Run(["dockerfile", "--base", "debian:bookworm"]);

        Assert.EndsWith("USER root\n" + Baked(Json), Said);
        Assert.Equal(1, Said.Split("USER root").Length - 1);
    }

    [Fact]
    public void A_fragment_with_a_final_newline_gets_no_extra_one()
    {
        Declare(Fixture());
        Fragment("USER root\n");

        Command().Run(["dockerfile", "--base", "debian:bookworm"]);

        Assert.EndsWith("USER root\n" + Baked(Json), Said);
    }

    [Fact]
    public void The_baked_file_and_the_label_carry_the_resolved_set_in_the_declarations_shape()
    {
        Declare("""{"dependencies":["playwright-browsers"]}""");

        Command().Run(["dockerfile", "--base", "debian:bookworm"]);

        Assert.EndsWith(Baked("""{"dependencies":["node","playwright-browsers"]}"""), Said);
    }

    [Fact]
    public void Base_wins_over_the_variable()
    {
        Declare("""{"dependencies":["make"]}""");

        Command(variable: "from-the-variable").Run(["dockerfile", "--base", "from-the-flag"]);

        Assert.StartsWith("FROM from-the-flag\n", Said);
    }

    [Fact]
    public void The_variable_stands_when_there_is_no_flag()
    {
        Declare("""{"dependencies":["make"]}""");

        Command(variable: "from-the-variable").Run(["dockerfile"]);

        Assert.StartsWith("FROM from-the-variable\n", Said);
    }

    [Fact]
    public void With_no_base_anywhere_it_refuses_naming_both()
    {
        Declare("""{"dependencies":["make"]}""");

        Assert.Equal(1, Command().Run(["dockerfile"]));

        Assert.Contains("--base", Complained);
        Assert.Contains("HATCH_RUNNER_BASE_IMAGE", Complained);
        Assert.Empty(_say.Said);
    }

    [Theory]
    [InlineData("debian bookworm")]
    [InlineData("debian\nRUN x")]
    public void A_base_with_a_space_or_line_break_is_refused(string image)
    {
        Declare("""{"dependencies":["make"]}""");

        Assert.Equal(1, Command().Run(["dockerfile", "--base", image]));

        Assert.Empty(_say.Said);
    }

    [Fact]
    public void A_missing_declaration_refuses_naming_the_path()
    {
        Assert.Equal(1, Command().Run(["dockerfile", "--base", "debian:bookworm"]));

        Assert.Equal($"hatch: no .hatch/dependencies.json at {Path.Combine(_root, ".hatch", "dependencies.json")}", Complained);
    }

    [Fact]
    public void A_bad_declaration_passes_the_parsers_sentence_on()
    {
        Declare("""{"dependencies":["make","make"]}""");

        Assert.Equal(1, Command().Run(["dockerfile", "--base", "debian:bookworm"]));

        Assert.Equal(
            $"hatch: {Path.Combine(_root, ".hatch", "dependencies.json")} entry 2: `make` is already declared by entry 1",
            Complained);
    }

    [Fact]
    public void A_dependency_with_no_recipe_refuses_by_name_and_says_the_repository_stays_on_host_runners()
    {
        Declare("""{"dependencies":["pwsh"]}""");

        Assert.Equal(1, Command(renderer: _ => (null, null, "`pwsh` is on the master list but ships no install recipe - a repository declaring it stays on host runners"))
            .Run(["dockerfile", "--base", "debian:bookworm"]));

        Assert.Contains("`pwsh`", Complained);
        Assert.Contains("stays on host runners", Complained);
        Assert.Empty(_say.Said);
    }

    [Fact]
    public void Help_names_the_subcommand_and_its_flag()
    {
        Assert.Equal(0, Command().Run(["--help"]));

        Assert.Contains("hatch image dockerfile [--base <image>]", Said);
        Assert.Contains("hatch image build [--base <image>] [--image <name>] [--platform <list>]", Said);
    }

    [Fact]
    public void Another_subcommand_is_refused_with_the_usage()
    {
        Assert.Equal(1, Command().Run(["push"]));

        Assert.Contains("usage: hatch image <dockerfile|build>", Complained);
    }

    // ---- build ----

    [Fact]
    public void Build_feeds_the_tool_exactly_what_dockerfile_prints_and_runs_it_at_the_root()
    {
        Declare(Fixture());
        Fragment("COPY a b\n");
        Command().Run(["dockerfile", "--base", "debian:bookworm"]);
        var printed = Said;
        var again = new Transcript();

        var command = Building() with { Say = again };
        Assert.Equal(0, command.Run(["build", "--base", "debian:bookworm", "--image", "reg/o/r:t", "--platform", "linux/arm64"]));

        var build = Assert.Single(Builds);
        Assert.Equal("docker", build.Tool);
        Assert.Equal(["build", "-f", "-", "--platform", "linux/arm64", "-t", "reg/o/r:t", "."], build.Args);
        Assert.Equal(printed, build.Stdin);
        Assert.Equal(_root, build.WorkingDirectory);
        Assert.Contains("hatch: built reg/o/r:t for linux/arm64", string.Concat(again.Said));
    }

    [Fact]
    public void A_failed_build_says_so_after_the_tools_own_output_and_exits_one()
    {
        Declare(Fixture());

        Assert.Equal(1, Building(call => call.Args[0] == "build" ? (3, "boom") : (0, ""))
            .Run(["build", "--base", "b", "--image", "i", "--platform", "linux/arm64"]));

        Assert.Equal("boom", Said);
        Assert.Equal("hatch: docker build failed", Complained);
    }

    [Fact]
    public void Build_refuses_what_dockerfile_does_before_it_looks_for_a_tool()
    {
        Assert.Equal(1, Building().Run(["build", "--base", "b", "--image", "i", "--platform", "linux/arm64"]));
        Assert.Contains("no .hatch/dependencies.json", Complained);

        Declare(Fixture());
        Assert.Equal(1, Building(onPath: "nothing").Run(["build", "--image", "i", "--platform", "linux/arm64"]));
        Assert.Contains("HATCH_RUNNER_BASE_IMAGE", Complained);
        Assert.Empty(_calls);
    }

    [Theory]
    [InlineData("--image")]
    [InlineData("--platform")]
    [InlineData("--wat")]
    public void Build_refuses_a_flag_with_no_value_or_that_it_does_not_take(string flag)
    {
        Declare(Fixture());

        Assert.Equal(1, Building().Run(["build", "--base", "b", flag]));

        Assert.Contains("usage: hatch image", Complained);
        Assert.Empty(_calls);
    }

    [Fact]
    public void A_flag_given_twice_or_a_value_that_is_a_flag_is_refused()
    {
        Declare(Fixture());

        Assert.Equal(1, Building().Run(["build", "--base", "a", "--base", "b"]));
        Assert.Equal(1, Building().Run(["build", "--base", "b", "--image", "--platform", "linux/arm64"]));
        Assert.Empty(_calls);
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("git@github.com:owner/repo", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("https://github.com/owner/repo.git", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("https://github.com/owner/repo", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("https://github.com/owner/repo/", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("https://user:tok%40en@github.com/owner/repo.git", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("https://x-access-token:abc@GitHub.com/owner/repo", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("ssh://git@github.com/owner/repo.git", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("ssh://git@github.com:22/owner/repo.git", "ghcr.io/owner/repo/hatch-runner")]
    [InlineData("git@github.com:YeetSoft/Hatch.git", "ghcr.io/yeetsoft/hatch/hatch-runner")]
    public void The_name_derives_from_a_github_origin_lowercased(string origin, string expected)
    {
        Declare(Fixture());

        Assert.Equal(0, Building(origin: origin).Run(["build", "--base", "b", "--platform", "linux/arm64"]));

        Assert.Equal(expected, Builds.Single().Args[6]);
    }

    [Theory]
    [InlineData("git@gitlab.example.org:owner/repo.git", "gitlab.example.org")]
    [InlineData("https://tok@git.example.org/owner/repo", "git.example.org")]
    [InlineData("ssh://git@git.example.org:2222/owner/repo", "git.example.org")]
    public void An_origin_on_another_host_refuses_naming_the_host_and_image(string origin, string host)
    {
        Declare(Fixture());

        Assert.Equal(1, Building(origin: origin).Run(["build", "--base", "b", "--platform", "linux/arm64"]));

        Assert.Equal($"hatch: cannot derive an image name from the origin host {host} - pass --image <name>", Complained);
        Assert.Empty(Builds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/srv/git/repo.git")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/group/repo")]
    public void No_origin_or_one_that_names_no_owner_and_repo_refuses_naming_image(string? origin)
    {
        Declare(Fixture());

        Assert.Equal(1, Building(origin: origin).Run(["build", "--base", "b", "--platform", "linux/arm64"]));

        Assert.Contains("--image", Complained);
        Assert.Empty(Builds);
    }

    [Fact]
    public void Image_wins_and_origin_is_never_asked()
    {
        Declare(Fixture());

        Assert.Equal(0, Building().Run(["build", "--base", "b", "--image", "mine:1", "--platform", "linux/arm64"]));

        Assert.Equal(0, _originAsked);
        Assert.Equal("mine:1", Builds.Single().Args[6]);
    }

    [Fact]
    public void An_image_with_whitespace_is_refused()
    {
        Declare(Fixture());

        Assert.Equal(1, Building().Run(["build", "--base", "b", "--image", "a b", "--platform", "linux/arm64"]));

        Assert.Empty(_calls);
    }

    [Fact]
    public void Platform_wins_and_the_base_is_never_inspected()
    {
        Declare(Fixture());

        Assert.Equal(0, Building().Run(["build", "--base", "b", "--image", "i", "--platform", "linux/arm64,linux/riscv64"]));

        Assert.Equal(["build"], _calls.Select(c => c.Args[0]));
        Assert.Equal("linux/arm64,linux/riscv64", Builds.Single().Args[4]);
    }

    [Theory]
    [InlineData("arm64")]
    [InlineData("linux/arm64,")]
    [InlineData("linux/arm64 linux/amd64")]
    public void A_platform_that_is_not_os_slash_arch_is_refused(string platform)
    {
        Declare(Fixture());

        Assert.Equal(1, Building().Run(["build", "--base", "b", "--image", "i", "--platform", platform]));

        Assert.Empty(_calls);
    }

    [Fact]
    public void Without_platform_they_are_read_off_the_base_manifest_list_skipping_attestations()
    {
        Declare(Fixture());

        var command = Building(call => call.Args is ["buildx", "imagetools", ..] ? (0, Manifest3) : (0, ""));
        // More than one platform: docker's own answer is what stops it, and the fake lets it through.
        Assert.Equal(0, command.Run(["build", "--base", "debian:bookworm", "--image", "i"]));

        Assert.Equal(["buildx", "imagetools", "inspect", "debian:bookworm"], _calls[0].Args);
        Assert.Equal("linux/amd64,linux/arm64,linux/arm/v7", Builds.Single().Args[4]);
    }

    [Fact]
    public void Where_buildx_fails_the_manifest_json_is_read()
    {
        Declare(Fixture());
        const string json = """
            {"schemaVersion":2,"manifests":[
              {"platform":{"architecture":"amd64","os":"linux"}},
              {"platform":{"architecture":"arm64","os":"linux","variant":"v8"}},
              {"platform":{"architecture":"unknown","os":"unknown"}}]}
            """;

        var command = Building(call => call.Args[0] switch
        {
            "buildx" => (1, "no such manifest"),
            "manifest" => (0, json),
            _ => (0, ""),
        });
        Assert.Equal(0, command.Run(["build", "--base", "b", "--image", "i"]));

        Assert.Equal("linux/amd64,linux/arm64/v8", Builds.Single().Args[4]);
    }

    [Fact]
    public void A_base_no_registry_has_falls_back_to_the_local_images_own_platform()
    {
        Declare(Fixture());

        var command = Building(call => call.Args[0] switch
        {
            "buildx" or "manifest" => (1, "not found"),
            "image" => (0, "linux/arm64\n"),
            _ => (0, ""),
        });
        Assert.Equal(0, command.Run(["build", "--base", "hatch-runner:ci", "--image", "i"]));

        Assert.Equal(["image", "inspect", "--format", "{{.Os}}/{{.Architecture}}", "hatch-runner:ci"], _calls[2].Args);
        Assert.Equal("linux/arm64", Builds.Single().Args[4]);
    }

    [Fact]
    public void Where_no_platform_can_be_read_it_refuses_naming_platform()
    {
        Declare(Fixture());

        Assert.Equal(1, Building(call => (1, "nope")).Run(["build", "--base", "b", "--image", "i"]));

        Assert.Contains("--platform", Complained);
        Assert.Contains("b", Complained);
        Assert.Empty(Builds);
    }

    [Fact]
    public void Podman_has_no_buildx_and_is_asked_by_its_own_name()
    {
        Declare(Fixture());

        var command = Building(call => call.Args[0] == "manifest" ? (0, """{"manifests":[{"platform":{"architecture":"arm64","os":"linux"}}]}""") : (0, ""),
            onPath: "podman");
        Assert.Equal(0, command.Run(["build", "--base", "b", "--image", "i"]));

        Assert.All(_calls, c => Assert.Equal("podman", c.Tool));
        Assert.DoesNotContain(_calls, c => c.Args[0] == "buildx");
        Assert.Equal(["build", "-f", "-", "--platform", "linux/arm64", "-t", "i", "."], Builds.Single().Args);
    }

    [Fact]
    public void Docker_is_preferred_when_both_are_on_path()
    {
        Declare(Fixture());

        Building(onPath: ["podman", "docker"]).Run(["build", "--base", "b", "--image", "i", "--platform", "linux/arm64"]);

        Assert.Equal("docker", Builds.Single().Tool);
    }

    [Fact]
    public void With_neither_it_refuses_in_one_sentence_naming_both()
    {
        Declare(Fixture());

        Assert.Equal(1, Building(onPath: "git").Run(["build", "--base", "b", "--image", "i", "--platform", "linux/arm64"]));

        Assert.Single(_say.Complained);
        Assert.Contains("docker", Complained);
        Assert.Contains("podman", Complained);
        Assert.Empty(_calls);
    }

    [Fact]
    public void A_multi_platform_build_that_the_tool_refuses_is_followed_by_a_line_naming_platform()
    {
        Declare(Fixture());

        Assert.Equal(1, Building(call => call.Args[0] == "build" ? (1, "ERROR: docker exporter does not support multi-platform") : (0, ""))
            .Run(["build", "--base", "b", "--image", "i", "--platform", "linux/amd64,linux/arm64"]));

        Assert.Equal(["hatch: docker build failed", "hatch: linux/amd64,linux/arm64 is more than one platform, which docker cannot load into the local image store - name one with --platform"], _say.Complained);
        Assert.Contains("does not support multi-platform", Said);
    }

    [Fact]
    public void No_platform_pair_is_written_into_the_source()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Hatch.Cli", "ImageCommand.cs"));

        Assert.DoesNotContain("linux/amd64,linux/arm64", source);
    }

    [Fact]
    public void The_parsers_read_literal_sample_output()
    {
        Assert.Equal(["linux/amd64", "linux/arm64", "linux/arm/v7"], ImageCommand.ParseImagetools(Manifest3));
        Assert.Empty(ImageCommand.ParseImagetools("Name: x\nMediaType: y\n"));
        Assert.Empty(ImageCommand.ParseManifestList("not json"));
        Assert.Empty(ImageCommand.ParseManifestList("""{"mediaType":"x"}"""));
        Assert.Empty(ImageCommand.ParseManifestList("[1]"));
        Assert.Equal(["linux/amd64"], ImageCommand.ParseLocal("linux/amd64\n"));
        Assert.Empty(ImageCommand.ParseLocal("<no value>/<no value>"));
        Assert.Empty(ImageCommand.ParseLocal(""));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Hatch.Cli", "ImageCommand.cs"))) return directory.FullName;
        }

        throw new InvalidOperationException("no repository above the test binary");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that will not go is not a failing test.
        }
    }
}

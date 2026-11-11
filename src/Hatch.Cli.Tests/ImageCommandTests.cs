using System.Text;
using Hatch.Contracts;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>image dockerfile</c> - the text a checkout's declaration renders to, read
/// from a temp directory standing in for the checkout.
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
    }

    [Fact]
    public void Another_subcommand_is_refused_with_the_usage()
    {
        Assert.Equal(1, Command().Run(["build"]));

        Assert.Contains("usage: hatch image dockerfile", Complained);
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

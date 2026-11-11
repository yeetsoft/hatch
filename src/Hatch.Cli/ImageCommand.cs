using System.Text;
using Hatch.Contracts;

namespace Hatch.Cli;

/// <summary>
/// The Dockerfile a repository's <c>.hatch/dependencies.json</c> renders to.
/// </summary>
/// <remarks>
/// <para>Reads the checkout and the machine-bound settings layers and nothing on
/// the board, so it works with no origin configured - the way
/// <see cref="UseCommand"/> and <see cref="ConfigCommand"/> do.</para>
///
/// <para>The text is a pure function of its inputs: <c>FROM</c>, the recipes of
/// the declared set closed over its requirements, the repository's own
/// <c>.hatch/runner.Dockerfile</c> as it was written when there is one, and a
/// closing block that bakes the resolved set into the image.</para>
/// </remarks>
public sealed record ImageCommand(Terminal Say)
{
    /// <summary>Where a repository's own Dockerfile fragment lives in its checkout.</summary>
    public const string FragmentPath = ".hatch/runner.Dockerfile";

    /// <summary>The image label the resolved set is baked to, and the key a runner reads it back by.</summary>
    public const string LabelKey = "hatch.dependencies";

    /// <summary>Where the resolved set is baked inside the image.</summary>
    public const string BakedPath = "/etc/hatch/dependencies.json";

    private const string Delimiter = "DEPENDENCIES_JSON_END";

    /// <summary>The checkout root, or null outside one.</summary>
    public string? Root { get; init; }

    /// <summary>The machine-bound settings, folded highest layer first - what <see cref="Settings.Layers"/> returns.</summary>
    public Func<string, (string? Value, Settings.Layer From)> Fold { get; init; } =
        _ => (null, Settings.Layer.Unset);

    /// <summary>
    /// The renderer. A seam, because every dependency Hatch knows ships a recipe and the table that could say
    /// otherwise is not reachable from here.
    /// </summary>
    public Func<IReadOnlyList<DeclaredDependency>, (string? Dockerfile, IReadOnlyList<DeclaredDependency>? Resolved, string? Problem)> Renderer
    { get; init; } = DependencyRecipes.Render;

    public static readonly string[] ImageUsage =
    [
        "usage: hatch image dockerfile [--base <image>]",
        "",
        "  hatch image dockerfile   print the Dockerfile this checkout's",
        "                              .hatch/dependencies.json renders to",
        "",
        "  FROM the base image, then each declared dependency's install recipe",
        "  (what a recipe requires is installed too) in id order, then",
        "  .hatch/runner.Dockerfile as written when the repository has one, then",
        "  the resolved set baked to /etc/hatch/dependencies.json and into the",
        "  `hatch.dependencies` label. The same declaration renders the same bytes.",
        "",
        "  --base <image> is the image to start from. Without it, HATCH_RUNNER_BASE_IMAGE",
        "  is read, which `hatch config --runner-base-image <image>` writes.",
        "",
        "  A runner.Dockerfile runs before the baked block, which needs root: leave",
        "  the user as root. A declaration naming a dependency with no install recipe",
        "  is refused - that repository stays on host runners.",
    ];

    public int Run(string[] args)
    {
        if (Usage.Wanted(args)) return Usage.Print(Say, ImageUsage);

        if (args is not ["dockerfile", ..])
            return Usage.Refuse(Say, "image takes `dockerfile`", ImageUsage);

        string? flag = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] != "--base" || i + 1 >= args.Length || flag is not null)
                return Usage.Refuse(Say, "image dockerfile takes only --base <image>", ImageUsage);

            flag = args[++i];
        }

        var declaration = Root is null ? null : Path.Combine(Root, DependencyDeclaration.Path);
        if (declaration is null || !File.Exists(declaration))
        {
            Say.Complain($"hatch: no {DependencyDeclaration.Path} at {declaration ?? Path.GetFullPath(DependencyDeclaration.Path)}");
            return 1;
        }

        var (declared, parseProblem) = DependencyDeclaration.Parse(File.ReadAllBytes(declaration));
        if (parseProblem is not null)
        {
            Say.Complain($"hatch: {declaration} {parseProblem}");
            return 1;
        }

        var (recipes, resolved, renderProblem) = Renderer(declared!);
        if (renderProblem is not null)
        {
            Say.Complain($"hatch: {renderProblem}");
            return 1;
        }

        var baseImage = (flag ?? Fold("HATCH_RUNNER_BASE_IMAGE").Value)?.Trim();
        if (string.IsNullOrEmpty(baseImage))
        {
            Say.Complain("hatch: no base image - pass --base <image> or set HATCH_RUNNER_BASE_IMAGE (`hatch config --runner-base-image <image>`)");
            return 1;
        }

        // It is spliced into the FROM line.
        if (baseImage.Any(char.IsWhiteSpace))
        {
            Say.Complain($"hatch: the base image \"{baseImage}\" has a space or line break in it - an image is one word, like debian:bookworm");
            return 1;
        }

        var text = new StringBuilder();
        text.Append("FROM ").Append(baseImage).Append('\n');
        text.Append(recipes);

        var fragment = Root is null ? null : Path.Combine(Root, FragmentPath);
        if (fragment is not null && File.Exists(fragment))
        {
            // As written: a BOM and CRLF stay.
            var own = new UTF8Encoding(false).GetString(File.ReadAllBytes(fragment));
            text.Append(own);
            if (own.Length > 0 && !own.EndsWith('\n')) text.Append('\n');
        }

        // The ids and versions were validated to leave no `$` or backslash in the
        // JSON, so a quoted heredoc delimiter and escaping the quote are enough.
        var json = DependencyDeclaration.Serialize(resolved!);
        text.Append($"RUN mkdir -p /etc/hatch && cat > {BakedPath} <<'{Delimiter}'\n");
        text.Append(json).Append('\n');
        text.Append(Delimiter).Append('\n');
        text.Append($"LABEL {LabelKey}=\"{json.Replace("\"", "\\\"")}\"\n");

        Say.Write(text.ToString());
        return 0;
    }
}

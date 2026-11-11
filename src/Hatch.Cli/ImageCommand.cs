using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hatch.Contracts;

namespace Hatch.Cli;

/// <summary>
/// The Dockerfile a repository's <c>.hatch/dependencies.json</c> renders to, and
/// the image built from it.
/// </summary>
/// <remarks>
/// <para>Reads the checkout and the machine-bound settings layers and nothing on
/// the board, so it works with no origin configured - the way
/// <see cref="UseCommand"/> and <see cref="ConfigCommand"/> do. <c>build</c>
/// additionally runs <c>docker</c> or <c>podman</c>, and still asks the board
/// nothing.</para>
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

    /// <summary>One run of an external tool: what to start, what to feed it, and where.</summary>
    /// <param name="OnLine">Called with each line of the tool's output as it arrives, when the caller wants it live.</param>
    public sealed record ToolCall(string Tool, IReadOnlyList<string> Args, string? Stdin, string? WorkingDirectory, Action<string>? OnLine);

    /// <summary>
    /// Runs a tool and returns its exit code and its output, standard output and standard error merged. A seam,
    /// because a test has neither Docker nor podman and must not need one.
    /// </summary>
    public Func<ToolCall, (int Exit, string Output)> Tool { get; init; } = RunTool;

    /// <summary>Whether a tool is on <c>PATH</c>. A seam, for the same reason as <see cref="Tool"/>.</summary>
    public Func<string, bool> OnPath { get; init; } = IsOnPath;

    /// <summary>The checkout's <c>origin</c> remote, or null. A seam, so a test need not run <c>git</c>.</summary>
    public Func<string, string?> Origin { get; init; } = Checkouts.Origin;

    /// <summary>
    /// The registry a forge's host names. Keyed on the forge because that is what a host says: an image name is
    /// <c>&lt;registry&gt;/&lt;owner&gt;/&lt;repo&gt;</c>, and only for a forge whose registry is known can the host
    /// stand for it. A host not listed has no derivation and is refused, naming <c>--image</c>.
    /// </summary>
    private static readonly Dictionary<string, string> RegistryByHost = new(StringComparer.OrdinalIgnoreCase)
    {
        ["github.com"] = "ghcr.io",
    };

    /// <summary>The image's name under the repository's own, when it is derived.</summary>
    private const string ImageLeaf = "hatch-runner";

    private static readonly Regex PlatformShape = new(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)?$", RegexOptions.Compiled);

    public static readonly string[] ImageUsage =
    [
        "usage: hatch image <dockerfile|build> ...",
        "",
        "  hatch image dockerfile [--base <image>]",
        "  hatch image build [--base <image>] [--image <name>] [--platform <list>] [--push]",
        "",
        "  hatch image dockerfile   print the Dockerfile this checkout's",
        "                              .hatch/dependencies.json renders to",
        "  hatch image build        build that Dockerfile with docker (or podman) and tag it",
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
        "  --image <name> (build) is the name to tag. Without it the name comes from",
        "  the checkout's origin remote: a github.com origin gives",
        "  ghcr.io/<owner>/<repo>/hatch-runner, lowercased. Any other host, or no origin,",
        "  is refused - pass --image.",
        "",
        "  --platform <list> (build) is a comma list of os/arch, such as linux/arm64.",
        "  Without it the platforms are read off the base image itself; where they",
        "  cannot be read, the build is refused - pass --platform. A classic local image",
        "  store cannot hold a list of more than one: name one platform, or add --push.",
        "",
        "  --push (build) pushes the image once it is built, under the name the build",
        "  resolved. A registry that refuses a name three segments deep (Docker Hub)",
        "  is tried once more under <registry>/<owner>/<repo>-hatch-runner, and the",
        "  line says which name the image reached. Only a rejection of the name itself",
        "  retries: a refusal of the login or the network fails with the tool's output.",
        "  With more than one platform, build and push are one step.",
        "",
        "  build runs docker, or podman where there is no docker, from the repository",
        "  root, so .hatch/runner.Dockerfile's COPY lines resolve against the checkout.",
        "",
        "  A runner.Dockerfile runs before the baked block, which needs root: leave",
        "  the user as root. A declaration naming a dependency with no install recipe",
        "  is refused - that repository stays on host runners.",
    ];

    public int Run(string[] args)
    {
        if (Usage.Wanted(args)) return Usage.Print(Say, ImageUsage);

        if (args is not ["dockerfile" or "build", ..])
            return Usage.Refuse(Say, "image takes `dockerfile` or `build`", ImageUsage);

        return args[0] == "dockerfile" ? Dockerfile(args) : Build(args);
    }

    private int Dockerfile(string[] args)
    {
        string? flag = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] != "--base" || i + 1 >= args.Length || flag is not null)
                return Usage.Refuse(Say, "image dockerfile takes only --base <image>", ImageUsage);

            flag = args[++i];
        }

        var (text, _, exit) = Render(flag);
        if (text is null) return exit;

        Say.Write(text);
        return 0;
    }

    private int Build(string[] args)
    {
        var flags = new Dictionary<string, string>();
        var push = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--push" && !push)
            {
                push = true;
                continue;
            }

            // A value that is itself a flag is a flag missing its value, and would reach the tool as an option.
            if (args[i] is not ("--base" or "--image" or "--platform") || i + 1 >= args.Length
                || args[i + 1].StartsWith('-') || !flags.TryAdd(args[i], args[++i]))
                return Usage.Refuse(Say, "image build takes only --base <image>, --image <name>, --platform <list> and --push, each once", ImageUsage);
        }

        var (text, baseImage, exit) = Render(flags.GetValueOrDefault("--base"));
        if (text is null) return exit;

        var tool = OnPath("docker") ? "docker" : OnPath("podman") ? "podman" : null;
        if (tool is null)
        {
            Say.Complain("hatch: neither docker nor podman is on PATH - install one to build an image");
            return 1;
        }

        string image;
        if (flags.TryGetValue("--image", out var named))
        {
            if (named.Any(char.IsWhiteSpace))
            {
                Say.Complain($"hatch: the image name \"{named}\" has a space or line break in it - an image is one word, like registry.example/team/runner:tag");
                return 1;
            }

            image = named;
        }
        else
        {
            var (derived, problem) = DeriveName(Root is null ? null : Origin(Root));
            if (derived is null)
            {
                Say.Complain($"hatch: {problem} - pass --image <name>");
                return 1;
            }

            image = derived;
        }

        string platforms;
        if (flags.TryGetValue("--platform", out var listed))
        {
            if (!listed.Split(',').All(PlatformShape.IsMatch))
            {
                Say.Complain($"hatch: the platform \"{listed}\" is not a comma list of os/arch, such as linux/arm64");
                return 1;
            }

            platforms = listed;
        }
        else
        {
            var read = BasePlatforms(tool, baseImage!);
            if (read.Count == 0)
            {
                Say.Complain($"hatch: cannot read the platforms of the base image {baseImage} - pass --platform <list>");
                return 1;
            }

            platforms = string.Join(',', read);
        }

        if (push && platforms.Contains(','))
            return PushWith(tool, image, (name, _) => BuildAndPush(tool, text, platforms, name));

        var (code, _) = Tool(new ToolCall(
            tool, ["build", "-f", "-", "--platform", platforms, "-t", image, "."], text, Root, Say.Line));
        if (code != 0)
        {
            Say.Complain($"hatch: {tool} build failed");
            if (platforms.Contains(','))
                Say.Complain($"hatch: {platforms} is more than one platform, which {tool} cannot load into the local image store - name one with --platform, or add --push to build and push it in one step");
            return 1;
        }

        Say.Line($"hatch: built {image} for {platforms}");
        return push ? PushWith(tool, image, (name, nested) => PushBuilt(tool, name, nested)) : 0;
    }

    /// <summary>One attempt at getting an image to the registry under <c>name</c>: whether it did, what the tool said, and the step that failed.</summary>
    private delegate (bool Pushed, string Output, string Label) Attempt(string name, string? nested);

    /// <summary>
    /// Pushes <paramref name="nested"/>; when the registry refuses the name itself, once more under its flat form.
    /// Two attempts at most, and the second's failure is final.
    /// </summary>
    private int PushWith(string tool, string nested, Attempt attempt)
    {
        var (pushed, output, label) = attempt(nested, null);
        if (pushed)
        {
            Say.Line($"hatch: pushed {nested}");
            return 0;
        }

        var flat = FlatName(nested);
        if (flat is not null && NameRejected(output))
        {
            (pushed, _, label) = attempt(flat, nested);
            if (pushed)
            {
                Say.Line($"hatch: pushed {flat} (the registry rejected {nested})");
                return 0;
            }
        }

        Say.Complain($"hatch: {label} failed");
        return 1;
    }

    /// <summary>A built image's push; on the retry it is first tagged with the flat name, so nothing is rebuilt.</summary>
    private (bool, string, string) PushBuilt(string tool, string name, string? nested)
    {
        if (nested is not null)
        {
            var (tagged, tagOutput) = Tool(new ToolCall(tool, ["tag", nested, name], null, Root, Say.Line));
            if (tagged != 0) return (false, tagOutput, $"{tool} tag");
        }

        var (exit, output) = Tool(new ToolCall(tool, ["push", name], null, Root, Say.Line));
        return (exit == 0, output, $"{tool} push");
    }

    /// <summary>
    /// Build and push in one step, since a manifest list cannot be loaded into the classic local store.
    /// docker's buildx pushes as it builds; podman builds a manifest and pushes it all.
    /// </summary>
    private (bool, string, string) BuildAndPush(string tool, string text, string platforms, string name)
    {
        if (tool == "docker")
        {
            var (exit, output) = Tool(new ToolCall(
                tool, ["buildx", "build", "-f", "-", "--platform", platforms, "-t", name, "--push", "."], text, Root, Say.Line));
            return (exit == 0, output, "docker buildx build --push");
        }

        // A manifest left by an earlier run would take this run's images in as well. Its exit is ignored, and so is
        // its output: that there was none to remove is not news.
        Tool(new ToolCall(tool, ["manifest", "rm", name], null, Root, null));

        var (built, buildOutput) = Tool(new ToolCall(
            tool, ["build", "-f", "-", "--platform", platforms, "--manifest", name, "."], text, Root, Say.Line));
        if (built != 0) return (false, buildOutput, "podman build");

        var (sent, sentOutput) = Tool(new ToolCall(
            tool, ["manifest", "push", "--all", name, $"docker://{name}"], null, Root, Say.Line));
        return (sent == 0, sentOutput, "podman manifest push");
    }

    private const string FlatTail = "-" + ImageLeaf;

    /// <summary>
    /// The name a registry that nests only two deep takes: <c>&lt;registry&gt;/&lt;owner&gt;/&lt;repo&gt;/hatch-runner</c>
    /// as <c>&lt;registry&gt;/&lt;owner&gt;/&lt;repo&gt;-hatch-runner</c>. Null for any other shape - a name of the
    /// operator's own has no flat form to guess at.
    /// </summary>
    internal static string? FlatName(string nested)
    {
        const string tail = "/" + ImageLeaf;
        if (!nested.EndsWith(tail, StringComparison.Ordinal)) return null;

        var head = nested[..^tail.Length];
        var parts = head.Split('/');
        return parts.Length >= 3 && parts.All(part => part.Length > 0) ? head + FlatTail : null;
    }

    /// <summary>What a registry or tool says when it is the login or the network that failed. Checked first, and it wins.</summary>
    private static readonly string[] NotTheName =
    [
        "unauthorized", "denied", "authentication required", "forbidden", "no such host",
        "timeout", "timed out", "connection refused", "i/o timeout",
    ];

    /// <summary>What a registry or tool says of a repository name it will not take.</summary>
    private static readonly string[] TheName =
    [
        "invalid reference format", "invalid repository name", "repository name must be", "name invalid",
        "NAME_INVALID", "repository name not valid", "invalid repository",
    ];

    /// <summary>
    /// Whether a failed push's output reads as the registry refusing the name, rather than the login or the
    /// network. Anything that is neither is not a reason to try another name.
    /// </summary>
    internal static bool NameRejected(string output) =>
        !NotTheName.Any(phrase => output.Contains(phrase, StringComparison.OrdinalIgnoreCase))
        && TheName.Any(phrase => output.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The Dockerfile text and the base it starts from, or the exit code after saying why there is none.
    /// </summary>
    private (string? Text, string? Base, int Exit) Render(string? baseFlag)
    {
        var declaration = Root is null ? null : Path.Combine(Root, DependencyDeclaration.Path);
        if (declaration is null || !File.Exists(declaration))
        {
            Say.Complain($"hatch: no {DependencyDeclaration.Path} at {declaration ?? Path.GetFullPath(DependencyDeclaration.Path)}");
            return (null, null, 1);
        }

        var (declared, parseProblem) = DependencyDeclaration.Parse(File.ReadAllBytes(declaration));
        if (parseProblem is not null)
        {
            Say.Complain($"hatch: {declaration} {parseProblem}");
            return (null, null, 1);
        }

        var (recipes, resolved, renderProblem) = Renderer(declared!);
        if (renderProblem is not null)
        {
            Say.Complain($"hatch: {renderProblem}");
            return (null, null, 1);
        }

        var baseImage = (baseFlag ?? Fold("HATCH_RUNNER_BASE_IMAGE").Value)?.Trim();
        if (string.IsNullOrEmpty(baseImage))
        {
            Say.Complain("hatch: no base image - pass --base <image> or set HATCH_RUNNER_BASE_IMAGE (`hatch config --runner-base-image <image>`)");
            return (null, null, 1);
        }

        // It is spliced into the FROM line.
        if (baseImage.Any(char.IsWhiteSpace))
        {
            Say.Complain($"hatch: the base image \"{baseImage}\" has a space or line break in it - an image is one word, like debian:bookworm");
            return (null, null, 1);
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

        return (text.ToString(), baseImage, 0);
    }

    /// <summary>
    /// <c>&lt;registry&gt;/&lt;owner&gt;/&lt;repo&gt;/hatch-runner</c>, lowercased, from an origin remote - or
    /// the sentence for why there is none.
    /// </summary>
    internal static (string? Name, string? Problem) DeriveName(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return (null, "there is no origin remote to derive an image name from");

        var (host, path) = SplitRemote(origin.Trim());
        if (host is null) return (null, "cannot derive an image name from this origin, which names no host");
        if (!RegistryByHost.TryGetValue(host, out var registry))
            return (null, $"cannot derive an image name from the origin host {host}");

        var parts = path!.Trim('/') is var trimmed && trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4].Split('/')
            : trimmed.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrEmpty))
            return (null, $"cannot derive an image name from the origin path {path} - it is not <owner>/<repo>");

        return ($"{registry}/{parts[0]}/{parts[1]}/{ImageLeaf}".ToLowerInvariant(), null);
    }

    /// <summary>
    /// A remote's host and path, in <c>https://[creds@]host/path</c>, <c>ssh://[user@]host[:port]/path</c> and
    /// scp's <c>[user@]host:path</c> - a bare local path has neither.
    /// </summary>
    private static (string? Host, string? Path) SplitRemote(string remote)
    {
        string authority, path;
        var scheme = remote.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var rest = remote[(scheme + 3)..];
            var slash = rest.IndexOf('/');
            (authority, path) = slash < 0 ? (rest, "") : (rest[..slash], rest[slash..]);
            authority = authority[(authority.LastIndexOf('@') + 1)..];
            var port = authority.LastIndexOf(':');
            if (port >= 0) authority = authority[..port];
        }
        else
        {
            var colon = remote.IndexOf(':');
            if (colon <= 0 || remote.StartsWith('/')) return (null, null);
            authority = remote[..colon];
            authority = authority[(authority.LastIndexOf('@') + 1)..];
            path = remote[(colon + 1)..];
        }

        return authority.Length == 0 ? (null, null) : (authority, path);
    }

    /// <summary>
    /// The platforms of the base image, as the build will want them: the registry's manifest list, then - for a
    /// base only this machine holds, such as one just built - the local image's own platform. Empty when neither reads.
    /// </summary>
    private IReadOnlyList<string> BasePlatforms(string tool, string baseImage)
    {
        // `buildx imagetools` is docker's; podman has no equivalent and starts at its manifest.
        var steps = new List<(string[] Args, Func<string, IReadOnlyList<string>> Parse)>();
        if (tool == "docker") steps.Add((["buildx", "imagetools", "inspect", baseImage], ParseImagetools));
        steps.Add((["manifest", "inspect", baseImage], ParseManifestList));
        steps.Add((["image", "inspect", "--format", "{{.Os}}/{{.Architecture}}", baseImage], ParseLocal));

        foreach (var (args, parse) in steps)
        {
            var (exit, output) = Tool(new ToolCall(tool, args, null, Root, null));
            if (exit != 0) continue;

            var read = parse(output);
            if (read.Count > 0) return read;
        }

        return [];
    }

    /// <summary>The <c>Platform:</c> lines of <c>docker buildx imagetools inspect</c>, minus the attestation manifests.</summary>
    internal static IReadOnlyList<string> ParseImagetools(string output) =>
        output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("Platform:", StringComparison.Ordinal))
            .Select(line => line["Platform:".Length..].Trim())
            .Where(Real)
            .Distinct()
            .ToArray();

    /// <summary>The <c>manifests[].platform</c> of <c>docker manifest inspect</c>'s JSON, minus the attestation manifests.</summary>
    internal static IReadOnlyList<string> ParseManifestList(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("manifests", out var manifests)
                || manifests.ValueKind != JsonValueKind.Array)
                return [];

            var platforms = new List<string>();
            foreach (var manifest in manifests.EnumerateArray())
            {
                if (manifest.ValueKind != JsonValueKind.Object
                    || !manifest.TryGetProperty("platform", out var platform)
                    || platform.ValueKind != JsonValueKind.Object)
                    continue;

                var os = Text(platform, "os");
                var architecture = Text(platform, "architecture");
                var variant = Text(platform, "variant");
                if (os is null || architecture is null) continue;

                var one = variant is null ? $"{os}/{architecture}" : $"{os}/{architecture}/{variant}";
                if (Real(one) && !platforms.Contains(one)) platforms.Add(one);
            }

            return platforms;
        }
        catch (JsonException)
        {
            return [];
        }

        static string? Text(JsonElement of, string name) =>
            of.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s
                ? s
                : null;
    }

    /// <summary>One <c>os/arch</c> line, as <c>image inspect --format '{{.Os}}/{{.Architecture}}'</c> prints it.</summary>
    internal static IReadOnlyList<string> ParseLocal(string output)
    {
        var one = output.Trim();
        return PlatformShape.IsMatch(one) && Real(one) ? [one] : [];
    }

    /// <summary>An attestation manifest rides in a manifest list as <c>unknown/unknown</c>; it is not a platform.</summary>
    private static bool Real(string platform) => PlatformShape.IsMatch(platform) && !platform.StartsWith("unknown/", StringComparison.Ordinal);

    private static bool IsOnPath(string name)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, file)));
    }

    /// <summary>
    /// The default <see cref="Tool"/>: no shell, the arguments one by one, the text on standard input and then
    /// closed, the output merged and handed on line by line as it comes.
    /// </summary>
    private static (int Exit, string Output) RunTool(ToolCall call)
    {
        var start = new ProcessStartInfo
        {
            FileName = call.Tool,
            RedirectStandardInput = call.Stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = call.Stdin is null ? null : new UTF8Encoding(false),
        };
        if (call.WorkingDirectory is not null) start.WorkingDirectory = call.WorkingDirectory;
        foreach (var argument in call.Args) start.ArgumentList.Add(argument);

        var gate = new Lock();
        var output = new StringBuilder();
        void Take(string? line)
        {
            if (line is null) return;
            lock (gate)
            {
                output.Append(line).Append('\n');
                call.OnLine?.Invoke(line);
            }
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null) return (127, $"could not start {call.Tool}");

            process.OutputDataReceived += (_, e) => Take(e.Data);
            process.ErrorDataReceived += (_, e) => Take(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (call.Stdin is not null)
            {
                try
                {
                    process.StandardInput.Write(call.Stdin);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // It stopped reading - its exit code says why.
                }
            }

            process.WaitForExit();
            return (process.ExitCode, output.ToString());
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Take(e.Message);
            return (127, output.ToString());
        }
    }
}

namespace Hatch.Cli;

/// <summary>
/// A dream put on the Dream Board: <c>dream</c>, for a thought that is not yet
/// a ticket.
/// </summary>
/// <remarks>
/// Open to a key. The server sets the author and holds the length caps; its
/// refusal reaches the terminal as it is, so no cap is repeated here.
/// </remarks>
public sealed class DreamCommand(Cli cli)
{
    public static readonly string[] DreamUsage =
    [
        "usage: hatch dream \"<words>\" [--source <ref>] [--project <KEY>]",
        "",
        "  Puts a dream on the Dream Board.",
        "",
        "  The words are markdown. --source is free text saying where the dream came from",
        "  - a session id, a URL. --project is a lean, not a home: the project the dream",
        "  leans toward, by key.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, DreamUsage);

        string? words = null;
        string? source = null;
        string? projectKey = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--source")
            {
                if (i + 1 >= args.Length) return Usage.Refuse(cli.Say, "--source takes a reference", DreamUsage);
                source = args[++i];
                continue;
            }

            if (arg == "--project")
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    return Usage.Refuse(cli.Say, "--project takes a key", DreamUsage);
                projectKey = args[++i].Trim();
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
                return Usage.Refuse(cli.Say, $"dream does not take \"{arg}\"", DreamUsage);

            if (words is not null)
                return Usage.Refuse(cli.Say, "dream takes its words as one argument - quote them", DreamUsage);

            words = arg;
        }

        if (string.IsNullOrWhiteSpace(words))
            return Usage.Refuse(cli.Say, "dream needs some words", DreamUsage);

        // The route takes only an id; hatch speaks keys, so the key is resolved
        // here, before anything is posted.
        int? projectId = null;
        if (projectKey is not null)
        {
            var projects = await cli.Board.ProjectsAsync(ct);
            var match = projects.FirstOrDefault(p => p.Key.Equals(projectKey, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                cli.Say.Complain($"hatch: there is no project {projectKey}");
                return 1;
            }

            projectId = match.Id;
        }

        var trimmedSource = source?.Trim();
        var dream = await cli.Board.AddDreamAsync(
            new DreamCreateRequest(words.Trim(), string.IsNullOrEmpty(trimmedSource) ? null : trimmedSource, projectId), ct);
        if (dream is null)
        {
            cli.Say.Complain("hatch: the server answered with no dream");
            return 1;
        }

        cli.Say.Line($"posted dream {dream.Id} as {dream.Author}");
        return 0;
    }
}

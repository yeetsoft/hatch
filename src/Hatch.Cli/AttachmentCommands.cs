using System.Net;

namespace Hatch.Cli;

/// <summary>
/// Sending a file to the board and fetching one from it, so a session needs no
/// other tool for either.
/// </summary>
/// <remarks>
/// <para><c>add</c> prints the one line to paste into a comment or a
/// description - the same two forms the web page's <c>attachmentMarkdown</c>
/// writes: an image inline, anything else as a link.</para>
///
/// <para><c>get</c> never writes into the checkout: with no <c>--out</c> the
/// file goes under the system temporary directory, in a folder of its own id so
/// two files called <c>image.png</c> never overwrite each other. The stored
/// name came from whoever uploaded the file, so it is never trusted as a path.</para>
///
/// <para>A refusal the server wrote is a <see cref="HatchException"/> and is
/// left to <c>Program</c>, which prints it as it came and exits 1. A <c>404</c>
/// is the exception: the shared sentence says "no such issue", which is wrong
/// for a file, so each verb says its own.</para>
/// </remarks>
/// <param name="temp">
/// The system temporary directory; <see cref="Path.GetTempPath"/> when null.
/// How a test keeps from writing into the real one.
/// </param>
public sealed class AttachmentCommands(Cli cli, string? temp = null)
{
    private static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    public static readonly string[] AttachmentUsage =
    [
        "usage: hatch attachment add <file> [--project <KEY>]",
        "       hatch attachment get <id> [--out <path>]",
        "",
        "  Send a file to the board, and fetch one from it.",
        "",
        "  add   uploads the file and prints one line to paste into a comment or a",
        "        description: ![name](/api/images/<id>) for a PNG, JPEG, GIF or WebP,",
        "        [name](/api/attachments/<id>) for anything else. A person's key may",
        "        leave --project out; a program's key may not. The most it takes is",
        "        25 MB.",
        "  get   saves the file under its stored name in the system temporary",
        "        directory, never in the checkout, and prints the path, the content",
        "        type and the size on one line each. --out saves to that path",
        "        instead; a directory that exists there means inside it, under the",
        "        stored name.",
    ];

    public async Task<int> AttachmentAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, AttachmentUsage);
        if (args.Length == 0) return Usage.Refuse(cli.Say, "attachment takes a verb", AttachmentUsage);

        var verb = args[0];
        var rest = args[1..];
        if (Usage.Wanted(rest)) return Usage.Print(cli.Say, AttachmentUsage);

        return verb switch
        {
            "add" => await AddAsync(rest, ct),
            "get" => await GetAsync(rest, ct),
            _ => Usage.Refuse(cli.Say, $"attachment does not do {verb}", AttachmentUsage),
        };
    }

    private async Task<int> AddAsync(string[] args, CancellationToken ct)
    {
        if (Parse("add", args, "file", "--project") is not { } given) return 1;

        var file = given.Positional;
        if (!File.Exists(file))
        {
            cli.Say.Complain($"hatch: {file} is not a file");
            return 1;
        }

        var path = "/api/attachments";
        if (given.Flag is { } projectKey)
        {
            var projects = await cli.Board.ProjectsAsync(ct);
            var match = projects.FirstOrDefault(p => p.Key.Equals(projectKey, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                cli.Say.Complain($"hatch: there is no project {projectKey}");
                return 1;
            }

            path += $"?projectId={match.Id}";
        }

        AttachmentDto? sent;
        try
        {
            sent = await cli.Board.Client.PostAsync<AttachmentDto>(path, new FileBody(file), ct);
        }
        catch (HatchException e) when (e.Status == HttpStatusCode.NotFound)
        {
            cli.Say.Complain("hatch: that project is not one this key can reach");
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            cli.Say.Complain($"hatch: {e.Message}");
            return 1;
        }

        if (sent is null)
        {
            cli.Say.Complain("hatch: the board answered with no attachment");
            return 1;
        }

        cli.Say.Line(Markdown(sent));
        return 0;
    }

    private async Task<int> GetAsync(string[] args, CancellationToken ct)
    {
        if (Parse("get", args, "id", "--out") is not { } given) return 1;

        // Parsed before it goes near a URL, so nothing odd is ever sent.
        if (!Guid.TryParse(given.Positional, out var id))
        {
            Usage.Refuse(cli.Say, $"{given.Positional} is not an attachment id", AttachmentUsage);
            return 1;
        }

        FetchedFile fetched;
        try
        {
            fetched = await cli.Board.Client.GetFileAsync($"/api/attachments/{id}", ct);
        }
        catch (HatchException e) when (e.Status == HttpStatusCode.NotFound)
        {
            cli.Say.Complain($"hatch: no such attachment {id}");
            return 1;
        }

        var name = SafeName(fetched.FileName, id);
        try
        {
            var destination = Destination(given.Flag, id, name);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            await File.WriteAllBytesAsync(destination, fetched.Bytes, ct);

            cli.Say.Line(Path.GetFullPath(destination));
            cli.Say.Line(fetched.ContentType);
            cli.Say.Line($"{fetched.Bytes.Length} bytes");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            cli.Say.Complain($"hatch: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Where the bytes go: <paramref name="outPath"/> when it names a file, inside
    /// it when it names a directory that exists, and otherwise a folder of the
    /// attachment's own id under the temporary directory.
    /// </summary>
    private string Destination(string? outPath, Guid id, string name)
    {
        if (outPath is null)
            return Path.Combine(temp ?? Path.GetTempPath(), "hatch-attachments", id.ToString(), name);

        return Directory.Exists(outPath) ? Path.Combine(outPath, name) : outPath;
    }

    /// <summary>
    /// The stored name as a bare file name. It was typed by whoever uploaded the
    /// file, so a path in it is cut down to its last part, a character the
    /// system cannot put in a name is dropped, and nothing left means the id.
    /// </summary>
    public static string SafeName(string? stored, Guid id)
    {
        var name = (stored ?? "").Trim().Trim('"').Replace('\\', '/');
        name = Path.GetFileName(name);
        name = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return name is "" or "." or ".." ? id.ToString() : name;
    }

    /// <summary>
    /// The line to paste: an image inline, anything else as a link - the forms of
    /// <c>attachmentMarkdown</c> in <c>lib/descriptionImage.ts</c>. The name is
    /// escaped for <c>\</c>, <c>[</c> and <c>]</c>, and a control character
    /// becomes a space so the answer stays one line.
    /// </summary>
    public static string Markdown(AttachmentDto a)
    {
        var image = ImageTypes.Contains(a.ContentType);
        var name = string.Concat((a.FileName ?? "").Select(c => char.IsControl(c) ? " " : c.ToString()))
            .Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
        if (name.Length == 0) name = image ? "image" : "attachment";

        return image ? $"![{name}](/api/images/{a.Id})" : $"[{name}](/api/attachments/{a.Id})";
    }

    private sealed record Given(string Positional, string? Flag);

    /// <summary>
    /// One positional argument and at most one flag, refusing before anything is
    /// sent an unknown flag, a flag with no value, a flag said twice, or the
    /// wrong number of positional arguments. Null means the refusal is printed.
    /// </summary>
    private Given? Parse(string verb, string[] args, string name, string flagName)
    {
        var positional = new List<string>();
        string? flag = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == flagName)
            {
                if (i + 1 >= args.Length || args[i + 1].Trim().Length == 0)
                {
                    Usage.Refuse(cli.Say, $"{flagName} needs a value", AttachmentUsage);
                    return null;
                }

                if (flag is not null)
                {
                    Usage.Refuse(cli.Say, $"{flagName} is said twice", AttachmentUsage);
                    return null;
                }

                flag = args[++i];
            }
            else if (args[i].StartsWith('-') && args[i].Length > 1)
            {
                Usage.Refuse(cli.Say, $"attachment {verb} does not take {args[i]}", AttachmentUsage);
                return null;
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count != 1 || positional[0].Length == 0)
        {
            Usage.Refuse(cli.Say, $"attachment {verb} takes <{name}>", AttachmentUsage);
            return null;
        }

        return new Given(positional[0], flag);
    }
}

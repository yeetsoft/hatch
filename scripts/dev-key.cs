// Makes sure the development .env.json holds a secret key at Hatch:SecretKey.
//
//   dotnet run scripts/dev-key.cs ./src/Hatch.Api/.env.json
//
// Called by `make run` and `make up`, for the one machine a developer runs
// Hatch from source on. Hatch itself never makes a key (HA-2721); this is the
// build's own convenience. Exit codes, which the Makefile reads:
//
//    0   a key is already there; nothing was written
//   10   a key was made and the file written
//    1   something is in the way; one sentence on stderr says what
//
// A key already there is never replaced: replacing it would leave every secret
// sealed with it unreadable. The key itself is never printed.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dev-key.cs <path to .env.json>");
    return 1;
}

var path = Path.GetFullPath(args[0]);

// Docker makes a directory at a bind-mount path that does not exist yet, so a
// clone that ran `docker compose up` before this has a directory where the
// file belongs. An empty one is the mount point and is safe to replace.
if (Directory.Exists(path))
{
    if (Directory.EnumerateFileSystemEntries(path).Any())
    {
        Console.Error.WriteLine(
            $"{path} is a directory with files in it. Docker makes a directory there when `docker compose up` runs before the file exists. Move its contents out or delete it, then run again.");
        return 1;
    }

    Directory.Delete(path);
}

JsonObject root;
if (File.Exists(path))
{
    // Comments and trailing commas are read the way the config provider reads them.
    try
    {
        var node = JsonNode.Parse(
            File.ReadAllText(path),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        root = node as JsonObject ?? throw new JsonException("the top level is not an object");
    }
    catch (JsonException e)
    {
        Console.Error.WriteLine($"{path} is not a JSON object ({e.Message}). It was left as it was; fix it or delete it, then run again.");
        return 1;
    }
}
else
{
    root = new JsonObject();
}

// Configuration keys are case-insensitive, and a flat "Hatch:SecretKey" name is
// the same key as the nested one.
static string? Find(JsonObject o, string name) =>
    o.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

static bool HasKey(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s);

var flat = Find(root, "Hatch:SecretKey");
if (flat is not null && HasKey(root[flat])) return 0;

var hatchName = Find(root, "Hatch");
if (hatchName is not null && root[hatchName] is JsonObject existing)
{
    var keyName = Find(existing, "SecretKey");
    if (keyName is not null && HasKey(existing[keyName])) return 0;
}

JsonObject hatch;
if (hatchName is null)
{
    hatch = new JsonObject();
    root["Hatch"] = hatch;
}
else if (root[hatchName] is JsonObject o)
{
    hatch = o;
}
else if (root[hatchName] is null)
{
    hatch = new JsonObject();
    root[hatchName] = hatch;
}
else
{
    Console.Error.WriteLine($"{path} has a \"{hatchName}\" that is not an object, so a key cannot go under it. It was left as it was; fix it or delete it, then run again.");
    return 1;
}

var secretName = Find(hatch, "SecretKey") ?? "SecretKey";
hatch[secretName] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(
    path,
    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

Console.WriteLine($"made a development secret key in {path}");
return 10;

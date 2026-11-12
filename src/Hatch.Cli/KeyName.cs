namespace Hatch.Cli;

/// <summary>
/// What a loop needs of its key: that there is one, and the name it carries.
/// </summary>
/// <remarks>
/// A runner's name is its key's - the server says it, at <c>GET /api/me</c>,
/// and nothing on this side chooses, records or sends another. Kept out of
/// <c>Program.cs</c>, whose top level a test cannot reach.
/// </remarks>
public static class LoopKey
{
    /// <summary>What a keyless loop is told, once, before it takes a lock, a claim or makes a request.</summary>
    public const string Refusal = "hatch: a loop needs a key - run `hatch config` to create one.";

    /// <summary>
    /// Whether this command is a loop that has no key to be one with.
    /// </summary>
    /// <remarks>
    /// <c>-h</c> as the first argument is let through, since usage is not a
    /// run - the same first-argument rule <see cref="Usage.Wanted"/> applies
    /// everywhere else.
    /// </remarks>
    public static bool Refuses(string command, string[] rest, Settings settings) =>
        command is "work" or "go-to-work" or "do-my-work"
        && string.IsNullOrWhiteSpace(settings.Key)
        && !Usage.Wanted(rest);

    /// <summary>
    /// The key's own name: the empty string with no key, the <c>Name</c> of one
    /// <c>GET /api/me</c> for a key, and null where that read failed or
    /// the caller it described was not a key.
    /// </summary>
    public static async Task<string?> NameAsync(
        Settings settings, CancellationToken ct, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(settings.Key)) return "";

        try
        {
            using var client = new HatchClient(settings, handler);
            var me = await new Board(client).MeAsync(ct);
            return me is { Kind: "key" } ? me.Name : null;
        }
        catch (HatchException)
        {
            return null;
        }
    }
}

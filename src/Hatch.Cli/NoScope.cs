namespace Hatch.Cli;

/// <summary>
/// The one sentence a key says when its scope reaches no project, so a night
/// of <c>hatch next</c> or <c>go-to-work</c> does not read as a board with
/// nothing on it.
/// </summary>
/// <remarks>
/// The server enforces scope on every read; this only explains. It fails open
/// for that reason - a server that predates <c>scope</c>, a 204, or a refused
/// <c>/api/me</c> all read as "carry on", and the loop then meets whatever the
/// board says as it always did.
/// </remarks>
public static class NoScope
{
    public const string Sentence =
        "hatch: no project is in this key's scope - an admin sets it on Manage → Users";

    /// <summary>True only when the server says this caller's scope holds no project.</summary>
    public static async Task<bool> EmptyAsync(Board board, CancellationToken ct)
    {
        try
        {
            return (await board.MeAsync(ct))?.Scope?.Mode == "none";
        }
        catch (HatchException)
        {
            return false;
        }
    }

    /// <summary>Says the sentence on the error stream, so what a script reads from stdout is never mistaken for a ticket key.</summary>
    public static async Task<bool> SaidAsync(Cli cli, CancellationToken ct)
    {
        if (!await EmptyAsync(cli.Board, ct)) return false;

        cli.Say.Complain(Sentence);
        return true;
    }
}

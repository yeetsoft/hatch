using Hatch.Contracts;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The sentence for each kind of runner failure, in one place on the server.
/// They are the words <c>hatch runners</c> and the runner's page already use, so
/// a person reads the same thing in a channel as at the terminal.
/// </summary>
public static class RunnerFailureWords
{
    private static readonly IReadOnlyDictionary<string, string> Sentences = new Dictionary<string, string>
    {
        [RunnerFailureKinds.Forge] = "Cannot reach its forge",
        [RunnerFailureKinds.Signing] = "Cannot sign its commits",
        [RunnerFailureKinds.AgentSignedOut] = "Its agent is signed out",
        [RunnerFailureKinds.AgentNotStarting] = "Its agent will not start",
        [RunnerFailureKinds.BoardPaused] = "Paused by the board",
    };

    /// <summary>Whether this kind has a sentence. A test holds every kind in <see cref="RunnerFailureKinds.All"/> to it.</summary>
    public static bool Has(string kind) => Sentences.ContainsKey(kind);

    /// <summary>What a failure of this kind is. A kind this build does not know comes back as it is, so a beat never throws over a word.</summary>
    public static string Sentence(string kind) => Sentences.TryGetValue(kind, out var words) ? words : kind;
}

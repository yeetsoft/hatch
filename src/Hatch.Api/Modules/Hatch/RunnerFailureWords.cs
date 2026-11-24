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

    /// <summary>
    /// What to do about each kind, word for word as <c>runnerFailureAdvice</c> in the web app says it,
    /// so a person is told the same thing in a message as on the page.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Advices = new Dictionary<string, string>
    {
        [RunnerFailureKinds.Forge] = "Sign gh in on that machine.",
        [RunnerFailureKinds.Signing] = "Fix signing on that machine, or allow unsigned commits on the project.",
        [RunnerFailureKinds.AgentSignedOut] = "Sign the agent in on that machine.",
        [RunnerFailureKinds.AgentNotStarting] = "Read what the agent said, on the runner's page.",
        [RunnerFailureKinds.BoardPaused] = "Read why on the runner's page, where the pause can be lifted.",
    };

    private const string DefaultAdvice = "Open the runner's page to see what it cannot do.";

    /// <summary>Whether this kind has its own advice. A test holds every kind in <see cref="RunnerFailureKinds.All"/> to it.</summary>
    public static bool HasAdvice(string kind) => Advices.ContainsKey(kind);

    /// <summary>What to do about a failure of this kind; a kind this build does not know gets a pointer to the runner's page.</summary>
    public static string Advice(string kind) => Advices.TryGetValue(kind, out var words) ? words : DefaultAdvice;

    /// <summary>Whether this kind has a sentence. A test holds every kind in <see cref="RunnerFailureKinds.All"/> to it.</summary>
    public static bool Has(string kind) => Sentences.ContainsKey(kind);

    /// <summary>What a failure of this kind is. A kind this build does not know comes back as it is, so a beat never throws over a word.</summary>
    public static string Sentence(string kind) => Sentences.TryGetValue(kind, out var words) ? words : kind;
}

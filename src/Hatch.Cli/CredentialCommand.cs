using System.Text;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// The credential this Hatch holds for an agent, printed once as
/// <c>NAME=value</c>, for the container runner's entrypoint to export.
/// </summary>
/// <remarks>
/// <para>The container carries <c>git</c>, the agent CLIs and this binary, and
/// nothing else - no <c>python</c>, no <c>jq</c>, no <c>openssl</c>. So the
/// shell script that starts the loop has no way of its own to unwrap what
/// <c>GET /api/hatch/settings/credentials/{agent}</c> answers with, and this is
/// that way: one call, one line on standard output, nothing else anywhere. The
/// line names the variable the agent's CLI reads for headless use, so the
/// entrypoint exports it without knowing any vendor's vocabulary.</para>
///
/// <para>Not interactive, and named so nobody types it by accident. A person
/// who wants to know whether a credential is set looks at the Settings page,
/// which answers that without ever showing one.</para>
///
/// <para>Three exits, and the middle one is the whole reason this is a command
/// rather than a <c>hatch api</c> call: <c>0</c> with the line on stdout,
/// <c>2</c> for a Hatch that holds no credential for that agent yet - which is
/// an ordinary state a friend is in between starting the stack and pasting one,
/// and which the entrypoint waits out - and <c>1</c> for everything else, which
/// is a network or a refusal and is worth saying out loud.</para>
/// </remarks>
public sealed class CredentialCommand(Cli cli)
{
    /// <summary>
    /// No credential is set. Its own code so the entrypoint can tell "not yet"
    /// from "something is wrong" without reading stderr and guessing.
    /// </summary>
    public const int NoCredential = 2;

    public static readonly string[] CredentialUsage =
    [
        "usage: hatch runner-credential <agent>",
        "",
        "  Prints the credential this Hatch holds for an agent, decoded, on standard",
        "  output as NAME=value - the variable that agent's CLI reads for headless",
        "  use. For the container runner's entrypoint, not for interactive use.",
        "",
        "  <agent> is claude, codex, grok or ollama. ollama needs none and exits 0 printing nothing.",
        "",
        "  Exits 0 having printed one, 2 where this Hatch holds none yet, and 1",
        "  where the call itself failed. Refused outright by a Hatch with its",
        "  wall on - see docs/hatch.md, \"API surface\".",
    ];

    /// <summary>
    /// The variable an agent's CLI reads its headless credential from, or null
    /// for an agent this build has never heard of, and for ollama, which has
    /// nothing to store. Lives here and not on the
    /// server or on <c>IAgentAdapter</c>: this is the side that knows vendor
    /// vocabulary. Each was read off the vendor's own page and confirmed live
    /// on 2026-10-20 - claude: <c>https://code.claude.com/docs/en/authentication</c>
    /// (the long-lived token <c>claude setup-token</c> mints, for CI);
    /// codex: <c>https://developers.openai.com/codex/noninteractive</c> and
    /// <c>.../codex/environment-variables</c> (read by <c>codex exec</c> only,
    /// not interactive login); grok: <c>https://docs.x.ai/build/settings</c>
    /// (what the Grok Build CLI resolves to when no session token is active).
    /// Names drift between releases - re-pull the pages rather than trusting
    /// this list if a newer CLI is installed by the time this is read again.
    /// </summary>
    public static string? Variable(string agent) => agent switch
    {
        "claude" => "CLAUDE_CODE_OAUTH_TOKEN",
        "codex" => "CODEX_API_KEY",
        "grok" => "XAI_API_KEY",
        _ => null,
    };

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, CredentialUsage);
        if (args.Length != 1)
            return Usage.Refuse(cli.Say, "runner-credential takes exactly one argument, the agent", CredentialUsage);

        var agent = args[0];
        if (Variable(agent) is not { } variable)
        {
            if (Settings.KnownAgentIds.Contains(agent))
            {
                cli.Say.Complain($"hatch: {agent} needs no credential");
                return 0;
            }

            return Usage.Refuse(
                cli.Say,
                $"runner-credential does not know an agent called \"{agent}\" (known: {string.Join(", ", Settings.KnownAgentIds)})",
                CredentialUsage);
        }

        var path = $"/api/hatch/settings/credentials/{agent}";

        // Through Send rather than the throwing read: the 204 is an answer here
        // and not a fault, and it has an exit code of its own.
        var answer = await cli.Board.Client.Send(HttpMethod.Get, path, null, ct);

        if (!answer.Ok)
        {
            cli.Say.Complain(cli.Board.Client.Refusal(answer, path));
            return 1;
        }

        if (answer.Body.Trim().Length == 0) return NoCredential;

        CredentialDto? wrapped;
        try
        {
            wrapped = JsonSerializer.Deserialize(answer.Body, HatchJson.Default.CredentialDto);
        }
        catch (JsonException e)
        {
            cli.Say.Complain($"hatch: {path} answered with something that is not a credential: {e.Message}");
            return 1;
        }

        if (Secret.Reveal(wrapped?.ProtectedToken) is not { Length: > 0 } credential)
        {
            // A value that will not decode is a Hatch newer than this binary -
            // a scheme it has never heard of - or a row somebody hand-edited.
            // Unreadable is the honest answer either way, and it is not "no
            // credential": the entrypoint should say something rather than wait.
            cli.Say.Complain(
                $"hatch: {cli.Board.Client.Origin} answered with a credential this build cannot read - is it newer than this runner?");
            return 1;
        }

        cli.Say.Line($"{variable}={credential}");
        return 0;
    }
}

/// <summary>
/// The other half of <c>SecretProtector</c>, on this side of the wire.
/// </summary>
/// <remarks>
/// <para>A copy of about fifteen lines of
/// <c>src/Hatch.Api/Common/SecretObfuscator.cs</c> and its
/// <c>SecretProtector</c> wrapper, rather than a reference to
/// <c>Hatch.Api.Common</c>: the API and this CLI ship as separate,
/// independently-trimmed binaries onto different machines, and one of them is a
/// container that pulls the other over HTTP. The scheme tag is what makes the
/// copy safe to have - a payload written under a scheme this build has never
/// heard of comes back null and says so, rather than decoding into
/// nonsense.</para>
///
/// <para>The same "one symbol to change" note that class carries applies here
/// too, and doubled: when the protection becomes real crypto, this is the
/// second place a scheme has to be taught. It is obfuscation and not encryption
/// - a fixed key, in the open, on both sides - and it is honestly labelled as
/// such in docs/secrets-architecture.md. What keeps a token off the wire in
/// clear is TLS, which is the operator's to put in front of Hatch.</para>
/// </remarks>
internal static class Secret
{
    /// <summary>Byte for byte <c>SecretObfuscator.Key</c>, and it has to stay that way.</summary>
    private static readonly byte[] Key = "Hatch.SiteSettings.Obfuscation.v1"u8.ToArray();

    /// <summary>
    /// The plaintext behind a protected value, or null where there is not one -
    /// absent, blank, damaged past reading, or written under a scheme this
    /// build does not know.
    /// </summary>
    public static string? Reveal(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        // Untagged is v1 too: base64 has no colon in its alphabet, so a value
        // written before the tag existed cannot be mistaken for a tagged one.
        var separator = stored.IndexOf(':');
        var payload = separator < 0 ? stored : stored[(separator + 1)..];
        if (separator >= 0 && stored[..separator] != "v1") return null;

        try
        {
            var bytes = Convert.FromBase64String(payload);
            for (var i = 0; i < bytes.Length; i++) bytes[i] ^= Key[i % Key.Length];

            var plaintext = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrWhiteSpace(plaintext) ? null : plaintext;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

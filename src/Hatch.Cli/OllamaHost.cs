namespace Hatch.Cli;

/// <summary>
/// The Ollama host an agent talks to, resolved from whichever of the two
/// environment variables an operator set - this type reads neither itself;
/// the two strings are passed in by whoever reads the environment.
/// </summary>
/// <remarks>
/// Mirrors Ollama's own <c>envconfig.Host()</c> and the normalisation its test
/// table (<c>envconfig/config_test.go</c>'s <c>TestHost</c>) asserts: a blank
/// value with no scheme defaults to <c>http</c>; <c>host:port</c> splits the
/// way Go's <c>net.SplitHostPort</c> does, including the empty host a bare
/// <c>:port</c> leaves behind, and a split that cannot find an unbracketed
/// port falls back to the literal string as the host (or <c>127.0.0.1</c> only
/// when that string is itself empty); a port that is not an integer in
/// 0-65535 falls back to the scheme's default port without touching the host;
/// and the bare literal <c>ollama.com</c> is Ollama's own hardcoded special
/// case, forced to <c>https://ollama.com:443</c>. <c>0.0.0.0</c> is passed
/// through unchanged here - the <c>0.0.0.0</c>-to-<c>127.0.0.1</c> substitution
/// belongs to Ollama's separate <c>ConnectableHost()</c>, which this does not
/// mirror.
/// </remarks>
public sealed record OllamaHost(string Url, bool Explicit)
{
    /// <summary>Ollama's own default, used when neither environment variable was set.</summary>
    public static readonly OllamaHost Default = new("http://127.0.0.1:11434", false);

    /// <summary>
    /// The first non-blank of <paramref name="hatchOllamaHost"/> (Hatch's own
    /// override) and <paramref name="ollamaHost"/> (Ollama's own variable),
    /// normalised; <see cref="Default"/> when both are blank.
    /// </summary>
    public static OllamaHost Resolve(string? hatchOllamaHost, string? ollamaHost)
    {
        var chosen = FirstNonBlank(hatchOllamaHost) ?? FirstNonBlank(ollamaHost);
        return chosen is null ? Default : new OllamaHost(Normalise(chosen), true);
    }

    private static string? FirstNonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalise(string value)
    {
        if (value == "ollama.com")
        {
            return "https://ollama.com:443";
        }

        var defaultPort = "11434";
        string scheme;
        string hostport;
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            scheme = "http";
            hostport = value;
        }
        else
        {
            scheme = value[..schemeEnd];
            hostport = value[(schemeEnd + 3)..];
            defaultPort = scheme switch
            {
                "http" => "80",
                "https" => "443",
                _ => defaultPort,
            };
        }

        var path = "";
        var slash = hostport.IndexOf('/');
        if (slash >= 0)
        {
            path = hostport[slash..];
            hostport = hostport[..slash];
        }
        if (path.EndsWith('/'))
        {
            path = path[..^1];
        }

        var (host, port, ok) = SplitHostPort(hostport);
        if (!ok)
        {
            host = hostport.Length == 0 ? "127.0.0.1" : hostport;
            port = defaultPort;
        }

        if (!int.TryParse(port, out var portNumber) || portNumber < 0 || portNumber > 65535)
        {
            port = defaultPort;
        }

        var hostOut = host.Contains(':') ? $"[{host}]" : host;
        return $"{scheme}://{hostOut}:{port}{path}";
    }

    /// <summary>
    /// Go's <c>net.SplitHostPort</c>: a bracketed host keeps everything
    /// between the brackets and the port after the matching close bracket's
    /// colon; otherwise the host is everything before the last colon, and a
    /// colon still found inside that (an unbracketed IPv6 address) fails the
    /// split the same way a hostport with no colon at all does - an empty
    /// host before a bare <c>:port</c> is not that failure and is kept as
    /// given.
    /// </summary>
    private static (string Host, string Port, bool Ok) SplitHostPort(string hostport)
    {
        var last = hostport.LastIndexOf(':');
        if (last < 0)
        {
            return ("", "", false);
        }

        if (hostport.Length > 0 && hostport[0] == '[')
        {
            var close = hostport.IndexOf(']');
            if (close < 0 || close + 1 == hostport.Length || close + 1 != last)
            {
                return ("", "", false);
            }

            return (hostport[1..close], hostport[(last + 1)..], true);
        }

        var host = hostport[..last];
        if (host.Contains(':'))
        {
            return ("", "", false);
        }

        return (host, hostport[(last + 1)..], true);
    }
}

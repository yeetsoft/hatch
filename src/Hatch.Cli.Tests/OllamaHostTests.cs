namespace Hatch.Cli.Tests;

/// <summary>
/// Cross-checked row by row against Ollama's own <c>envconfig/config_test.go</c>
/// <c>TestHost</c> table, fetched from <c>github.com/ollama/ollama</c>.
/// </summary>
public sealed class OllamaHostTests
{
    [Theory]
    [InlineData(null, null, "http://127.0.0.1:11434", false)]
    [InlineData("", "  ", "http://127.0.0.1:11434", false)]
    [InlineData("host", null, "http://host:11434", true)]
    [InlineData("host:1234", null, "http://host:1234", true)]
    [InlineData("http://host", null, "http://host:80", true)]
    [InlineData("https://host", null, "https://host:443", true)]
    [InlineData("http://host:8080", null, "http://host:8080", true)]
    [InlineData("HOST", null, "http://HOST:11434", true)]
    [InlineData("127.0.0.1", null, "http://127.0.0.1:11434", true)]
    [InlineData(":11434", null, "http://:11434", true)]
    [InlineData("0.0.0.0:11434", null, "http://0.0.0.0:11434", true)]
    [InlineData("::1", null, "http://[::1]:11434", true)]
    [InlineData("[::1]:9", null, "http://[::1]:9", true)]
    [InlineData("host:99999", null, "http://host:11434", true)]
    [InlineData("host:abc", null, "http://host:11434", true)]
    [InlineData("http://host:8080/prefix/", null, "http://host:8080/prefix", true)]
    [InlineData("ollama.com", null, "https://ollama.com:443", true)]
    [InlineData("a", "b", "http://a:11434", true)]
    [InlineData("  ", "b", "http://b:11434", true)]
    public void Resolves_the_way_Ollamas_own_Host_does(
        string? hatchOllamaHost, string? ollamaHost, string expectedUrl, bool expectedExplicit)
    {
        var host = OllamaHost.Resolve(hatchOllamaHost, ollamaHost);

        Assert.Equal(expectedUrl, host.Url);
        Assert.Equal(expectedExplicit, host.Explicit);
    }
}

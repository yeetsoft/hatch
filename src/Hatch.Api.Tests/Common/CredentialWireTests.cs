using Hatch.Api.Common;

namespace Hatch.Api.Tests.Common;

/// <summary>
/// The wire to the container runner is <c>v1</c> on purpose and is not how
/// Hatch stores anything. These tests pin it with strings written out in full,
/// so a change to the wire fails here rather than in a container already built.
/// The same literal is read by the CLI's own decoder in <c>Hatch.Cli.Tests</c>.
/// </summary>
public class CredentialWireTests
{
    [Fact]
    public void A_known_credential_wraps_to_a_known_string()
    {
        Assert.Equal("v1:OwpZAgZafgYVEX5U", CredentialWire.Wrap("sk-ant-oat-1"));
        Assert.Equal("v1:IBQaFw1cYQ==", CredentialWire.Wrap("hunter2"));
    }

    [Fact]
    public void Wrap_then_unwrap_round_trips()
    {
        Assert.Equal("sk-ant-oat-1", CredentialWire.Unwrap(CredentialWire.Wrap("sk-ant-oat-1")));
    }

    [Fact]
    public void A_non_ascii_credential_round_trips()
    {
        var wrapped = CredentialWire.Wrap("clé-ключ-鍵");

        Assert.StartsWith("v1:", wrapped, StringComparison.Ordinal);
        Assert.Equal("clé-ключ-鍵", CredentialWire.Unwrap(wrapped));
    }

    [Theory]
    [InlineData("v2:OwpZAgZafgYVEX5U")]
    [InlineData("v1:not base64!")]
    [InlineData("OwpZAgZafgYVEX5U")]
    [InlineData("")]
    [InlineData(null)]
    public void A_wrong_tag_or_bad_base64_unwraps_to_null(string? wrapped)
    {
        Assert.Null(CredentialWire.Unwrap(wrapped));
    }
}

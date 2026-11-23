using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Modules;
using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Modules;

/// <summary>
/// Direct construction, no host, the way <see cref="AssetLinksValidationTests"/> is.
/// Keys are made at run time; none is written in a tracked file.
/// </summary>
public class SecretKeyValidationTests
{
    private static string NewKey(int length = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string current, string previous) =>
        new SecretKeyValidation().Validate(null, new HatchOptions { SecretKey = current, PreviousSecretKey = previous });

    [Fact]
    public void Validate_Succeeds_WhenNeitherKeyIsSet()
        => Assert.True(Validate("", "").Succeeded);

    [Theory]
    [InlineData(" ")]
    [InlineData("   \t ")]
    public void Validate_TreatsABlankValueAsNotSet(string blank)
    {
        Assert.True(Validate(blank, blank).Succeeded);
    }

    [Fact]
    public void Validate_Succeeds_ForTwoGoodKeys()
        => Assert.True(Validate(NewKey(), NewKey()).Succeeded);

    [Fact]
    public void Validate_Succeeds_ForAPreviousKeyAlone()
        => Assert.True(Validate("", NewKey()).Succeeded);

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Validate_Fails_ForTheCurrentKey_OfTheWrongLength(int length)
    {
        var value = NewKey(length);

        var result = Validate(value, "");

        Assert.True(result.Failed);
        var message = Assert.Single(result.Failures!);
        Assert.Contains("HATCH_SECRET_KEY", message);
        Assert.DoesNotContain("HATCH_PREVIOUS_SECRET_KEY", message);
        Assert.Contains("base64 of exactly 32 bytes", message);
        Assert.Contains("openssl rand -base64 32", message);
        Assert.DoesNotContain(value, message);
    }

    [Fact]
    public void Validate_Fails_ForThePreviousKey_OfTheWrongLength()
    {
        var value = NewKey(33);

        var result = Validate(NewKey(), value);

        Assert.True(result.Failed);
        var message = Assert.Single(result.Failures!);
        Assert.Contains("HATCH_PREVIOUS_SECRET_KEY", message);
        Assert.DoesNotContain(value, message);
    }

    [Fact]
    public void Validate_Fails_ForValuesThatAreNotBase64()
    {
        const string notBase64 = "this is *not* base64!";

        var current = Validate(notBase64, "");
        var previous = Validate("", notBase64);

        Assert.Contains("HATCH_SECRET_KEY", Assert.Single(current.Failures!));
        Assert.Contains("HATCH_PREVIOUS_SECRET_KEY", Assert.Single(previous.Failures!));
        Assert.DoesNotContain(notBase64, string.Join(" ", current.Failures!.Concat(previous.Failures!)));
    }

    [Fact]
    public void Validate_NamesBothVariables_WhenBothAreBad()
    {
        var result = Validate("nope", "nope");

        Assert.Equal(2, result.Failures!.Count());
        Assert.Contains(result.Failures!, f => f.Contains("HATCH_SECRET_KEY"));
        Assert.Contains(result.Failures!, f => f.Contains("HATCH_PREVIOUS_SECRET_KEY"));
    }

    [Fact]
    public void TryParse_ReturnsTheBytes_OfAGoodKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        Assert.True(SecretKeys.TryParse(Convert.ToBase64String(bytes), out var key));
        Assert.Equal(bytes, key);
    }

    [Fact]
    public void TryParse_ReturnsNoKey_ForABlankValue()
    {
        Assert.True(SecretKeys.TryParse(null, out var none));
        Assert.Null(none);
        Assert.True(SecretKeys.TryParse("  ", out var blank));
        Assert.Null(blank);
    }
}

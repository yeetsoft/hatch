using System.Reflection;
using System.Security.Cryptography;
using Hatch.Api.Common;

namespace Hatch.Api.Tests.Common;

/// <summary>
/// The protector over a real cipher, no host. Every key is made here at run
/// time: no key, real or example, goes in a tracked file (docs/ethos.md).
///
/// The property these tests exist to pin is that the *format* can change under
/// the callers: a value written by any scheme this build knows reads back,
/// including the untagged ones written before schemes existed, and that nothing
/// new is written weakly.
/// </summary>
public class SecretProtectorTests
{
    private const string Row = "site-setting/CodexApiKey";

    // The awkward string SecretCipherTests round-trips: the characters that break a URL.
    private const string Awkward = "p@ss:w/rd?#&=%[]";

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static SecretProtector WithKey(byte[]? current = null, byte[]? previous = null) =>
        new(new SecretCipher(current ?? NewKey(), previous));

    private static SecretProtector WithNoKey() => new(new SecretCipher(null, null));

    private static string Sealed(SecretProtector protector, string plaintext, string row = Row)
    {
        Assert.True(protector.TrySeal(plaintext, row, out var stored));
        return stored;
    }

    [Fact]
    public void The_current_scheme_is_v2()
    {
        Assert.Equal("v2", SecretProtector.CurrentScheme);
    }

    [Fact]
    public void A_sealed_value_reads_back()
    {
        var protector = WithKey();

        Assert.Equal("hunter2", (protector.Unprotect(Sealed(protector, "hunter2"), Row))?.Reveal());
    }

    [Fact]
    public void A_sealed_value_is_tagged_v2_with_a_payload_of_three_dotted_parts()
    {
        // The tag is the whole mechanism, so it is asserted directly rather
        // than only through a round trip that would pass without it.
        var stored = Sealed(WithKey(), "hunter2");

        Assert.StartsWith("v2:", stored, StringComparison.Ordinal);
        Assert.Equal(3, stored["v2:".Length..].Split('.').Length);
        Assert.DoesNotContain("hunter2", stored);
    }

    [Fact]
    public void Round_trips_a_secret_full_of_url_metacharacters()
    {
        var protector = WithKey();

        Assert.Equal(Awkward, (protector.Unprotect(Sealed(protector, Awkward), Row))?.Reveal());
    }

    [Fact]
    public void A_value_sealed_for_one_row_does_not_open_for_another()
    {
        var protector = WithKey();
        var stored = Sealed(protector, "hunter2");

        Assert.Null(protector.Unprotect(stored, "site-setting/XaiApiKey"));
    }

    [Fact]
    public void Two_seals_of_the_same_secret_store_different_text()
    {
        var protector = WithKey();

        Assert.NotEqual(Sealed(protector, "hunter2"), Sealed(protector, "hunter2"));
    }

    [Fact]
    public void The_previous_key_opens_what_it_sealed_but_seals_nothing()
    {
        var oldKey = NewKey();
        var stored = Sealed(WithKey(oldKey), "hunter2");

        var rotated = WithKey(NewKey(), oldKey);

        Assert.Equal("hunter2", (rotated.Unprotect(stored, Row))?.Reveal());
        Assert.NotEqual(stored.Split(':')[1].Split('.')[0], Sealed(rotated, "hunter2").Split(':')[1].Split('.')[0]);
    }

    [Fact]
    public void A_value_sealed_under_a_key_that_is_gone_reads_as_absent()
    {
        var stored = Sealed(WithKey(), "hunter2");

        Assert.Null(WithKey().Unprotect(stored, Row));
        Assert.Null(WithNoKey().Unprotect(stored, Row));
    }

    [Fact]
    public void V1_and_untagged_values_read_with_no_key()
    {
        // What every row in the database looked like before sealing. If this
        // ever fails, every stored secret in every installation is unreadable
        // to a Hatch that has not been given a key.
        var legacy = SecretObfuscator.Obfuscate("legacy-token");
        var protector = WithNoKey();

        Assert.Equal("legacy-token", (protector.Unprotect($"v1:{legacy}", Row))?.Reveal());
        Assert.Equal("legacy-token", (protector.Unprotect(legacy, Row))?.Reveal());
    }

    [Fact]
    public void V1_and_untagged_values_read_the_same_whatever_row_asks()
    {
        // v1 never carried a row name, so none can be checked.
        var legacy = SecretObfuscator.Obfuscate("legacy-token");

        Assert.Equal("legacy-token", (WithKey().Unprotect($"v1:{legacy}", "chat-workspace/slack/T1"))?.Reveal());
    }

    [Fact]
    public void Sealing_nothing_stores_nothing_with_a_key_or_without_one()
    {
        // Rather than a tagged empty payload: "unset" is a state every
        // settings form has, and clearing one needs no key.
        foreach (var protector in new[] { WithKey(), WithNoKey() })
        {
            Assert.True(protector.TrySeal("", Row, out var empty));
            Assert.Equal(string.Empty, empty);
            Assert.True(protector.TrySeal(null, Row, out var none));
            Assert.Equal(string.Empty, none);
        }
    }

    [Fact]
    public void With_no_key_sealing_a_secret_is_refused_and_stores_nothing()
    {
        var protector = WithNoKey();

        Assert.False(protector.CanSeal);
        Assert.False(protector.TrySeal("hunter2", Row, out var stored));
        Assert.Equal(string.Empty, stored);
    }

    [Fact]
    public void CanSeal_follows_the_key()
    {
        Assert.True(WithKey().CanSeal);
        Assert.False(WithNoKey().CanSeal);
    }

    [Fact]
    public void An_unknown_scheme_reads_as_absent_rather_than_throwing()
    {
        // A row written by a newer Hatch than this one, seen by a rolled-back
        // deployment. Unreadable is the honest answer and it is the same
        // answer callers already handle for a damaged value.
        Assert.Null(WithKey().Unprotect("v9:AAAA", Row));
    }

    [Theory]
    [InlineData("v1:not-base64!!")]
    [InlineData("v2:not.a.sealed-value")]
    [InlineData("v2:")]
    [InlineData("v2:abc")]
    public void A_damaged_payload_reads_as_absent_rather_than_throwing(string stored)
    {
        Assert.Null(WithKey().Unprotect(stored, Row));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_stored_reads_as_nothing(string? stored)
    {
        Assert.Null(WithKey().Unprotect(stored, Row));
    }

    /// <summary>
    /// Point 4 of HA-2748, held for whoever adds a method later: there is no
    /// way to store a value without saying which row it is for.
    /// </summary>
    [Fact]
    public void Every_public_method_that_takes_a_plaintext_also_takes_the_row_name()
    {
        var methods = typeof(SecretProtector).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var method in methods)
        {
            var strings = method.GetParameters().Where(p => p.ParameterType == typeof(string) && !p.IsOut).ToArray();
            if (strings.Length == 0) continue;

            Assert.True(strings.Any(p => p.Name == "rowName"), $"{method.Name} takes a string and no rowName");
        }

        Assert.Contains(methods, m => m.Name == nameof(SecretProtector.TrySeal));
    }
}

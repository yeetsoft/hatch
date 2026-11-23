using System.Security.Cryptography;
using System.Text;
using Hatch.Api.Common;

namespace Hatch.Api.Tests.Common;

/// <summary>
/// The cipher alone, no host. Every key is made here at run time: no key, real
/// or example, goes in a tracked file (docs/ethos.md).
/// </summary>
public class SecretCipherTests
{
    private const string Row = "site-setting/AnthropicApiKey";

    // The awkward string SecretProtectorTests round-trips: the characters that break a URL.
    private const string Awkward = "p@ss:w/rd?#&=%[]";

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static SecretCipher Cipher(byte[]? current, byte[]? previous = null) => new(current, previous);

    [Theory]
    [InlineData("hunter2")]
    [InlineData(Awkward)]
    [InlineData("")]
    [InlineData("héllo wörld ✓ 🔑")]
    public void A_sealed_value_opens_to_the_same_value(string plaintext)
    {
        var cipher = Cipher(NewKey());

        var sealedValue = cipher.Seal(plaintext, Row);

        Assert.NotNull(sealedValue);
        Assert.True(cipher.TryOpen(sealedValue, Row, out var opened));
        Assert.Equal(plaintext, opened);
    }

    [Fact]
    public void A_value_sealed_for_one_row_does_not_open_under_another_rows_name()
    {
        var cipher = Cipher(NewKey());
        var sealedValue = cipher.Seal("hunter2", Row);

        Assert.False(cipher.TryOpen(sealedValue, "site-setting/OtherKey", out var opened));
        Assert.Null(opened);
    }

    [Fact]
    public void Two_seals_of_the_same_value_differ()
    {
        var cipher = Cipher(NewKey());

        var first = cipher.Seal("hunter2", Row);
        var second = cipher.Seal("hunter2", Row);

        Assert.NotEqual(first, second);
        Assert.NotEqual(first!.Split('.')[1], second!.Split('.')[1]);
    }

    [Fact]
    public void The_sealed_form_is_key_id_nonce_and_ciphertext_with_tag()
    {
        var key = NewKey();
        var cipher = Cipher(key);
        const string plaintext = Awkward;

        var sealedValue = cipher.Seal(plaintext, Row)!;
        var parts = sealedValue.Split('.');

        Assert.Equal(3, parts.Length);

        var expectedId = Convert.ToHexStringLower(SHA256.HashData(key))[..8];
        Assert.Equal(expectedId, parts[0]);
        Assert.Equal(expectedId, cipher.CurrentKeyId);
        Assert.Matches("^[0-9a-f]{8}$", parts[0]);

        foreach (var part in parts.Skip(1))
        {
            Assert.DoesNotContain('=', part);
            Assert.DoesNotContain('+', part);
            Assert.DoesNotContain('/', part);
        }

        Assert.Equal(12, FromBase64Url(parts[1]).Length);
        Assert.Equal(Encoding.UTF8.GetByteCount(plaintext) + 16, FromBase64Url(parts[2]).Length);
    }

    [Fact]
    public void A_value_opens_under_the_previous_key_its_key_id_names()
    {
        var oldKey = NewKey();
        var sealedValue = Cipher(oldKey).Seal("hunter2", Row);

        var rotated = Cipher(NewKey(), oldKey);

        Assert.True(rotated.TryOpen(sealedValue, Row, out var opened));
        Assert.Equal("hunter2", opened);
    }

    [Fact]
    public void A_value_opens_under_the_current_key_when_a_previous_one_is_also_set()
    {
        var current = NewKey();
        var cipher = Cipher(current, NewKey());
        var sealedValue = cipher.Seal("hunter2", Row);

        Assert.True(cipher.TryOpen(sealedValue, Row, out var opened));
        Assert.Equal("hunter2", opened);
    }

    [Fact]
    public void A_value_whose_key_id_names_neither_key_does_not_open()
    {
        var sealedValue = Cipher(NewKey()).Seal("hunter2", Row);

        var other = Cipher(NewKey(), NewKey());

        Assert.False(other.TryOpen(sealedValue, Row, out var opened));
        Assert.Null(opened);
    }

    [Fact]
    public void A_value_with_a_right_key_id_but_the_wrong_key_material_does_not_open()
    {
        var key = NewKey();
        var sealedValue = Cipher(key).Seal("hunter2", Row)!;
        var parts = sealedValue.Split('.');

        // Same key id, different ciphertext: a tag that fails, not a missing key.
        var other = Cipher(key).Seal("hunter3", Row)!.Split('.');
        var spliced = $"{parts[0]}.{parts[1]}.{other[2]}";

        Assert.False(Cipher(key).TryOpen(spliced, Row, out _));
    }

    [Fact]
    public void Only_the_current_key_seals()
    {
        var oldKey = NewKey();
        var current = NewKey();
        var cipher = Cipher(current, oldKey);

        var sealedValue = cipher.Seal("hunter2", Row)!;

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(current))[..8], sealedValue.Split('.')[0]);
        Assert.NotEqual(Convert.ToHexStringLower(SHA256.HashData(oldKey))[..8], sealedValue.Split('.')[0]);
    }

    [Fact]
    public void With_only_a_previous_key_nothing_seals_and_old_values_still_open()
    {
        var oldKey = NewKey();
        var sealedValue = Cipher(oldKey).Seal("hunter2", Row);

        var cipher = Cipher(null, oldKey);

        Assert.False(cipher.CanSeal);
        Assert.Null(cipher.CurrentKeyId);
        Assert.Null(cipher.Seal("hunter2", Row));
        Assert.True(cipher.TryOpen(sealedValue, Row, out var opened));
        Assert.Equal("hunter2", opened);
    }

    [Fact]
    public void With_no_key_at_all_nothing_seals_and_nothing_opens()
    {
        var sealedValue = Cipher(NewKey()).Seal("hunter2", Row);
        var cipher = Cipher(null);

        Assert.False(cipher.CanSeal);
        Assert.Null(cipher.Seal("hunter2", Row));
        Assert.False(cipher.TryOpen(sealedValue, Row, out _));
    }

    [Fact]
    public void Damaged_input_does_not_open_and_never_throws()
    {
        var cipher = Cipher(NewKey());
        var good = cipher.Seal("hunter2", Row)!;
        var parts = good.Split('.');

        var flipped = good[..^1] + (good[^1] == 'A' ? 'B' : 'A');
        var cutTail = $"{parts[0]}.{parts[1]}.{parts[2][..(parts[2].Length / 2)]}";
        var tooShort = $"{parts[0]}.{parts[1]}.{parts[2][..4]}";
        var shortNonce = $"{parts[0]}.{parts[1][..4]}.{parts[2]}";
        var notBase64 = $"{parts[0]}.!!!!.{parts[2]}";

        foreach (var damaged in new string?[]
        {
            null, "", "x", "a.b", ".....", "..", good + ".extra", flipped, cutTail, tooShort, shortNonce, notBase64,
            $"{parts[0]}..", $"{parts[0]}.{parts[1]}.",
        })
        {
            var opened = cipher.TryOpen(damaged, Row, out var plaintext);

            Assert.False(opened, $"opened: {damaged}");
            Assert.Null(plaintext);
        }
    }

    [Fact]
    public void A_flipped_ciphertext_character_does_not_open()
    {
        var cipher = Cipher(NewKey());
        var parts = cipher.Seal("a longer value to give the ciphertext some body", Row)!.Split('.');
        var body = parts[2];
        var flipped = (body[0] == 'A' ? 'B' : 'A') + body[1..];

        Assert.False(cipher.TryOpen($"{parts[0]}.{parts[1]}.{flipped}", Row, out _));
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

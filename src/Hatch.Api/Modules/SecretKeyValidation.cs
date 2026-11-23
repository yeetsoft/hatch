using Hatch.Api.Common;
using Hatch.Api.Modules.Hatch;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules;

/// <summary>
/// Checks <see cref="HatchOptions.SecretKey"/> and <see cref="HatchOptions.PreviousSecretKey"/>
/// at startup (<c>.ValidateOnStart()</c>, HatchModule.cs). A blank one is "not
/// set" and passes. One that is not base64 of exactly 32 bytes stops the API,
/// because a key that quietly fails to seal is a secret saved under nothing.
/// The sentence names the variable the operator sets and never the value.
/// </summary>
public class SecretKeyValidation : IValidateOptions<HatchOptions>
{
    public ValidateOptionsResult Validate(string? name, HatchOptions options)
    {
        var failures = new List<string>();

        if (!SecretKeys.TryParse(options.SecretKey, out _))
        {
            failures.Add(Sentence("HATCH_SECRET_KEY"));
        }

        if (!SecretKeys.TryParse(options.PreviousSecretKey, out _))
        {
            failures.Add(Sentence("HATCH_PREVIOUS_SECRET_KEY"));
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string Sentence(string variable) =>
        $"{variable} is not a usable key: it takes base64 of exactly 32 bytes. Make one with `openssl rand -base64 32`.";
}

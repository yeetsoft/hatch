using Hatch.Api.Common;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Says once, when the API starts, that there is no key to seal secrets with.
/// A hosted service rather than the cipher's constructor, so the line is one
/// per start and not one per place the cipher gets built.
/// </summary>
public sealed class SecretKeyWarning(SecretCipher cipher, ILogger<SecretKeyWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!cipher.CanSeal)
        {
            logger.LogWarning(
                "HATCH_SECRET_KEY is not set, so secrets cannot be saved until one is set. Make one with `openssl rand -base64 32`.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

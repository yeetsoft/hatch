namespace Hatch.Api.Common;

public interface ISecrets
{
    public string? GetSecret(string key);
}

// Backed by IConfiguration so secrets can come from either .env.json (local
// dev, see Program.cs) or environment variables injected in production -
// configuration keys are case-insensitive, so either casing resolves the same
// key.
public class EnvSecrets(IConfiguration configuration) : ISecrets
{
    public string? GetSecret(string key) => configuration[key];
}

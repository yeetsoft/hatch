using System.Text.Json;

namespace Hatch.Cli.Tests.Envelopes;

/// <summary>
/// Every future reader must pass this directory: one hand-written pull
/// request body (<c>*.md</c>) per case, beside a sidecar (<c>*.json</c>)
/// saying what <see cref="PullRequestEnvelopeReader.TryRead"/> and
/// <see cref="PullRequestEnvelopeReader.TryReadVersionZero"/> must answer for
/// it. Walked by directory rather than listed by name, so a case added here
/// is run without also having to be added to this file - and a case missing
/// its sidecar fails by the name of the file that has none, not silently.
/// </summary>
public sealed class EnvelopeCorpusTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var md in Directory.EnumerateFiles(CorpusDirectory(), "*.md").OrderBy(f => f, StringComparer.Ordinal))
            yield return [Path.GetFileNameWithoutExtension(md)];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachPair_AnswersAsItsSidecarSays(string name)
    {
        var mdPath = Path.Combine(CorpusDirectory(), name + ".md");
        var jsonPath = Path.Combine(CorpusDirectory(), name + ".json");
        Assert.True(File.Exists(jsonPath), $"{name}.md has no sidecar {name}.json");

        var body = File.ReadAllText(mdPath);
        var expected = JsonSerializer.Deserialize<CaseFile>(File.ReadAllText(jsonPath), SerializerOptions)
            ?? throw new InvalidDataException($"{name}.json did not parse as a case file");

        var readOk = PullRequestEnvelopeReader.TryRead(body, out var envelope, out var reason);
        Assert.Equal(expected.Read.Ok, readOk);
        if (readOk)
        {
            Assert.Null(reason);
            AssertEnvelope(expected.Read.Envelope!, envelope!);
        }
        else
        {
            Assert.NotNull(reason);
            Assert.Null(envelope);
        }

        var versionZeroOk = PullRequestEnvelopeReader.TryReadVersionZero(expected.Title, body, out var key, out var instance);
        Assert.Equal(expected.VersionZero.Ok, versionZeroOk);
        Assert.Equal(expected.VersionZero.Key, key);
        Assert.Equal(expected.VersionZero.Instance, instance);
    }

    private static void AssertEnvelope(EnvelopeCase expected, PullRequestEnvelope actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Instance, actual.Instance);
        Assert.Equal(expected.Project, actual.Project);
        Assert.Equal(expected.Chain.Select(ToIssue), actual.Chain);
    }

    private static PullRequestEnvelopeIssue ToIssue(IssueCase c) =>
        new(c.Key, c.Title, c.Type, c.Description, c.Status, c.ParentKey, c.Url, c.Truncated);

    private static string CorpusDirectory()
    {
        const string relative = "src/Hatch.Cli.Tests/Envelopes";

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}.");
    }

    private sealed record CaseFile(string Title, ReadCase Read, VersionZeroCase VersionZero);

    private sealed record ReadCase(bool Ok, EnvelopeCase? Envelope);

    private sealed record EnvelopeCase(int Version, string? Instance, string? Project, List<IssueCase> Chain);

    private sealed record IssueCase(
        string Key, string Title, string? Type, string? Description, string? Status, string? ParentKey, string? Url, bool Truncated);

    private sealed record VersionZeroCase(bool Ok, string? Key, string? Instance);
}

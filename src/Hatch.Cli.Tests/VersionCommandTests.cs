using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>version</c> - one line of JSON a launcher checking a downloaded build
/// reads back, with nothing configured.
/// </summary>
public sealed class VersionCommandTests
{
    private readonly Transcript _say = new();

    private string Said => string.Join("\n", _say.Said);

    [Fact]
    public void A_stamped_build_s_line_parses_back_as_its_own_revision_sequence_and_launcher()
    {
        var command = new VersionCommand(_say, new FakeCliRevision("a".PadLeft(40, 'a'), 42));

        Assert.Equal(0, command.Run([]));

        var report = JsonSerializer.Deserialize<VersionReport>(Said, HatchClient.Json);
        Assert.Equal(new string('a', 40), report!.Revision);
        Assert.Equal(42, report.Sequence);
        Assert.Equal(Launcher.Version, report.Launcher);
    }

    [Fact]
    public void A_dev_build_says_dev_and_zero()
    {
        var command = new VersionCommand(_say, new FakeCliRevision());

        Assert.Equal(0, command.Run([]));

        var report = JsonSerializer.Deserialize<VersionReport>(Said, HatchClient.Json);
        Assert.Equal("dev", report!.Revision);
        Assert.Equal(0, report.Sequence);
    }

    [Fact]
    public void Dash_h_prints_usage_and_nothing_else()
    {
        var command = new VersionCommand(_say, new FakeCliRevision());

        Assert.Equal(0, command.Run(["-h"]));

        Assert.Equal(VersionCommand.VersionUsage, _say.Said);
    }

    [Fact]
    public void An_argument_is_refused()
    {
        var command = new VersionCommand(_say, new FakeCliRevision());

        Assert.Equal(1, command.Run(["--anything"]));

        Assert.Contains("version takes no arguments", string.Join("\n", _say.Complained));
    }

    [Fact]
    public void It_runs_with_no_settings_at_all()
    {
        var command = new VersionCommand(_say, new CliRevision());

        Assert.Equal(0, command.Run([]));

        Assert.Single(_say.Said);
    }
}

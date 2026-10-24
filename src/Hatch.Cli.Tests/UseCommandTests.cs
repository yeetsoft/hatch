namespace Hatch.Cli.Tests;

/// <summary>
/// <c>use</c> - which named profile is active, with nothing configured and a
/// temp directory standing in for the machine's own.
/// </summary>
public sealed class UseCommandTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-use-").FullName;
    private readonly Transcript _say = new();

    private string Profiles => Path.Combine(_temp, "profiles");

    private string Active => Path.Combine(_temp, "active");

    private UseCommand Command() => new(_say) { ProfilesDirectory = Profiles, ActiveProfilePath = Active };

    private string Said => string.Join("\n", _say.Said);

    private string Complained => string.Join("\n", _say.Complained);

    private void Profile(string name)
    {
        Directory.CreateDirectory(Profiles);
        File.WriteAllLines(Path.Combine(Profiles, name), ["HATCH_BASE=https://hatch.example"]);
    }

    [Fact]
    public void Bare_with_none_active_prints_none()
    {
        Assert.Equal(0, Command().Run([]));

        Assert.Equal("<none>", Said);
    }

    [Fact]
    public void Bare_with_one_active_prints_its_name()
    {
        File.WriteAllLines(Active, ["staging"]);

        Assert.Equal(0, Command().Run([]));

        Assert.Equal("staging", Said);
    }

    [Fact]
    public void Switching_writes_the_active_file_and_prints_the_name()
    {
        Profile("staging");

        Assert.Equal(0, Command().Run(["staging"]));

        Assert.Equal(["staging"], File.ReadAllLines(Active));
        Assert.Equal("staging", Said);

        Assert.Equal(0, Command().Run([]));
        Assert.Equal("staging\nstaging", Said);
    }

    [Fact]
    public void None_clears_the_active_file()
    {
        File.WriteAllLines(Active, ["staging"]);

        Assert.Equal(0, Command().Run(["--none"]));

        Assert.False(File.Exists(Active));

        Assert.Equal(0, Command().Run([]));
        Assert.Equal("<none>\n<none>", Said);
    }

    [Fact]
    public void None_with_nothing_active_is_not_an_error()
    {
        Assert.Equal(0, Command().Run(["--none"]));

        Assert.Equal("<none>", Said);
    }

    [Fact]
    public void An_unknown_name_refuses_naming_the_profiles_directory()
    {
        Profile("staging");

        Assert.Equal(1, Command().Run(["prod"]));

        Assert.Contains("prod", Complained);
        Assert.Contains(Profiles, Complained);
        Assert.False(File.Exists(Active));
    }

    [Fact]
    public void An_invalid_name_refuses()
    {
        Profile("has spaces");

        Assert.Equal(1, Command().Run(["has spaces"]));

        Assert.Contains("has spaces", Complained);
        Assert.Contains(Profiles, Complained);
        Assert.False(File.Exists(Active));
    }

    [Fact]
    public void A_refusal_leaves_the_active_profile_as_it_was()
    {
        Profile("staging");
        Command().Run(["staging"]);

        Assert.Equal(1, Command().Run(["prod"]));

        Assert.Equal(["staging"], File.ReadAllLines(Active));
    }

    [Fact]
    public void Two_names_are_refused_with_the_usage()
    {
        Assert.Equal(1, Command().Run(["a", "b"]));

        Assert.Contains("usage: hatch use", Complained);
    }

    [Fact]
    public void Dash_h_prints_the_usage_block()
    {
        Assert.Equal(0, Command().Run(["-h"]));

        Assert.Equal(UseCommand.UseUsage, _say.Said);
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);
}

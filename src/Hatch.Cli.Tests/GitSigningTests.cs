using System.Diagnostics;

namespace Hatch.Cli.Tests;

/// <summary>
/// The signing check against a scratch repository whose own settings name a
/// signing program: one that signs, one that fails, and one that never answers.
/// Real git, because whether a signer works is git's own business. Nothing here
/// reads or writes the machine's settings - git is pointed at an empty global
/// file, told to ignore the system one, and given a home of its own.
/// </summary>
public sealed class GitSigningTests : IDisposable
{
    private static readonly TimeSpan Plenty = TimeSpan.FromSeconds(30);

    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-signing-").FullName;
    private readonly string _repo;
    private readonly Dictionary<string, string?> _env;

    public GitSigningTests()
    {
        _repo = Path.Combine(_temp, "repo");
        Directory.CreateDirectory(_repo);

        var home = Path.Combine(_temp, "home");
        Directory.CreateDirectory(home);
        var global = Path.Combine(_temp, "global.gitconfig");
        File.WriteAllText(global, "");

        _env = new()
        {
            ["GIT_CONFIG_GLOBAL"] = global,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["HOME"] = home,
            ["USERPROFILE"] = home,
            ["GIT_CONFIG_COUNT"] = null,
        };

        G("init", "--quiet", "-b", "main");
        G("config", "--local", "user.name", "Test");
        G("config", "--local", "user.email", "test@example.invalid");
    }

    public void Dispose()
    {
        try
        {
            // Git's object files are read-only, which a recursive delete on Windows will not take.
            foreach (var file in Directory.EnumerateFiles(_temp, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_temp, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private string G(params string[] args)
    {
        var done = Detect.ProbeIn("git", args, Plenty, CancellationToken.None, _repo, _env).GetAwaiter().GetResult();
        Assert.True(done is { Completed: true, ExitCode: 0 }, $"git {string.Join(' ', args)}: {done.Output}");
        return done.Output;
    }

    /// <summary>A shell script a test names as <c>gpg.program</c>.</summary>
    private string Signer(string body)
    {
        var path = Path.Combine(_temp, $"signer-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path.Replace('\\', '/');
    }

    private const string Signs = """
        cat >/dev/null
        echo "[GNUPG:] SIG_CREATED " >&2
        echo "-----BEGIN PGP SIGNATURE-----"
        echo "ZmFrZQ=="
        echo "-----END PGP SIGNATURE-----"
        """;

    private void SignWith(string program)
    {
        G("config", "--local", "commit.gpgsign", "true");
        G("config", "--local", "gpg.program", program);
    }

    private void Commit(string file)
    {
        File.WriteAllText(Path.Combine(_repo, file), file);
        G("add", "--all");
        G("-c", "commit.gpgsign=false", "commit", "--quiet", "-m", file);
    }

    private Task<SigningCheck> Check(TimeSpan? limit = null) =>
        GitSigning.CheckAsync(_repo, limit ?? Plenty, CancellationToken.None, _env);

    [Fact]
    public async Task Git_not_set_to_sign_answers_off()
    {
        Commit("a.txt");

        var check = await Check();

        Assert.Equal(Signing.Off, check.State);
        Assert.Equal("", check.Why);
    }

    [Fact]
    public async Task Signing_set_false_answers_off_even_with_a_program_named()
    {
        Commit("a.txt");
        SignWith(Signer("exit 9"));
        G("config", "--local", "commit.gpgsign", "false");

        Assert.Equal(Signing.Off, (await Check()).State);
    }

    [Fact]
    public async Task A_signer_that_signs_answers_works()
    {
        Commit("a.txt");
        SignWith(Signer(Signs));

        var check = await Check();

        Assert.Equal(Signing.Works, check.State);
        Assert.Equal("", check.Why);
    }

    [Fact]
    public async Task A_signer_on_an_unborn_branch_is_asked_too()
    {
        SignWith(Signer(Signs));

        Assert.Equal(Signing.Works, (await Check()).State);
    }

    [Fact]
    public async Task A_signer_that_fails_answers_fails_with_what_git_said()
    {
        Commit("a.txt");
        SignWith(Signer("cat >/dev/null\necho 'no secret key to sign with' >&2\nexit 2"));

        var check = await Check();

        Assert.Equal(Signing.Fails, check.State);
        Assert.Contains("gpg failed to sign the data", check.Why);
        Assert.Contains("no secret key to sign with", check.Why);
        Assert.DoesNotContain('\n', check.Why);
    }

    [Fact]
    public async Task A_signer_that_never_answers_fails_at_the_limit_and_is_ended()
    {
        Commit("a.txt");
        var pidFile = Path.Combine(_temp, "signer.pid").Replace('\\', '/');
        SignWith(Signer($"echo $$ > '{pidFile}'\nexec sleep 300"));

        var watch = Stopwatch.StartNew();
        var check = await Check(TimeSpan.FromSeconds(3));
        watch.Stop();

        Assert.Equal(Signing.Fails, check.State);
        Assert.Equal("git did not answer within 3s", check.Why);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20));

        // The signing program was started, and is gone: nothing is left sleeping.
        Assert.True(File.Exists(pidFile));
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Alive(pid) && DateTime.UtcNow < deadline) await Task.Delay(100);
        Assert.False(Alive(pid), $"the signing program (pid {pid}) was left running");
    }

    private static bool Alive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
    }

    [Fact]
    public async Task The_check_moves_no_ref_and_touches_neither_the_index_nor_the_tree()
    {
        Commit("a.txt");
        SignWith(Signer(Signs));

        // A tree with something in every state a check could disturb: a staged
        // change, an unstaged one and an untracked file.
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "changed");
        File.WriteAllText(Path.Combine(_repo, "staged.txt"), "staged");
        G("add", "staged.txt");
        File.WriteAllText(Path.Combine(_repo, "untracked.txt"), "untracked");

        string State() => string.Join("\n---\n",
            G("for-each-ref", "--format=%(refname) %(objectname)"),
            G("rev-parse", "HEAD"),
            G("status", "--porcelain=v2", "--branch"),
            G("diff", "--cached"),
            Convert.ToHexString(File.ReadAllBytes(Path.Combine(_repo, ".git", "index"))),
            File.ReadAllText(Path.Combine(_repo, "a.txt")),
            File.ReadAllText(Path.Combine(_repo, "untracked.txt")));

        var before = State();
        var check = await Check();

        Assert.Equal(Signing.Works, check.State);
        Assert.Equal(before, State());
    }

    [Fact]
    public async Task The_check_does_not_change_the_settings_it_read()
    {
        Commit("a.txt");
        SignWith(Signer(Signs));
        var before = G("config", "--local", "--list") + File.ReadAllText(_env["GIT_CONFIG_GLOBAL"]!);

        await Check();

        Assert.Equal(before, G("config", "--local", "--list") + File.ReadAllText(_env["GIT_CONFIG_GLOBAL"]!));
    }

    [Theory]
    [InlineData("git version 2.43.0.windows.1", false)]
    [InlineData("git version 2.31.0", false)]
    [InlineData("git version 2.30.9", true)]
    [InlineData("git version 1.9.5", true)]
    [InlineData("git version 3.0.0", false)]
    [InlineData("", false)]
    [InlineData("not a version", false)]
    public void Git_older_than_2_31_is_told_from_its_version_line(string line, bool older) =>
        Assert.Equal(older, GitSigning.Older(new ProbeResult(true, 0, line)));

    [Fact]
    public void A_version_git_did_not_print_is_not_older() =>
        Assert.False(GitSigning.Older(new ProbeResult(false, -1, "")));

    [Fact]
    public void A_failure_is_remembered_for_thirty_minutes_by_path()
    {
        var cache = new SigningCache();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var failed = new SigningCheck(Signing.Fails, "locked");

        cache.Record(_repo, failed, start);

        Assert.Equal(failed, cache.Failed(_repo, start.AddMinutes(29)));
        Assert.Null(cache.Failed(Path.Combine(_temp, "elsewhere"), start));
        Assert.Null(cache.Failed(_repo, start.AddMinutes(30)));
    }

    [Fact]
    public void Another_answer_forgets_a_remembered_failure()
    {
        var cache = new SigningCache();
        var start = DateTimeOffset.UnixEpoch;
        cache.Record(_repo, new SigningCheck(Signing.Fails, "locked"), start);

        cache.Record(_repo, new SigningCheck(Signing.Works), start);

        Assert.Null(cache.Failed(_repo, start));
    }

    [Fact]
    public void The_latest_answer_is_kept_whatever_it_was_and_does_not_expire()
    {
        var cache = new SigningCache();
        var start = DateTimeOffset.UnixEpoch;
        Assert.Null(cache.Latest(_repo));

        var failed = new SigningCheck(Signing.Fails, "locked");
        cache.Record(_repo, failed, start);
        Assert.Equal(failed, cache.Latest(_repo));
        Assert.Null(cache.Failed(_repo, start.AddMinutes(30)));
        Assert.Equal(failed, cache.Latest(_repo));

        var works = new SigningCheck(Signing.Works);
        cache.Record(_repo, works, start.AddHours(1));
        Assert.Equal(works, cache.Latest(_repo));

        var off = new SigningCheck(Signing.Off);
        cache.Record(_repo, off, start.AddHours(2));
        Assert.Equal(off, cache.Latest(_repo));
        Assert.Null(cache.Latest(Path.Combine(_temp, "elsewhere")));
    }

    [Fact]
    public void The_latest_answers_under_a_directory_are_those_of_the_checkouts_inside_it()
    {
        var cache = new SigningCache();
        var inside = new SigningCheck(Signing.Fails, "locked");
        cache.Record(Path.Combine(_temp, "workspace", "one"), inside, DateTimeOffset.UnixEpoch);
        cache.Record(Path.Combine(_temp, "workspace-two", "two"), new SigningCheck(Signing.Works), DateTimeOffset.UnixEpoch);
        cache.Record(_repo, new SigningCheck(Signing.Works), DateTimeOffset.UnixEpoch);

        Assert.Equal([inside], cache.LatestUnder(Path.Combine(_temp, "workspace")));
    }
}

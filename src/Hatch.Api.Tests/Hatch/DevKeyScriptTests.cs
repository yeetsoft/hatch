using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>scripts/dev-key.cs</c>, run for real the way the Makefile runs it: a
/// single-file C# app started with <c>dotnet run</c>. It is what gives a
/// developer a secret key from <c>make run</c> and <c>make up</c> (HA-2747), so
/// the cases are the promises that matter to somebody's data: a key already
/// there is never replaced, nothing else in the file is lost, and a file or a
/// directory it does not understand is left alone.
///
/// <para>The first run compiles the script, which takes tens of seconds, so the
/// fixture does that once up front. The tests then only pay for a start.</para>
/// </summary>
public sealed class DevKeyScriptTests : IClassFixture<DevKeyScriptTests.Compiled>
{
    private const int Changed = 10;

    private readonly Compiled _script;

    public DevKeyScriptTests(Compiled script) => _script = script;

    [Fact]
    public async Task A_missing_file_is_made_with_a_key_of_32_random_bytes()
    {
        using var dir = new Scratch();
        var path = Path.Combine(dir.Path, ".env.json");

        var run = await _script.RunAsync(path);

        Assert.Equal(Changed, run.Exit);
        var key = KeyIn(path);
        Assert.Equal(32, Convert.FromBase64String(key).Length);
        Assert.DoesNotContain(key, run.Stdout);
        Assert.Contains(path, run.Stdout);
    }

    [Fact]
    public async Task Two_missing_files_get_different_keys()
    {
        using var dir = new Scratch();
        var one = Path.Combine(dir.Path, "one.env.json");
        var two = Path.Combine(dir.Path, "two.env.json");

        await _script.RunAsync(one);
        await _script.RunAsync(two);

        Assert.NotEqual(KeyIn(one), KeyIn(two));
    }

    [Fact]
    public async Task A_key_already_there_is_kept_and_the_file_is_not_rewritten()
    {
        using var dir = new Scratch();
        var path = Path.Combine(dir.Path, ".env.json");
        // Comments and a trailing comma: a rewrite would drop them, so equal bytes show there was none.
        var original = "{\n  // mine\n  \"Hatch\": { \"SecretKey\": \"not-even-a-valid-key\", },\n}\n";
        await File.WriteAllTextAsync(path, original);

        var run = await _script.RunAsync(path);

        Assert.Equal(0, run.Exit);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task A_key_is_found_whatever_the_case_of_its_name()
    {
        using var dir = new Scratch();
        var path = Path.Combine(dir.Path, ".env.json");
        var original = "{ \"hatch\": { \"secretkey\": \"abc\" } }";
        await File.WriteAllTextAsync(path, original);

        var run = await _script.RunAsync(path);

        Assert.Equal(0, run.Exit);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Other_values_are_kept_when_a_key_is_added()
    {
        using var dir = new Scratch();
        var path = Path.Combine(dir.Path, ".env.json");
        await File.WriteAllTextAsync(path,
            """{ "Other": { "Token": "t" }, "Hatch": { "Name": "mine", "Port": 5 }, "Plain": "p" }""");

        var run = await _script.RunAsync(path);

        Assert.Equal(Changed, run.Exit);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal("t", (string?)root["Other"]!["Token"]);
        Assert.Equal("mine", (string?)root["Hatch"]!["Name"]);
        Assert.Equal(5, (int?)root["Hatch"]!["Port"]);
        Assert.Equal("p", (string?)root["Plain"]);
        Assert.Equal(32, Convert.FromBase64String(KeyIn(path)).Length);
    }

    [Theory]
    [InlineData("""{ "Hatch": { "SecretKey": "" } }""")]
    [InlineData("""{ "Hatch": { "SecretKey": "   " } }""")]
    [InlineData("""{ "Hatch": { "SecretKey": null } }""")]
    [InlineData("""{ "Hatch": {} }""")]
    [InlineData("{}")]
    public async Task A_blank_or_missing_key_counts_as_none(string content)
    {
        using var dir = new Scratch();
        var path = Path.Combine(dir.Path, ".env.json");
        await File.WriteAllTextAsync(path, content);

        var run = await _script.RunAsync(path);

        Assert.Equal(Changed, run.Exit);
        Assert.Equal(32, Convert.FromBase64String(KeyIn(path)).Length);
    }

    [Fact]
    public async Task An_empty_directory_is_replaced_by_the_file()
    {
        using var dir = new Scratch();
        var path = Path.Combine(dir.Path, ".env.json");
        Directory.CreateDirectory(path);

        var run = await _script.RunAsync(path);

        Assert.Equal(Changed, run.Exit);
        Assert.True(File.Exists(path));
        Assert.Equal(32, Convert.FromBase64String(KeyIn(path)).Length);
    }

    [Fact]
    public async Task A_directory_with_files_in_it_stops_the_script_and_is_left_alone()
    {
        using var dir = new Scratch();
        var path = Path.Combine(dir.Path, ".env.json");
        Directory.CreateDirectory(path);
        var inside = Path.Combine(path, "keep.txt");
        await File.WriteAllTextAsync(inside, "mine");

        var run = await _script.RunAsync(path);

        Assert.Equal(1, run.Exit);
        Assert.Contains(path, run.Stderr);
        Assert.Contains("docker compose up", run.Stderr);
        Assert.Equal("mine", await File.ReadAllTextAsync(inside));
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"a string\"")]
    [InlineData("""{ "Hatch": "text" }""")]
    public async Task A_file_it_cannot_use_stops_the_script_and_is_left_untouched(string content)
    {
        using var dir = new Scratch();
        var path = Path.Combine(dir.Path, ".env.json");
        await File.WriteAllTextAsync(path, content);

        var run = await _script.RunAsync(path);

        Assert.Equal(1, run.Exit);
        Assert.Contains(path, run.Stderr);
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    private static string KeyIn(string path) =>
        (string)JsonNode.Parse(File.ReadAllText(path))!["Hatch"]!["SecretKey"]!;

    private sealed class Scratch : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hatch-dev-key-" + Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Finds the script and the SDK, and compiles the script once.</summary>
    public sealed class Compiled : IAsyncLifetime
    {
        private string _dotnet = "";
        private string _script = "";

        public async Task InitializeAsync()
        {
            // The host that is running these tests is the SDK's own dotnet.
            _dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "Makefile")))
            {
                root = root.Parent;
            }

            Assert.NotNull(root);
            _script = System.IO.Path.Combine(root.FullName, "scripts", "dev-key.cs");
            Assert.True(File.Exists(_script), _script);

            // Warm the compile against a scratch file, so no test pays for it.
            var warm = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "hatch-dev-key-warm-" + Guid.NewGuid().ToString("N"), ".env.json");
            try
            {
                var run = await RunAsync(warm);
                Assert.True(run.Exit is 0 or Changed, $"dev-key.cs did not compile or start: {run.Stdout}{run.Stderr}");
            }
            finally
            {
                var folder = System.IO.Path.GetDirectoryName(warm)!;
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        public async Task<(int Exit, string Stdout, string Stderr)> RunAsync(string path)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _dotnet,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(_script);
            psi.ArgumentList.Add(path);

            // An outer `dotnet test` hands its MSBuild its own node and settings; the
            // nested compile should start from nothing, as it does under make.
            foreach (var name in psi.Environment.Keys.Where(k => k.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                psi.Environment.Remove(name);
            }

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout, await stderr);
        }
    }
}

using System.Net;
using System.Text;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>attachment add</c> and <c>attachment get</c>: sending a file to the board
/// and saving one from it, in a temporary directory of the test's own.
/// </summary>
public sealed class AttachmentCommandsTests : IDisposable
{
    private static readonly Guid Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private const string Post = "/api/attachments";

    private readonly string _dir = Directory.CreateTempSubdirectory("hatch-attachment-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static ProjectDto AProject(int id, string key) =>
        new(id, key, key, 0, DateTimeOffset.UtcNow, null, null, [], null, [], false, false);

    private static AttachmentDto Stored(string name, string type, long size = 5) =>
        new(Id, name, type, size, DateTimeOffset.UnixEpoch);

    private string AFile(string name, string content = "hello")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Temp => Path.Combine(_dir, "temp");

    private Task<int> Run(CliHarness h, params string[] args) =>
        new AttachmentCommands(h.Cli, Temp).AttachmentAsync(args, default);

    // ---- add ----

    [Fact]
    public async Task Add_posts_the_file_as_one_part_named_file_under_the_project()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/projects", new List<ProjectDto> { AProject(3, "AER") });
        h.Wire.Json("POST", Post, Stored("a.png", "image/png"));

        Assert.Equal(0, await Run(h, "add", AFile("a.png", "PNGBYTES"), "--project", "aer"));

        var call = Assert.Single(h.Wire.To("POST", Post));
        Assert.Equal("?projectId=3", call.Query);
        Assert.Matches("name=\"?file\"?", call.Body);
        Assert.Contains("PNGBYTES", call.Body);
    }

    [Fact]
    public async Task Add_without_a_project_sends_no_project_id_and_looks_up_nothing()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Post, Stored("a.png", "image/png"));

        Assert.Equal(0, await Run(h, "add", AFile("a.png")));

        Assert.Equal("", Assert.Single(h.Wire.To("POST", Post)).Query);
        Assert.Equal(0, h.Wire.Count("GET", "/api/projects"));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    public async Task Add_prints_an_image_line_for_an_image_type(string type)
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Post, Stored("a.png", type));

        Assert.Equal(0, await Run(h, "add", AFile("a.png")));

        Assert.Equal($"![a.png](/api/images/{Id})", h.Said);
    }

    [Fact]
    public async Task Add_prints_a_link_for_anything_else_whatever_the_name_says()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Post, Stored("notes.pdf", "application/pdf"));

        Assert.Equal(0, await Run(h, "add", AFile("notes.pdf")));

        Assert.Equal($"[notes.pdf](/api/attachments/{Id})", h.Said);
    }

    [Fact]
    public async Task Add_does_not_trust_the_extension_for_the_type()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Post, Stored("a.png", "application/octet-stream"));

        Assert.Equal(0, await Run(h, "add", AFile("a.png")));

        Assert.Equal($"[a.png](/api/attachments/{Id})", h.Said);
    }

    [Theory]
    [InlineData("a[1]\\b.txt", "[a\\[1\\]\\\\b.txt]")]
    [InlineData("two\nlines.txt", "[two lines.txt]")]
    [InlineData("", "[attachment]")]
    public async Task Add_escapes_the_name_and_keeps_it_on_one_line(string name, string shown)
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Post, Stored(name, "text/plain"));

        Assert.Equal(0, await Run(h, "add", AFile("x.txt")));

        Assert.Equal($"{shown}(/api/attachments/{Id})", h.Said);
    }

    [Fact]
    public async Task Add_names_an_empty_image_image()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Post, Stored("", "image/png"));

        Assert.Equal(0, await Run(h, "add", AFile("x.png")));

        Assert.Equal($"![image](/api/images/{Id})", h.Said);
    }

    [Fact]
    public async Task Add_refuses_a_file_that_does_not_exist_and_sends_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await Run(h, "add", Path.Combine(_dir, "missing.png")));

        Assert.Contains("is not a file", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Add_refuses_a_directory_the_same_way()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await Run(h, "add", _dir));

        Assert.Contains("is not a file", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Add_refuses_an_unknown_project_key_and_uploads_nothing()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/projects", new List<ProjectDto> { AProject(3, "AER") });

        Assert.Equal(1, await Run(h, "add", AFile("a.png"), "--project", "NOPE"));

        Assert.Contains("hatch: there is no project NOPE", h.Complained);
        Assert.Equal(0, h.Wire.Count("POST", Post));
    }

    [Fact]
    public async Task Add_says_so_when_the_board_cannot_find_the_project_for_this_key()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/projects", new List<ProjectDto> { AProject(3, "AER") });
        h.Wire.Reply("POST", Post, HttpStatusCode.NotFound);

        Assert.Equal(1, await Run(h, "add", AFile("a.png"), "--project", "AER"));

        Assert.Equal("hatch: that project is not one this key can reach", h.Complained);
    }

    [Theory]
    [InlineData("\"A file is at most 25 MB\"", "A file is at most 25 MB")]
    [InlineData("\"A key uploads to a project\"", "A key uploads to a project")]
    public async Task Add_leaves_the_servers_own_refusal_to_be_printed_as_it_came(string body, string sentence)
    {
        using var h = new CliHarness();
        h.Wire.Reply("POST", Post, HttpStatusCode.BadRequest, body);

        var e = await Assert.ThrowsAsync<HatchException>(() => Run(h, "add", AFile("a.png")));

        Assert.Equal(HttpStatusCode.BadRequest, e.Status);
        Assert.Contains(sentence, e.Message);
    }

    [Fact]
    public async Task Add_takes_one_file_and_refuses_a_second()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await Run(h, "add", AFile("a.png"), AFile("b.png")));

        Assert.Empty(h.Wire.Calls);
    }

    // ---- get ----

    private static string Disposition(string name) => $"attachment; filename=\"{name}\"";

    [Fact]
    public async Task Get_saves_under_the_stored_name_in_a_folder_of_the_id_and_prints_three_lines()
    {
        using var h = new CliHarness();
        byte[] bytes = [0, 1, 2, 250, 255, 3];
        h.Wire.File("GET", $"/api/attachments/{Id}", bytes, "image/png", Disposition("shot.png"));

        Assert.Equal(0, await Run(h, "get", Id.ToString()));

        var path = Path.Combine(Temp, "hatch-attachments", Id.ToString(), "shot.png");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal([path, "image/png", "6 bytes"], h.Say.Said);
    }

    [Fact]
    public async Task Get_with_out_as_a_file_path_saves_there()
    {
        using var h = new CliHarness();
        h.Wire.File("GET", $"/api/attachments/{Id}", [9, 8, 7], "application/pdf", Disposition("n.pdf"));
        var target = Path.Combine(_dir, "chosen.bin");

        Assert.Equal(0, await Run(h, "get", Id.ToString(), "--out", target));

        Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(target));
        Assert.Equal(target, h.Say.Said[0]);
    }

    [Fact]
    public async Task Get_with_out_as_an_existing_directory_saves_inside_it_under_the_stored_name()
    {
        using var h = new CliHarness();
        h.Wire.File("GET", $"/api/attachments/{Id}", [1, 2], "application/pdf", Disposition("n.pdf"));
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "into")).FullName;

        Assert.Equal(0, await Run(h, "get", Id.ToString(), "--out", folder));

        Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(Path.Combine(folder, "n.pdf")));
    }

    [Theory]
    [InlineData("..\\evil.txt")]
    [InlineData("../evil.txt")]
    [InlineData("a/b/evil.txt")]
    public async Task Get_never_trusts_a_stored_name_as_a_path(string stored)
    {
        using var h = new CliHarness();
        h.Wire.File("GET", $"/api/attachments/{Id}", [1], "text/plain", Disposition(stored));
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "into")).FullName;

        Assert.Equal(0, await Run(h, "get", Id.ToString(), "--out", folder));

        Assert.Equal(["evil.txt"], Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")));
    }

    [Fact]
    public async Task Get_falls_back_to_the_id_when_the_server_names_no_file()
    {
        using var h = new CliHarness();
        h.Wire.File("GET", $"/api/attachments/{Id}", [1], "application/octet-stream");

        Assert.Equal(0, await Run(h, "get", Id.ToString()));

        Assert.True(File.Exists(Path.Combine(Temp, "hatch-attachments", Id.ToString(), Id.ToString())));
    }

    [Fact]
    public async Task Get_reads_a_utf8_name()
    {
        using var h = new CliHarness();
        h.Wire.File("GET", $"/api/attachments/{Id}", [1], "text/plain",
            "attachment; filename=\"fallback.txt\"; filename*=UTF-8''caf%C3%A9.txt");

        Assert.Equal(0, await Run(h, "get", Id.ToString()));

        Assert.True(File.Exists(Path.Combine(Temp, "hatch-attachments", Id.ToString(), "café.txt")));
    }

    [Fact]
    public async Task Get_says_so_when_the_id_names_nothing()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", $"/api/attachments/{Id}", HttpStatusCode.NotFound);

        Assert.Equal(1, await Run(h, "get", Id.ToString()));

        Assert.Equal($"hatch: no such attachment {Id}", h.Complained);
        Assert.False(Directory.Exists(Temp));
    }

    [Fact]
    public async Task Get_refuses_an_id_that_is_not_a_guid_and_sends_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await Run(h, "get", "../../etc/passwd"));

        Assert.Contains("is not an attachment id", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Get_leaves_any_other_refusal_to_be_printed_as_it_came()
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", $"/api/attachments/{Id}", HttpStatusCode.Unauthorized);

        var e = await Assert.ThrowsAsync<HatchException>(() => Run(h, "get", Id.ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, e.Status);
    }

    [Fact]
    public async Task Get_prints_an_io_failure_and_exits_1()
    {
        using var h = new CliHarness();
        h.Wire.File("GET", $"/api/attachments/{Id}", [1], "text/plain", Disposition("n.txt"));
        // The path names a file where a directory has to be.
        var blocker = AFile("blocker");

        Assert.Equal(1, await Run(h, "get", Id.ToString(), "--out", Path.Combine(blocker, "inside", "n.txt")));

        Assert.StartsWith("hatch: ", h.Complained);
    }

    // ---- help and shape ----

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("add", "-h")]
    [InlineData("get", "-h")]
    public async Task Help_prints_the_usage_and_exits_0(params string[] args)
    {
        using var h = new CliHarness();

        Assert.Equal(0, await Run(h, args));

        Assert.Equal(AttachmentCommands.AttachmentUsage, h.Say.Said);
        Assert.Empty(h.Wire.Calls);
    }

    [Theory]
    [InlineData]
    [InlineData("remove")]
    [InlineData("add")]
    [InlineData("get")]
    [InlineData("add", "x.png", "--nope", "y")]
    [InlineData("get", "x", "--out")]
    [InlineData("get", "x", "--out", "a", "--out", "b")]
    public async Task A_malformed_call_is_refused_with_the_usage_and_sends_nothing(params string[] args)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await Run(h, args));

        Assert.StartsWith("hatch: ", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public void Hatch_help_lists_the_command()
    {
        Assert.Contains("attachment", Program.Commands);
    }
}

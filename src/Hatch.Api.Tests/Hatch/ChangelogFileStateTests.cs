using System.Text;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary><see cref="ChangelogFileState.Judge"/>, pure: what a trunk's bytes are, against the file the board would write.</summary>
public class ChangelogFileStateTests
{
    private const string Origin = "https://hatch.test";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private static Changelog.Entry Entry(int number, string title = "A thing", string? note = null, int day = 1) =>
        new(number, $"AER-{number}", number, title, "story", "AER", note, new DateTimeOffset(2026, 11, day, 9, 0, 0, TimeSpan.Zero), 0);

    private static string Render(params Changelog.Entry[] entries) => Changelog.RenderFile(entries, Origin);

    private static ChangelogFileVerdict Judge(string file, string projection) =>
        ChangelogFileState.Judge(Encoding.UTF8.GetBytes(file), projection, Sha);

    private static readonly Changelog.Entry[] Three = [Entry(1, day: 1), Entry(2, day: 2), Entry(3, day: 3)];

    [Fact]
    public void ExactlyTheBoardsOwnBytes_AreCurrent()
    {
        var verdict = Judge(Render(Three), Render(Three));

        Assert.Equal(ChangelogFile.CurrentState, verdict.State);
        Assert.Null(verdict.Behind);
        Assert.Null(verdict.Problem);
        Assert.Equal(64, verdict.Hash!.Length);
    }

    [Fact]
    public void AnEmptyProjection_AndTheEmptyFile_AreCurrent() =>
        Assert.Equal(ChangelogFile.CurrentState, Judge(Render(), Render()).State);

    [Fact]
    public void NothingOnTheTrunk_IsAbsent_WithNoHash()
    {
        var verdict = ChangelogFileState.Judge(null, Render(Three), Sha);

        Assert.Equal(ChangelogFile.AbsentState, verdict.State);
        Assert.Null(verdict.Hash);
        Assert.Null(verdict.Behind);
    }

    [Fact]
    public void TwoEntriesCutFromTheEnd_AreBehindByTwo_AndNameThem()
    {
        var verdict = Judge(Render(Three[0]), Render(Three));

        Assert.Equal(ChangelogFile.BehindState, verdict.State);
        Assert.Equal(2, verdict.Behind);
        Assert.Equal(["AER-2", "AER-3"], verdict.Lacking.Order());
    }

    [Fact]
    public void ADifferingNote_IsBehindByOne()
    {
        var verdict = Judge(Render(Entry(1), Entry(2, note: "old note")), Render(Entry(1), Entry(2, note: "new note")));

        Assert.Equal(ChangelogFile.BehindState, verdict.State);
        Assert.Equal(1, verdict.Behind);
        Assert.Equal(["AER-2"], verdict.Lacking);
    }

    [Fact]
    public void AnEntryTheFileHasAndTheBoardNoLongerDoes_IsBehind_ButLacksNothing()
    {
        var verdict = Judge(Render(Three), Render(Three[..2]));

        Assert.Equal(ChangelogFile.BehindState, verdict.State);
        Assert.Equal(1, verdict.Behind);
        Assert.Empty(verdict.Lacking);
    }

    [Fact]
    public void SameEntriesInAnotherOrderOrWithWhitespace_AreBehindByOne_NeverByZero()
    {
        var reordered = Render(Three).Replace("version: 1\n", "version: 1\n\n");

        var verdict = Judge(reordered, Render(Three));

        Assert.Equal(ChangelogFile.BehindState, verdict.State);
        Assert.Equal(1, verdict.Behind);
    }

    [Fact]
    public void ANullNote_AndTheWordNullQuoted_AreNotTheSameNote()
    {
        var quoted = Render(Entry(1, note: "null"));

        Assert.Equal(1, Judge(quoted, Render(Entry(1))).Behind);
    }

    [Fact]
    public void AnotherHeader_IsForeign_AndTheSentenceNamesTheShaAndWhatToDo()
    {
        var verdict = Judge("# mine\nversion: 1\nentries: []\n", Render(Three));

        Assert.Equal(ChangelogFile.ForeignState, verdict.State);
        Assert.Null(verdict.Behind);
        Assert.StartsWith(".hatch/changelog.yml at 0123456 is not a file Hatch wrote (", verdict.Problem);
        Assert.EndsWith(") - move or remove it before Hatch will write there", verdict.Problem);
        Assert.NotNull(verdict.Hash);
    }

    [Theory]
    [InlineData("version: 2\nentries: []\n")]
    [InlineData("entries: []\n")]
    [InlineData("version: 1\n")]
    [InlineData("version: 1\nentries: nope\n")]
    [InlineData("version: 1\nentries:\n  - just a string\n")]
    [InlineData("version: 1\nentries:\n  - title: no key\n")]
    [InlineData("version: 1\nentries:\n  - key: AER-1\n  - key: AER-1\n")]
    [InlineData("- a\n- list\n")]
    [InlineData("version: 1\nentries: [unclosed\n")]
    public void ABodyThatIsNotTheShapeHatchWrites_IsForeign(string body)
    {
        var verdict = Judge(Changelog.Header + "\n" + body, Render(Three));

        Assert.Equal(ChangelogFile.ForeignState, verdict.State);
        Assert.Contains("is not a file Hatch wrote", verdict.Problem);
    }

    [Fact]
    public void BytesThatAreNotUtf8_AreForeign()
    {
        var verdict = ChangelogFileState.Judge([0xFF, 0xFE, 0x00, 0x80], Render(Three), Sha);

        Assert.Equal(ChangelogFile.ForeignState, verdict.State);
        Assert.Contains("UTF-8", verdict.Problem);
    }

    [Fact]
    public void ARenderedFile_CarriesTheHeaderPrefixTheJudgeReads() =>
        Assert.StartsWith(Changelog.HeaderPrefix, Render(Three));
}

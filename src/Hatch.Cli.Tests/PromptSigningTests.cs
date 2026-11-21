namespace Hatch.Cli.Tests;

/// <summary>
/// What the branch section tells a session about signing a commit - one short
/// paragraph, and only where it matters. Every row ends in something to do: a
/// session cannot unlock a key, and nobody is reading to be asked.
/// </summary>
public sealed class PromptSigningTests
{
    private static readonly BranchEntry[] Branches =
        [new BranchEntry { Path = "/checkouts/repo", Kind = BranchKind.None, Cut = "aer-12-x" }];

    private static string Compose(CommitSigning? signing, WorkDto? work = null) =>
        Prompt.Compose(work ?? Fixtures.Work("AER-12"), branches: Branches, signing: signing);

    [Fact]
    public void Signing_that_fails_where_unsigned_is_allowed_says_commits_are_unsigned_and_not_to_ask()
    {
        var prompt = Compose(new CommitSigning(Signing.Fails, Allows: true));

        Assert.Contains("Commits here are unsigned", prompt, StringComparison.Ordinal);
        Assert.Contains("\"committed unsigned\"", prompt, StringComparison.Ordinal);
        Assert.Contains("do not ask", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-gpg-sign", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("commit.gpgsign=false", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_old_git_that_fails_to_sign_is_told_the_two_settings_to_pass_itself()
    {
        var prompt = Compose(new CommitSigning(Signing.Fails, Allows: true, OldGit: true));

        Assert.Contains("than 2.31", prompt, StringComparison.Ordinal);
        Assert.Contains("git -c commit.gpgsign=false -c tag.gpgsign=false commit", prompt, StringComparison.Ordinal);
        Assert.Contains("\"committed unsigned\"", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not ask", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Signing_that_works_where_unsigned_is_allowed_names_the_flag_for_a_commit_that_still_cannot_be_signed()
    {
        var prompt = Compose(new CommitSigning(Signing.Works, Allows: true));

        Assert.Contains("`--no-gpg-sign`", prompt, StringComparison.Ordinal);
        Assert.Contains("do not ask", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Commits here are unsigned", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Signing.Works)]
    [InlineData(Signing.Fails)]
    [InlineData(Signing.Off)]
    public void A_project_that_allows_only_signed_commits_is_never_told_it_may_commit_unsigned(Signing state)
    {
        var prompt = Compose(new CommitSigning(state, Allows: false));

        Assert.Contains("allows only signed commits", prompt, StringComparison.Ordinal);
        Assert.Contains("Never commit unsigned", prompt, StringComparison.Ordinal);
        Assert.Contains("leave the work uncommitted", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not ask", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-gpg-sign", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("committed unsigned", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Signing_off_where_unsigned_is_allowed_adds_nothing()
    {
        Assert.Equal(Compose(null), Compose(new CommitSigning(Signing.Off, Allows: true)));
    }

    [Fact]
    public void No_answer_adds_nothing()
    {
        var prompt = Compose(null);

        Assert.DoesNotContain("gpg", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unsigned", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_paragraph_ends_the_branch_section()
    {
        var prompt = Compose(new CommitSigning(Signing.Fails, Allows: true));

        var branch = prompt.IndexOf("## The branch", StringComparison.Ordinal);
        var paragraph = prompt.IndexOf("Commits here are unsigned", StringComparison.Ordinal);
        var next = prompt.IndexOf("\n## ", branch + 1, StringComparison.Ordinal);

        Assert.InRange(paragraph, branch, next < 0 ? prompt.Length : next);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_row_that_denies_git_writes_is_told_nothing_about_signing(bool allows)
    {
        var work = Fixtures.Work("AER-12") with { Playbook = Fixtures.Playbook(deniedTools: ["Bash(git push *)"]) };

        var prompt = Compose(new CommitSigning(Signing.Fails, allows), work);

        Assert.Equal(Compose(null, work), prompt);
    }

    [Fact]
    public void Without_a_branch_section_there_is_no_paragraph_to_put_it_in()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), signing: new CommitSigning(Signing.Fails, Allows: true));

        Assert.DoesNotContain("unsigned", prompt, StringComparison.OrdinalIgnoreCase);
    }
}

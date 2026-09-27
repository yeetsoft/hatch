namespace Hatch.Cli.Tests;

/// <summary>The instruction a session is handed.</summary>
public sealed class PromptTests
{
    [Fact]
    public void The_playbook_comes_first_and_the_ticket_after_it()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"));

        Assert.StartsWith("Do the thing.", prompt, StringComparison.Ordinal);
        Assert.Contains("AER-12  [task]  A ticket", prompt, StringComparison.Ordinal);
        Assert.Contains("moving:   In Progress -> In Review", prompt, StringComparison.Ordinal);
        Assert.Contains("The brief.", prompt, StringComparison.Ordinal);
        Assert.Contains("AER-12 should be in \"In Review\" when you stop", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pull_request_is_told_to_name_the_ticket_in_its_title_and_first_line()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", issueUrl: "https://hatch.example.test/apps/hatch/issues/AER-12"));

        Assert.Contains("## If you open a pull request", prompt, StringComparison.Ordinal);
        Assert.Contains("**Title:** `AER-12 ` and then", prompt, StringComparison.Ordinal);
        Assert.Contains("\n[AER-12](https://hatch.example.test/apps/hatch/issues/AER-12)\n", prompt, StringComparison.Ordinal);
        Assert.Contains("`hatch pr AER-12 <url>`", prompt, StringComparison.Ordinal);
        Assert.Contains("Never write a relative link.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_link_the_first_line_is_the_bare_key_and_the_session_is_told_so()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", noLink: true));

        Assert.Contains("\n```\nAER-12\n```\n", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("](", prompt, StringComparison.Ordinal);
        Assert.Contains("No link to the ticket is available", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_link_is_never_handed_on()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", issueUrl: "/apps/hatch/issues/AER-12"));

        Assert.DoesNotContain("](", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ticket_with_no_description_says_that_is_worth_noting()
    {
        var work = Fixtures.Work("AER-12", issue: Fixtures.Issue("AER-12", description: ""));
        Assert.Contains("_No description. That is itself worth noting on the ticket._",
            Prompt.Compose(work), StringComparison.Ordinal);
    }

    [Fact]
    public void Its_children_are_listed_where_it_has_them()
    {
        var work = Fixtures.Work("AER-12", children: [Fixtures.Card("AER-13"), Fixtures.Card("AER-14")]);
        var prompt = Prompt.Compose(work);

        Assert.Contains("## Its children", prompt, StringComparison.Ordinal);
        Assert.Contains("- AER-13  [task]  A child", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Decisions_already_made_are_carried_in_and_open_questions_are_not()
    {
        var work = Fixtures.Work("AER-12", questions:
        [
            Fixtures.Question(1, body: "Which way?", answered: true),
            Fixtures.Question(2, body: "And this one?"),
        ]);

        var prompt = Prompt.Compose(work);

        // The one thing a session must not do is reopen a question somebody has
        // already answered, so the answers travel in the prompt rather than
        // waiting in a comment thread.
        Assert.Contains("## Decisions already made", prompt, StringComparison.Ordinal);
        Assert.Contains("**Asked (hatch):** Which way?", prompt, StringComparison.Ordinal);
        Assert.Contains("**Answered (Nathan):** That way.", prompt, StringComparison.Ordinal);

        // An unanswered one is not a decision, and a dispatch carrying one would
        // not have been sent.
        Assert.DoesNotContain("And this one?", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ticket_with_no_answers_gets_no_decisions_section()
    {
        Assert.DoesNotContain("## Decisions already made",
            Prompt.Compose(Fixtures.Work("AER-12")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_ticket_says_when_it_chose_the_model_rather_than_the_playbook()
    {
        var pinned = Fixtures.Work("AER-12", issue: Fixtures.Issue("AER-12", modelOverride: "opus"));

        Assert.Equal("model from AER-12, not the playbook", Prompt.OverrideLine(pinned, "opus", "high"));

        // The value that won is the playbook's either way - the server has
        // already folded the override in - so a flag naming something else is
        // the flag's, and says nothing about the ticket.
        Assert.Null(Prompt.OverrideLine(pinned, "sonnet", "high"));
        Assert.Null(Prompt.OverrideLine(Fixtures.Work("AER-12"), "opus", "high"));
    }

    [Fact]
    public void Both_overrides_read_as_one_sentence()
    {
        var pinned = Fixtures.Work("AER-12",
            issue: Fixtures.Issue("AER-12", modelOverride: "opus", effortOverride: "xhigh"));

        Assert.Equal("model and effort from AER-12, not the playbook", Prompt.OverrideLine(pinned, "opus", "xhigh"));
    }

    // ---- Which checkouts the dispatch carries ----

    [Fact]
    public void Repositories_are_named_matched_first_unmatched_after_the_primary_marked()
    {
        var repositories = new[]
        {
            new Checkouts.RepositoryLine("/Users/x/code/hatch", "https://example.test/hatch.git", true, null),
            new Checkouts.RepositoryLine(null, "https://example.test/other.git", false, null),
        };

        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), repositories);

        Assert.Contains("## Repositories", prompt, StringComparison.Ordinal);
        Assert.Contains("/Users/x/code/hatch  https://example.test/hatch.git  (primary)", prompt, StringComparison.Ordinal);
        Assert.Contains("https://example.test/other.git  (no checkout here)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void No_repositories_section_when_the_dispatch_carries_none()
    {
        Assert.DoesNotContain("## Repositories", Prompt.Compose(Fixtures.Work("AER-12")), StringComparison.Ordinal);
        Assert.DoesNotContain("## Repositories", Prompt.Compose(Fixtures.Work("AER-12"), []), StringComparison.Ordinal);
    }

    [Fact]
    public void More_than_one_repository_names_which_root_the_tail_means()
    {
        var repositories = new[]
        {
            new Checkouts.RepositoryLine("/Users/x/code/hatch", "https://example.test/hatch.git", true, null),
            new Checkouts.RepositoryLine("/Users/x/code/other", "https://example.test/other.git", false, null),
        };

        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), repositories);

        Assert.Contains(
            "Run these from the repository root - https://example.test/hatch.git's checkout, not the others named above.",
            prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_repository_does_not_need_to_say_which_root_it_means()
    {
        var repositories = new[] { new Checkouts.RepositoryLine("/Users/x/code/hatch", "https://example.test/hatch.git", true, null) };
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), repositories);

        Assert.Contains("Run these from the repository root. The key is already in the environment.",
            prompt, StringComparison.Ordinal);
    }
}

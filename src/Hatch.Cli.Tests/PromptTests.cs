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

        Assert.Contains("## Opening a pull request", prompt, StringComparison.Ordinal);
        Assert.Contains("Open one before this increment ends, if it pushes any commit meant for a person to", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## If you open a pull request", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Only if this increment opens one", prompt, StringComparison.Ordinal);
        Assert.Contains("**Title:** `AER-12 ` and then", prompt, StringComparison.Ordinal);
        Assert.Contains("\n[AER-12](https://hatch.example.test/apps/hatch/issues/AER-12)\n", prompt, StringComparison.Ordinal);
        Assert.Contains("`hatch pr AER-12 <url>`", prompt, StringComparison.Ordinal);
        Assert.Contains("Never write a relative link.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trunk_landing_project_is_told_to_land_rather_than_open_a_pull_request()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", landing: Landings.Trunk));

        Assert.Contains("## Landing on the trunk", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Opening a pull request", prompt, StringComparison.Ordinal);
        Assert.Contains("git pull --no-rebase", prompt, StringComparison.Ordinal);
        Assert.Contains("hatch landed AER-12", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("git pull --rebase", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--force", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_project_still_opens_a_pull_request_not_a_landing()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"));

        Assert.Contains("## Opening a pull request", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Landing on the trunk", prompt, StringComparison.Ordinal);
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
    public void No_branch_on_origin_says_a_pull_request_follows_once_there_is_code_on_it()
    {
        var branches = new[] { new BranchEntry { Path = "/checkouts/repo", Kind = BranchKind.None, Cut = "aer-12-x" } };
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), branches: branches);

        Assert.Contains("cut `aer-12-x` from it.", prompt, StringComparison.Ordinal);
        Assert.Contains(
            "Push to it as the work goes, and open a pull request on it before this increment ends, so there is something for a person to review.",
            prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trunk_landing_project_with_no_branch_is_told_to_commit_on_the_trunk()
    {
        var branches = new[]
        {
            new BranchEntry { Path = "/checkouts/repo", Kind = BranchKind.None, Cut = "aer-12-x", TrunkLanding = true, Trunk = "main" },
        };
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), branches: branches);

        Assert.Contains(
            "this project lands its work on the trunk. origin has no branch for this issue, and none is wanted: the tree is on `main` at origin's tip - commit here, and cut nothing.",
            prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("open a pull request on it before this increment ends", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Do not open a pull request.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trunk_landing_project_whose_only_branch_has_merged_is_told_to_commit_on_the_trunk()
    {
        var branches = new[]
        {
            new BranchEntry
            {
                Path = "/checkouts/repo", Kind = BranchKind.AlreadyMerged, Merged = ["aer-12-old"], Cut = "aer-12-old-2",
                TrunkLanding = true, Trunk = "main",
            },
        };
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), branches: branches);

        Assert.Contains(
            "this project lands its work on the trunk. origin's `aer-12-old` has already merged into the trunk, and no branch is wanted: the tree is on `main` at origin's tip - commit here, and cut nothing.",
            prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("aer-12-old-2", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("cut `", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("pull request on it", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pull_request_project_whose_only_branch_has_merged_is_still_told_to_cut_a_new_one()
    {
        var branches = new[]
        {
            new BranchEntry
            {
                Path = "/checkouts/repo", Kind = BranchKind.AlreadyMerged, Merged = ["aer-12-old"], Cut = "aer-12-old-2",
                TrunkLanding = false, Trunk = "main",
            },
        };
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), branches: branches);

        Assert.Contains("has already merged into the trunk. The tree is on the trunk at origin's tip; cut `aer-12-old-2`", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entered_branch_on_a_trunk_landing_project_is_told_to_merge_into_the_trunk()
    {
        var branches = new[]
        {
            new BranchEntry
            {
                Path = "/checkouts/repo", Kind = BranchKind.Entered, Branch = "aer-12-x", Sha = "abc123def",
                Head = "fed321cba", Ahead = 3, Merge = MergeOutcome.Clean, TrunkLanding = true, Trunk = "main",
            },
        };
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), branches: branches);

        Assert.Contains(
            "This project lands on the trunk: finish on this branch, then merge it into `main`, push `main`, delete the branch on origin, and record the landing.",
            prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entered_branch_off_a_trunk_landing_project_has_no_merge_line()
    {
        var branches = new[]
        {
            new BranchEntry
            {
                Path = "/checkouts/repo", Kind = BranchKind.Entered, Branch = "aer-12-x", Sha = "abc123def",
                Head = "fed321cba", Ahead = 3, Merge = MergeOutcome.Clean,
            },
        };
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"), branches: branches);

        Assert.DoesNotContain("record the landing.", prompt, StringComparison.Ordinal);
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
    public void Each_child_names_its_own_column()
    {
        var work = Fixtures.Work("AER-12", children: [Fixtures.Card("AER-13", statusId: 2), Fixtures.Card("AER-14", statusId: 4)]);
        var prompt = Prompt.Compose(work);

        Assert.Contains("- AER-13  [task]  A child  (To Do)", prompt, StringComparison.Ordinal);
        Assert.Contains("- AER-14  [task]  A child  (In Review)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_child_in_a_column_the_dispatch_did_not_carry_falls_back_to_the_id()
    {
        var work = Fixtures.Work("AER-12", children: [Fixtures.Card("AER-13", statusId: 99)]);
        Assert.Contains("- AER-13  [task]  A child  (99)", Prompt.Compose(work), StringComparison.Ordinal);
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

    // ---- HA-118: a ticket already let go of once or more ----

    [Fact]
    public void A_ticket_let_go_of_gets_a_section_naming_how_many_times()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", letGo: 2));

        Assert.Contains("## This ticket was let go", prompt, StringComparison.Ordinal);
        Assert.Contains("The last 2 increment(s) on this ticket ended without moving it", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ticket_never_let_go_of_gets_no_such_section()
    {
        Assert.DoesNotContain("## This ticket was let go",
            Prompt.Compose(Fixtures.Work("AER-12", letGo: 0)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_let_go_section_comes_before_decisions_already_made()
    {
        var work = Fixtures.Work("AER-12", letGo: 1, questions: [Fixtures.Question(1, answered: true)]);
        var prompt = Prompt.Compose(work);

        Assert.True(
            prompt.IndexOf("## This ticket was let go", StringComparison.Ordinal)
            < prompt.IndexOf("## Decisions already made", StringComparison.Ordinal));
    }

    // ---- HA-352: a ticket marked, then handed back ----

    [Fact]
    public void A_stalled_ticket_gets_a_section_naming_why_and_how_many_times()
    {
        var work = Fixtures.Work(
            "AER-12", letGo: 2, issue: Fixtures.Issue("AER-12", stalledWhy: "left a half-finished merge"));

        var prompt = Prompt.Compose(work);

        Assert.Contains("## This ticket stalled", prompt, StringComparison.Ordinal);
        Assert.Contains("left a half-finished merge", prompt, StringComparison.Ordinal);
        Assert.Contains("It has stalled 2 times in a row.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ticket_never_marked_gets_no_such_section()
    {
        Assert.DoesNotContain("## This ticket stalled",
            Prompt.Compose(Fixtures.Work("AER-12")), StringComparison.Ordinal);
    }

    // ---- HA-395: a ticket sent back for its brief's size ----

    [Fact]
    public void A_ticket_never_sent_back_gets_no_such_section()
    {
        Assert.DoesNotContain("## This ticket was sent back",
            Prompt.Compose(Fixtures.Work("AER-12")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_ticket_sent_back_and_able_to_take_children_is_told_to_cut_it_into_tasks()
    {
        var work = Fixtures.Work(
            "AER-12", issue: Fixtures.Issue("AER-12", type: "story"),
            sentBack: new SentBackDto(Length: 5000, Limit: 4000, CanTakeChildren: true));

        var prompt = Prompt.Compose(work);

        Assert.Contains("## This ticket was sent back", prompt, StringComparison.Ordinal);
        Assert.Contains("5000 characters, over the 4000", prompt, StringComparison.Ordinal);
        Assert.Contains("Cut it into child tasks", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ticket_sent_back_and_unable_to_take_children_is_told_to_file_siblings()
    {
        var work = Fixtures.Work(
            "AER-12", issue: Fixtures.Issue("AER-12", type: "task"),
            sentBack: new SentBackDto(Length: 5000, Limit: 4000, CanTakeChildren: false));

        var prompt = Prompt.Compose(work);

        Assert.Contains("## This ticket was sent back", prompt, StringComparison.Ordinal);
        Assert.Contains("file the excess as new sibling tasks under the same parent", prompt, StringComparison.Ordinal);
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
    public void The_dry_run_names_the_playbook_and_whose_it_was()
    {
        var owned = Fixtures.Work("AER-12") with { Playbook = Fixtures.Playbook(projectKey: "AER") with { Source = "project" } };
        var boardWide = Fixtures.Work("AER-12") with { Playbook = Fixtures.Playbook() with { Source = "board" } };
        var hop = Fixtures.Work("AER-12", hop: true);
        var fileOwned = Fixtures.Work("AER-12", playbookFile: new PlaybookFileReferenceDto(".hatch/playbooks.yaml", "canonical", "main", "abcdef0123456789", "deadbeef"))
            with { Playbook = Fixtures.Playbook(projectKey: "AER") with { Source = "file" } };
        var boardWithFile = Fixtures.Work("AER-12", playbookFile: new PlaybookFileReferenceDto(".hatch/playbooks.yaml", "canonical", "main", "abcdef0123456789", "deadbeef"))
            with { Playbook = Fixtures.Playbook() };

        Assert.Equal("playbook: AER's own", Prompt.PlaybookLine(owned));
        Assert.Equal("playbook: board-wide", Prompt.PlaybookLine(boardWide));
        Assert.Null(Prompt.PlaybookLine(hop));
        Assert.Equal("playbook: AER's own file on main at abcdef0", Prompt.PlaybookLine(fileOwned));
        Assert.Equal("playbook: board-wide on main at abcdef0", Prompt.PlaybookLine(boardWithFile));
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
    // ---- What was said to the agent ----

    [Fact]
    public void Unread_messages_are_carried_under_their_own_heading_after_the_decisions()
    {
        var prompt = Prompt.Compose(Fixtures.Work(
            "AER-12",
            questions: [Fixtures.Question(1, "AER-12", answered: true)],
            messages: [Fixtures.Message(7, body: "use the other table"), Fixtures.Message(8, body: "and rename it")]));

        Assert.Contains("## Said to you since the last session", prompt, StringComparison.Ordinal);
        Assert.Contains("Nathan sent this to you on AER-12 at 2026-09-28T03:00:00+00:00, after the last session on it ended:", prompt, StringComparison.Ordinal);
        Assert.Contains("use the other table", prompt, StringComparison.Ordinal);
        Assert.Contains("and rename it", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("## Decisions already made", StringComparison.Ordinal)
            < prompt.IndexOf("## Said to you since the last session", StringComparison.Ordinal));
        Assert.DoesNotContain("while you were working", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_said_there_is_no_heading()
    {
        Assert.DoesNotContain("## Said to you", Prompt.Compose(Fixtures.Work("AER-12")), StringComparison.Ordinal);
        Assert.DoesNotContain("## Said to you", Prompt.Compose(Fixtures.Work("AER-12", messages: [])), StringComparison.Ordinal);
    }

    [Fact]
    public void An_express_ticket_gets_its_own_section_before_where_it_ends()
    {
        var work = Fixtures.Work("AER-12", issue: Fixtures.Issue("AER-12", preApproval: PreApprovalLevels.ExpressName));
        var prompt = Prompt.Compose(work);

        Assert.Contains("## This ticket is express", prompt, StringComparison.Ordinal);
        Assert.Contains("no session runs and nothing is spent", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("## This ticket is express", StringComparison.Ordinal)
            < prompt.IndexOf("## Where this increment ends", StringComparison.Ordinal));

        // The last line does not change for an express ticket.
        Assert.Contains("AER-12 should be in \"In Review\" when you stop", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_ticket_gets_no_express_section()
    {
        Assert.DoesNotContain("## This ticket is express", Prompt.Compose(Fixtures.Work("AER-12")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_preapproved_ticket_gets_its_own_section_saying_its_pull_request_merges_unattended()
    {
        var work = Fixtures.Work("AER-12", issue: Fixtures.Issue("AER-12", preApproval: PreApprovalLevels.PreApprovedName));
        var prompt = Prompt.Compose(work);

        Assert.Contains("## This ticket is pre-approved", prompt, StringComparison.Ordinal);
        Assert.Contains("nobody reading the diff first", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("## This ticket is pre-approved", StringComparison.Ordinal)
            < prompt.IndexOf("## Where this increment ends", StringComparison.Ordinal));
    }

    // ---- HA-1036: the release-note ask ----

    [Fact]
    public void A_parentless_ticket_moving_into_review_is_asked_for_a_release_note()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", asksReleaseNote: true));

        Assert.Contains("```release-note", prompt, StringComparison.Ordinal);
        Assert.Contains("one line, no body", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("feature announcement", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_epic_asked_for_a_release_note_uses_the_feature_announcement_wording()
    {
        var work = Fixtures.Work("AER-12", asksReleaseNote: true, issue: Fixtures.Issue("AER-12", type: "epic"));
        var prompt = Prompt.Compose(work);

        Assert.Contains("feature announcement", prompt, StringComparison.Ordinal);
        Assert.Contains("/apps/hatch/docs/...", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_flag_false_there_is_no_release_note_ask()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"));

        Assert.DoesNotContain("```release-note", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_note_ask_sits_before_the_work_log_block()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", asksReleaseNote: true));

        Assert.True(
            prompt.IndexOf("```release-note", StringComparison.Ordinal)
            < prompt.IndexOf("```work-log", StringComparison.Ordinal));
    }

    // ---- HA-118: the ending says a stall is recorded as having done nothing ----

    [Fact]
    public void Where_this_increment_ends_says_a_stall_counts_as_nothing_done()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"));

        var section = prompt.IndexOf("## Where this increment ends", StringComparison.Ordinal);
        Assert.True(section >= 0);
        Assert.Contains(
            "recorded as having",
            prompt[section..], StringComparison.Ordinal);
        Assert.Contains("so move it, or ask, before you", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hook_and_the_prompt_say_it_in_the_same_words()
    {
        var message = Fixtures.Message(7);

        var live = Prompt.Message("AER-12", message, whileWorking: true);
        var carried = Prompt.Message("AER-12", message, whileWorking: false);

        // Only the sentence about when it arrived differs.
        Assert.Contains("while you were working:", live, StringComparison.Ordinal);
        Assert.Equal(
            live.Replace(", while you were working:", ":", StringComparison.Ordinal),
            carried.Replace(", after the last session on it ended:", ":", StringComparison.Ordinal));
    }

    // ---- HA-222: the clamp tells a session to wrap up, not to ask ----

    [Fact]
    public void WrapUp_says_commit_push_file_comment_and_stop_but_never_to_ask()
    {
        var text = Prompt.WrapUp("AER-12");

        Assert.Contains("Commit", text, StringComparison.Ordinal);
        Assert.Contains("Push", text, StringComparison.Ordinal);
        Assert.Contains("File whatever is left as tasks", text, StringComparison.Ordinal);
        Assert.Contains("Comment", text, StringComparison.Ordinal);
        Assert.Contains("stop", text, StringComparison.Ordinal);

        Assert.DoesNotContain("question", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hatch ask", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---- HA-377: the prompt's fixed part has a character budget ----

    [Fact]
    public void The_fixed_part_of_the_prompt_stays_under_four_thousand_characters()
    {
        var fixedPart = string.Join('\n', Prompt.Tail(
            "AER-12", "In Review", "https://hatch.example.test/apps/hatch/issues/AER-12", null, preApproval: PreApprovalLevels.NoneName));

        Assert.True(fixedPart.Length < 4000,
            $"The prompt's fixed part is {fixedPart.Length} characters - trim it back under 4,000.");
    }

    [Fact]
    public void The_reaching_hatch_block_points_at_comments_not_show_and_says_the_brief_is_current()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12"));

        Assert.Contains("hatch comments AER-12", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("hatch show AER-12", prompt, StringComparison.Ordinal);
        Assert.Contains("the brief exactly as it stands", prompt, StringComparison.Ordinal);
    }

    // ---- HA-557: a check's own prompt reaches Hatch with a verdict, not a move ----

    [Fact]
    public void A_check_s_prompt_records_a_verdict_instead_of_starting_or_moving_the_issue()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", isCheck: true));

        Assert.Contains("hatch verdict AER-12 pass|fail [\"findings\"]", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("hatch start AER-12", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("hatch move AER-12 <column>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_check_s_ending_names_both_the_pass_and_the_fail_destination()
    {
        var prompt = Prompt.Compose(Fixtures.Work("AER-12", to: "In Review", isCheck: true, failStatusName: "Implementation"));

        var section = prompt[prompt.IndexOf("## Where this increment ends", StringComparison.Ordinal)..];
        Assert.Contains("a pass sends it to \"In Review\"", section, StringComparison.Ordinal);
        Assert.Contains("a fail sends it to \"Implementation\"", section, StringComparison.Ordinal);
    }

    // ---- HA-557: the last check's findings are carried forward after a fail ----

    [Fact]
    public void The_last_check_s_findings_are_shown_when_present_and_absent_otherwise()
    {
        var withFindings = Prompt.Compose(Fixtures.Work("AER-12", lastCheckFindings: "The build script hardcodes a path."));
        Assert.Contains("## The last check", withFindings, StringComparison.Ordinal);
        Assert.Contains("The build script hardcodes a path.", withFindings, StringComparison.Ordinal);

        var withoutFindings = Prompt.Compose(Fixtures.Work("AER-12"));
        Assert.DoesNotContain("## The last check", withoutFindings, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_check_section_is_capped_and_past_the_cap_the_findings_are_left_out()
    {
        var big = string.Join('\n', Enumerable.Range(0, 2000).Select(i => new string('x', 20) + i));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(big) > Prompt.MaxCheckFindingsSectionBytes);

        var prompt = Prompt.Compose(Fixtures.Work("AER-12", lastCheckFindings: big));

        Assert.Contains("## The last check", prompt, StringComparison.Ordinal);
        Assert.Contains("_findings left out: this section is at its size cap_", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(big, prompt, StringComparison.Ordinal);
    }

    // ---- HA-1210: a session is pointed at hatch-planning.md, never the whole of hatch.md ----

    [Fact]
    public void The_prompt_never_points_a_session_at_the_whole_of_hatch_md()
    {
        var composed = new[]
        {
            Prompt.Compose(Fixtures.Work("AER-12")),
            Prompt.Compose(Fixtures.Work("AER-12", landing: Landings.Trunk)),
            Prompt.Compose(Fixtures.Work("AER-12", noLink: true)),
        };

        foreach (var text in composed)
            Assert.Contains("docs/hatch-planning.md", text, StringComparison.Ordinal);

        // A session's context is smaller than the file, so hatch.md may only be pointed at by anchor, never handed over whole.
        foreach (var text in composed.Append(Prompt.WrapUp("AER-12")))
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, @"docs/hatch[.]md(?!#)"),
                $"a prompt points at the whole of docs/hatch.md:\n{text}");
    }
}

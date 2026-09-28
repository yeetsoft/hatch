using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hatch.Cli.Tests;

/// <summary>
/// The wire records the runner reads, built as records rather than as strings of
/// JSON.
/// </summary>
/// <remarks>
/// This is the whole payoff of a shared contract: a fixture that goes stale
/// against the server does not silently deserialise into a dispatch with empty
/// fields, it stops compiling.
/// </remarks>
public static class Fixtures
{
    /// <summary>
    /// How the stub wire writes what it answers with.
    /// </summary>
    /// <remarks>
    /// Reflective, and deliberately not <see cref="HatchClient.Json"/>. That one
    /// resolves through a source-generated table because the shipped binary is
    /// trimmed and reflection is switched off in it (<c>HatchJson</c>) - a
    /// constraint on the client. <see cref="Wire"/> is standing in for the
    /// server, which serialises reflectively, and holding the fixtures to the
    /// client's table would mean registering an array shape in production code
    /// because a test happened to write one.
    /// </remarks>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static StatusDto Status(int id, string name, bool terminal = false, bool deferred = false) =>
        new(id, name, id, terminal, deferred, "#888888");

    public static IssueDto Issue(
        string key, string type = "task", string title = "A ticket", string description = "The brief.",
        string? modelOverride = null, string? effortOverride = null, string? parentKey = null,
        bool expedited = false, string? pullRequestUrl = null) =>
        new(
            Key: key, ProjectId: 1, ProjectKey: "AER", Type: type, Title: title, Description: description,
            StatusId: 3, Rank: 1000, ParentKey: parentKey, ChildKeys: [], DependsOnKeys: [], DependentKeys: [],
            ReadyAt: null, DueAt: null, PullRequestUrl: pullRequestUrl,
            ModelOverride: modelOverride, EffortOverride: effortOverride, Assignee: null,
            CreatedBy: "somebody", CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
            Expedited: expedited);

    public static IssueCardDto Card(string key, string type = "task", string title = "A child") =>
        new(key, "AER", type, title, 3, 1000, null, null, null);

    public static PlaybookDto Playbook(string model = "opus", string effort = "high") =>
        new(1, 3, "In Progress", 4, "In Review", [], "Do the thing.", model, effort, DateTimeOffset.UnixEpoch);

    public static WorkDto Work(
        string key,
        string from = "In Progress",
        string? to = "In Review",
        string? blocked = null,
        IReadOnlyList<QuestionDto>? questions = null,
        IReadOnlyList<IssueCardDto>? children = null,
        IReadOnlyList<WorkRepositoryDto>? repositories = null,
        IssueDto? issue = null,
        string? issueUrl = null,
        bool noLink = false,
        string kind = WorkKinds.Advance,
        IReadOnlyList<CommentDto>? messages = null) =>
        new(
            Issue: issue ?? Issue(key),
            FromStatus: Status(3, from),
            ToStatus: to is null ? null : Status(kind == WorkKinds.Conflicts ? 3 : 4, to),
            Playbook: Playbook(),
            Children: children ?? [],
            Repositories: repositories ?? [],
            Questions: questions ?? [],
            Blocked: blocked,
            IssueUrl: noLink ? null : issueUrl ?? $"https://hatch.example.test/apps/hatch/issues/{key}",
            Kind: kind,
            Messages: messages);

    /// <summary>The dispatch of an issue in review whose branch conflicts: review to itself, with the board's verdict on it.</summary>
    public static WorkDto ConflictWork(
        string key, string trunk = "main", IReadOnlyList<MergeCheckDto>? checks = null,
        IReadOnlyList<WorkRepositoryDto>? repositories = null) =>
        Work(
            key, from: "In Review", to: "In Review", repositories: repositories,
            issue: Issue(key) with { MergeChecks = checks ?? [MergeCheck(MergeVerdicts.Conflicted, trunk, files: "a.txt")] },
            kind: WorkKinds.Conflicts);

    public static WorkRepositoryDto Repository(
        string remote, string? canonical = null, string? baseBranch = null, bool primary = false,
        string? matchedRemote = null) =>
        new(remote, canonical ?? remote, baseBranch, primary, matchedRemote);

    public static QueueEntryDto Row(string key, string? blocked = null, bool expedited = false) =>
        new(Issue(key, expedited: expedited), Status(3, "In Progress"), Status(4, "In Review"), blocked);

    /// <summary>A verdict as a runner would have put it, conflicted unless said otherwise.</summary>
    public static MergeCheckDto MergeCheck(
        string verdict = MergeVerdicts.Conflicted, string trunk = "main", string canonical = "example.com/o/r",
        params string[] files) =>
        new(
            $"https://{canonical}.git", canonical, trunk, new string('a', 40), verdict,
            verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? "aer-1-a-thing" : null,
            verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? new string('b', 40) : null,
            files, DateTimeOffset.UnixEpoch, "runner", "hatch");

    /// <summary>One row of the review read: an issue in review, and what the board holds about its branch.</summary>
    public static ReviewCheckDto Review(
        string key, IReadOnlyList<WorkRepositoryDto>? repositories = null, params MergeCheckDto[] checks) =>
        new(key, repositories ?? [], checks);

    /// <summary>An issue in review whose branch has stopped merging, and the queue row that says to resolve it.</summary>
    public static QueueEntryDto ConflictRow(string key, string trunk = "main", params string[] files) =>
        new(
            Issue(key) with { MergeChecks = [MergeCheck(MergeVerdicts.Conflicted, trunk, files: files)] },
            Status(4, "In Review"), Status(4, "In Review"), null, WorkKinds.Conflicts);

    public static CommentDto Comment(string body = "ok") =>
        new(1, "hatch", body, "comment", null, null, DateTimeOffset.UnixEpoch);

    /// <summary>A message to the agent, unread unless it is told it was read.</summary>
    public static CommentDto Message(
        long id, string body = "use the other table", string author = "Nathan",
        DateTimeOffset? deliveredAt = null, string? deliveredTo = null) =>
        new(id, author, body, "message", null, null, DateTimeOffset.Parse("2026-09-28T03:00:00+00:00"), deliveredAt, deliveredTo);

    public static QuestionDto Question(long id, string key = "AER-1", string body = "Which way?", bool answered = false) =>
        new(
            Id: id, IssueKey: key, IssueTitle: "A ticket", Body: body, AskedBy: "hatch",
            AskedAt: DateTimeOffset.UnixEpoch, Options: null,
            Answers: answered
                ? [new CommentDto(id + 100, "Nathan", "That way.", "answer", id, null, DateTimeOffset.UnixEpoch)]
                : []);

    public static WorkLogEntryDto WorkLogRow(decimal cost = 1.5m, long tokens = 12_345, long durationMs = 65_000) =>
        new(
            Id: 1, SessionId: "s-1", StartedAt: DateTimeOffset.UnixEpoch, EndedAt: DateTimeOffset.UnixEpoch,
            DurationMs: durationMs, Title: "Did a thing", Summary: "In detail.", Described: true, IsError: false,
            Turns: 12, CostUsd: cost, InputTokens: tokens, OutputTokens: 0, CacheCreationTokens: 0,
            CacheReadTokens: 0, TotalTokens: tokens, Models: []);

    public static string Taken(Guid token, int ttlSeconds = 300) =>
        JsonSerializer.Serialize(
            new ClaimTakenDto(token, "hatch", DateTimeOffset.UnixEpoch, ttlSeconds), Json);

    // ---- The events a session emits ----

    public static string Init(string sessionId = "s-1") =>
        $$"""{"type":"system","subtype":"init","session_id":"{{sessionId}}"}""";

    public static string ToolUse(string name, string command) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new { content = new[] { new { type = "tool_use", name, input = new { command } } } },
        });

    public static string Result(
        string sessionId = "s-1", decimal cost = 1.5m, int turns = 12, bool error = false, string said = "") =>
        JsonSerializer.Serialize(new
        {
            type = "result",
            session_id = sessionId,
            duration_ms = 65_000,
            num_turns = turns,
            is_error = error,
            total_cost_usd = cost,
            modelUsage = new Dictionary<string, object>
            {
                ["claude-opus-5"] = new
                {
                    inputTokens = 100,
                    outputTokens = 200,
                    cacheCreationInputTokens = 300,
                    cacheReadInputTokens = 400,
                    costUSD = cost,
                },
            },
            result = said,
        });
}

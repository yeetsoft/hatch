namespace Hatch.Cli;

/// <summary>
/// The reads and writes a runner makes about a ticket, named once so that no
/// call site spells a route.
/// </summary>
/// <remarks>
/// Every one of these writes an event carrying the key's name as the actor, so
/// the trail says who did what without anybody being asked to record it. The
/// work log is the deliberate exception - a meter reading is not a decision.
/// </remarks>
public sealed class Board(HatchClient client)
{
    public HatchClient Client { get; } = client;

    /// <summary>
    /// This machine's offset from UTC in minutes, which is what the API wants in
    /// order to fold ready dates against the caller's calendar day rather than
    /// the server's.
    /// </summary>
    public static int OffsetMinutes(DateTimeOffset now) => (int)now.Offset.TotalMinutes;

    /// <summary>
    /// Every issue a pass would look at, in the order it looks, each with the
    /// reason it would be folded past - or nothing, where it is clear.
    /// </summary>
    public async Task<IReadOnlyList<QueueEntryDto>> QueueAsync(
        IReadOnlyList<CheckoutEntry> checkouts, string? under, int offsetMinutes, CancellationToken ct,
        bool clones = false, bool mine = false)
    {
        var scope = string.IsNullOrEmpty(under) ? "" : $"&ancestorKey={Uri.EscapeDataString(under)}";
        var narrowed = mine ? "&mine=true" : "";
        return await Client.GetAsync<List<QueueEntryDto>>(
            $"/api/hatch/work/queue?offsetMinutes={offsetMinutes}{scope}{narrowed}{Declare(checkouts, clones)}", ct) ?? [];
    }

    /// <summary>
    /// Every issue in the review column that this runner holds a checkout for,
    /// and what the board holds about each one's branch - what the poll reads
    /// before it asks git anything. Declares the checkouts the way the queue
    /// does, and no more: a poll clones nothing.
    /// </summary>
    public async Task<IReadOnlyList<ReviewCheckDto>> ReviewAsync(
        IReadOnlyList<CheckoutEntry> checkouts, CancellationToken ct)
    {
        var parts = DeclareParts(checkouts);
        var query = parts.Count == 0 ? "" : $"?{string.Join('&', parts)}";
        return await Client.GetAsync<List<ReviewCheckDto>>($"/api/hatch/work/review{query}", ct) ?? [];
    }

    /// <summary>
    /// Every runner that has spoken to this Hatch lately - what
    /// <see cref="Checkout.RunnerAsync"/> reads to skip a name a live runner
    /// elsewhere already holds. Throws on a Hatch too old to have the route,
    /// the same as every other read here; the caller decides what that means.
    /// </summary>
    public async Task<IReadOnlyList<RunnerDto>> RunnersAsync(CancellationToken ct) =>
        await Client.GetAsync<List<RunnerDto>>("/api/hatch/runners", ct) ?? [];

    /// <summary>
    /// A verdict on one issue's branch in one repository - see
    /// <see cref="MergeCheckRequest"/>. The remote is spelled the way this
    /// runner has it; the board canonicalises it.
    /// </summary>
    public Task<MergeCheckDto?> MergeCheckAsync(string key, MergeCheckRequest request, CancellationToken ct) =>
        Client.WriteAsync<MergeCheckDto>(HttpMethod.Put, $"/api/hatch/issues/{key}/merge-check", request, ct);

    /// <summary>
    /// What the build on the tip of one issue's branch came to in one repository -
    /// see <see cref="BuildCheckRequest"/>. The remote is spelled the way this
    /// runner has it; the board canonicalises it.
    /// </summary>
    public Task<BuildCheckDto?> BuildCheckAsync(string key, BuildCheckRequest request, CancellationToken ct) =>
        Client.WriteAsync<BuildCheckDto>(HttpMethod.Put, $"/api/hatch/issues/{key}/build-check", request, ct);

    /// <summary>
    /// Every trunk verdict the board holds, read once per poll and matched
    /// locally against each checkout - a trunk is nobody's issue, so there is
    /// no issue to ride the verdict in on the way the branch checks do.
    /// </summary>
    public async Task<IReadOnlyList<TrunkBuildDto>> TrunkBuildsAsync(CancellationToken ct) =>
        await Client.GetAsync<List<TrunkBuildDto>>("/api/hatch/trunk-builds", ct) ?? [];

    /// <summary>
    /// What the build on the tip of one repository's trunk came to - see
    /// <see cref="TrunkBuildRequest"/>. The remote is spelled the way this
    /// runner has it; the board canonicalises it.
    /// </summary>
    public Task<TrunkBuildDto?> TrunkBuildAsync(TrunkBuildRequest request, CancellationToken ct) =>
        Client.WriteAsync<TrunkBuildDto>(HttpMethod.Put, "/api/hatch/trunk-builds", request, ct);

    /// <summary>
    /// The dispatch for one named issue.
    /// </summary>
    /// <param name="heldToken">
    /// The claim this runner is holding, so that its own lease does not fold its
    /// own dispatch. Absent everywhere a caller holds nothing.
    /// </param>
    public async Task<WorkDto?> WorkAsync(
        IReadOnlyList<CheckoutEntry> checkouts, string key, Guid? heldToken, CancellationToken ct,
        bool clones = false)
    {
        var parts = new List<string>();
        if (heldToken is { } token) parts.Add($"heldToken={token}");
        parts.AddRange(DeclareParts(checkouts, clones));

        var query = parts.Count == 0 ? "" : $"?{string.Join('&', parts)}";
        return WithIssueUrl(await Client.GetAsync<WorkDto>($"/api/hatch/work/{key}{query}", ct));
    }

    /// <summary>
    /// Carries an express issue one column right, with no session - see
    /// docs/hatch.md, "The hop". A <c>409</c> is not a fault: the row changed
    /// between the scan and this call, the same as a lost claim race, and the
    /// caller walks on to the next clear row rather than treating it as a
    /// refusal.
    /// </summary>
    public async Task<(IssueDto? Issue, string? WalkOn)> HopAsync(
        IReadOnlyList<CheckoutEntry> checkouts, string key, CancellationToken ct, bool clones = false)
    {
        var parts = DeclareParts(checkouts, clones);
        var query = parts.Count == 0 ? "" : $"?{string.Join('&', parts)}";
        var answer = await Client.Send(HttpMethod.Post, $"/api/hatch/work/{key}/hop{query}", null, ct);

        if (answer.Conflict) return (null, answer.Sentence);
        if (!answer.Ok) throw new HatchException(Client.Refusal(answer, $"/api/hatch/work/{key}/hop"));
        if (answer.Body.Trim().Length == 0) return (null, null);

        return (System.Text.Json.JsonSerializer.Deserialize(answer.Body, HatchJson.Default.IssueDto), null);
    }

    /// <summary>
    /// Confirms a pull request's merge against the one recorded on an issue
    /// waiting in review - see <see cref="PullRequestMergedRequest"/>. A
    /// <c>409</c> is not a fault, the same as <see cref="HopAsync"/> documents:
    /// the row changed between the poll's read and this write, and the caller
    /// walks on rather than treating it as a refusal.
    /// </summary>
    public async Task<(IssueDto? Issue, string? WalkOn)> MergedAsync(
        string key, PullRequestMergedRequest request, CancellationToken ct)
    {
        var answer = await Client.Send(HttpMethod.Post, $"/api/hatch/work/{key}/merged", request, ct);

        if (answer.Conflict) return (null, answer.Sentence);
        if (!answer.Ok) throw new HatchException(Client.Refusal(answer, $"/api/hatch/work/{key}/merged"));
        if (answer.Body.Trim().Length == 0) return (null, null);

        return (System.Text.Json.JsonSerializer.Deserialize(answer.Body, HatchJson.Default.IssueDto), null);
    }

    /// <summary>
    /// What a pass would take, without taking it.
    /// </summary>
    /// <remarks>
    /// The one caller left is <c>work --dry-run</c>, which spawns nothing and so
    /// holds nothing - and a walk that claimed in order to print a prompt would
    /// be the one thing a dry run must not do. Everywhere an increment actually
    /// follows, the queue picks and the claim decides; see <see cref="Picker"/>
    /// for why the second read there is the named one.
    /// </remarks>
    public async Task<WorkDto?> NextAsync(
        IReadOnlyList<CheckoutEntry> checkouts, string? under, int offsetMinutes, CancellationToken ct,
        bool clones = false, bool mine = false)
    {
        var scope = string.IsNullOrEmpty(under) ? "" : $"&ancestorKey={Uri.EscapeDataString(under)}";
        var narrowed = mine ? "&mine=true" : "";
        return WithIssueUrl(await Client.GetAsync<WorkDto>(
            $"/api/hatch/work/next?offsetMinutes={offsetMinutes}{scope}{narrowed}{Declare(checkouts, clones)}", ct));
    }

    /// <summary>
    /// This runner's checkouts, as query parameters: a repeatable <c>remote=</c>
    /// per checkout that has one, and <c>standing=true</c> where one of them is
    /// the process's own. Prefixed with <c>&amp;</c> for a query that already has
    /// something in it, which every caller's does.
    /// </summary>
    private static string Declare(IReadOnlyList<CheckoutEntry> checkouts, bool clones = false) =>
        DeclareParts(checkouts, clones) is { Count: > 0 } parts ? $"&{string.Join('&', parts)}" : "";

    private static List<string> DeclareParts(IReadOnlyList<CheckoutEntry> checkouts, bool clones = false)
    {
        var parts = checkouts.Where(c => c.Remote is not null)
            .Select(c => $"remote={Uri.EscapeDataString(c.Remote!)}")
            .ToList();
        if (checkouts.Any(c => c.Standing)) parts.Add("standing=true");
        if (clones) parts.Add("clones=true");
        return parts;
    }

    /// <summary>
    /// The dispatch, with a link to its issue whenever one can be made. Hatch
    /// answers with one only where the install has a public origin configured;
    /// otherwise the origin this runner reached Hatch at is the best address
    /// there is. Left null where that is not an absolute http(s) URL, so a
    /// session is never handed a relative link.
    /// </summary>
    private WorkDto? WithIssueUrl(WorkDto? work)
    {
        if (work is null || !string.IsNullOrEmpty(work.IssueUrl)) return work;

        return Uri.TryCreate(Client.Origin, UriKind.Absolute, out var origin)
            && origin.Scheme is "http" or "https"
                ? work with { IssueUrl = $"{Client.Origin}/apps/hatch/issues/{work.Issue.Key}" }
                : work;
    }

    public async Task<IReadOnlyList<QuestionDto>> QuestionsAsync(string? key, bool open, CancellationToken ct)
    {
        var path = string.IsNullOrEmpty(key)
            ? $"/api/hatch/questions?open={open.ToString().ToLowerInvariant()}"
            : $"/api/hatch/issues/{key}/questions?open={open.ToString().ToLowerInvariant()}";

        return await Client.GetAsync<List<QuestionDto>>(path, ct) ?? [];
    }

    public async Task<IReadOnlyList<IssueCardDto>> UnderAsync(string ancestorKey, CancellationToken ct) =>
        await Client.GetAsync<List<IssueCardDto>>(
            $"/api/hatch/issues?ancestorKey={Uri.EscapeDataString(ancestorKey)}", ct) ?? [];

    public Task<CommentDto?> CommentAsync(string key, string body, CancellationToken ct) =>
        Client.PostAsync<CommentDto>($"/api/hatch/issues/{key}/comments", new CommentCreateRequest(body), ct);

    public Task<CommentDto?> AskAsync(
        string key, string body, IReadOnlyList<QuestionOptionDto> options, CancellationToken ct) =>
        Client.PostAsync<CommentDto>(
            $"/api/hatch/issues/{key}/comments",
            new CommentCreateRequest(body, Kind: "question", Options: options.Count > 0 ? options : null),
            ct);

    /// <summary>
    /// Marks messages to the agent read, and answers with the ones this call
    /// marked - so a caller learns what it has to say, not what exists. Exactly
    /// <paramref name="ids"/> when given, every unread one when null.
    /// </summary>
    public async Task<IReadOnlyList<CommentDto>> DeliverMessagesAsync(
        string key, IReadOnlyList<long>? ids, CancellationToken ct) =>
        await Client.PostAsync<List<CommentDto>>(
            $"/api/hatch/issues/{key}/messages/deliver", new MessageDeliverRequest(ids), ct) ?? [];

    public Task<WorkLogEntryDto?> WorkLogAsync(string key, WorkLogEntryRequest entry, CancellationToken ct) =>
        Client.PostAsync<WorkLogEntryDto>($"/api/hatch/issues/{key}/work-log", entry, ct);

    // ---- The conversational half, which used to be jq over curl ----

    /// <summary>The columns and every card on them, in the board's own order.</summary>
    public Task<BoardDto?> BoardAsync(CancellationToken ct) =>
        Client.GetAsync<BoardDto>("/api/hatch/board", ct);

    /// <summary>The columns alone, which is what <c>show</c> needs to name one.</summary>
    public async Task<IReadOnlyList<StatusDto>> StatusesAsync(CancellationToken ct) =>
        await Client.GetAsync<List<StatusDto>>("/api/hatch/statuses", ct) ?? [];

    /// <summary>One issue, whole: the brief, its edges and where it is.</summary>
    public Task<IssueDto?> IssueAsync(string key, CancellationToken ct) =>
        Client.GetAsync<IssueDto>($"/api/hatch/issues/{key}", ct);

    public async Task<IReadOnlyList<CommentDto>> CommentsAsync(string key, CancellationToken ct) =>
        await Client.GetAsync<List<CommentDto>>($"/api/hatch/issues/{key}/comments", ct) ?? [];

    /// <summary>Into a column. The board decides the rank; nothing here has an opinion about it.</summary>
    public Task<IssueDto?> MoveAsync(string key, int statusId, CancellationToken ct) =>
        Client.PostAsync<IssueDto>($"/api/hatch/issues/{key}/move", new IssueMoveRequest(statusId, null, null), ct);

    /// <summary>One field at a time, which is all the CLI ever edits.</summary>
    public Task<IssueDto?> PatchAsync(string key, IssuePatchRequest patch, CancellationToken ct) =>
        Client.WriteAsync<IssueDto>(HttpMethod.Patch, $"/api/hatch/issues/{key}", patch, ct);

    /// <summary><paramref name="key"/> waits on <paramref name="dependsOnKey"/>.</summary>
    public Task<IssueDto?> DependAsync(string key, string dependsOnKey, CancellationToken ct) =>
        Client.PostAsync<IssueDto>(
            $"/api/hatch/issues/{key}/dependencies", new IssueDependencyRequest(dependsOnKey), ct);

    /// <summary>...no longer.</summary>
    public Task<IssueDto?> UndependAsync(string key, string dependsOnKey, CancellationToken ct) =>
        Client.WriteAsync<IssueDto>(
            HttpMethod.Delete, $"/api/hatch/issues/{key}/dependencies/{dependsOnKey}", null, ct);

    /// <summary>An answer to a question, which is a comment that names the one it answers.</summary>
    public Task<CommentDto?> AnswerAsync(string key, long questionId, string body, CancellationToken ct) =>
        Client.PostAsync<CommentDto>(
            $"/api/hatch/issues/{key}/comments",
            new CommentCreateRequest(body, Kind: "answer", AnswersId: questionId),
            ct);
}

/// <summary>The reasons a pass folded past what it folded past, counted rather than listed.</summary>
/// <remarks>
/// A column of two hundred cards folded for four reasons is four facts, and the
/// list of two hundred is not one of them.
/// </remarks>
public static class Digest
{
    /// <summary>The folded rows, grouped by reason, commonest first and then alphabetically.</summary>
    public static IReadOnlyList<string> Of(IReadOnlyList<QueueEntryDto> queue)
    {
        var folded = queue.Where(q => q.Blocked is { Length: > 0 }).ToList();
        if (folded.Count == 0) return [];

        var groups = folded
            .GroupBy(q => q.Blocked!)
            .Select(g => (Count: g.Count(), Why: g.Key))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Why, StringComparer.Ordinal)
            .ToList();

        var width = groups.Max(g => g.Count.ToString().Length);
        return groups.Select(g => $"hatch:     {g.Count.ToString().PadLeft(width)}  {g.Why}").ToList();
    }
}

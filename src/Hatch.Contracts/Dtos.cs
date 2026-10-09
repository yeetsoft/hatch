using System.Text.Json;

namespace Hatch.Contracts;

// ---- Projects ----

/// <param name="IssueCount">
/// What the delete guard will look at, so the Projects page can grey the button
/// rather than offer a 409.
/// </param>
/// <param name="Color">
/// <c>#rrggbb</c>, lower case, or null if nobody has chosen one yet - see
/// <see cref="EfHatchProject.Color"/>.
/// </param>
/// <param name="Icon">A slug naming one of a closed set of stock icons, or null if nobody has chosen one yet - see <see cref="EfHatchProject.Icon"/>.</param>
/// <param name="Repositories">The remotes this project is bound to, in order - the first is the primary.</param>
/// <param name="LogoUpdatedAt">When the project's logo was last written, or null if it has none.</param>
public record ProjectDto(
    int Id, string Key, string Name, int IssueCount, DateTimeOffset CreatedAt, string? Color, string? Icon,
    IReadOnlyList<ProjectRepositoryDto> Repositories, DateTimeOffset? LogoUpdatedAt);

/// <summary>A stored image, as the markdown that shows it will name it: <c>/api/hatch/images/{id}</c>.</summary>
public record ImageDto(Guid Id);

/// <summary>
/// A new project. <paramref name="Key"/> is checked against
/// <see cref="EfHatchProject.KeyPattern"/> and against every existing key. It
/// can be changed later, at a price the operator is shown first - see
/// <see cref="EfHatchProject.Key"/>.
/// </summary>
/// <param name="Color"><c>#rrggbb</c>, or absent to leave it unset.</param>
/// <param name="Icon">A slug naming one of a closed set of stock icons, or absent to leave it unset.</param>
public record ProjectCreateRequest(string Key, string Name, string? Color = null, string? Icon = null);

/// <summary>
/// A rename. <paramref name="Name"/> is what usually moves;
/// <paramref name="Key"/> is the one that costs something and is null in almost
/// every request - see <see cref="EfHatchProject.Key"/> for what a rekey breaks
/// and what it does not.
/// </summary>
/// <param name="Color">Null leaves it alone; <c>""</c> clears it; a hex value like <c>#6b7280</c> sets it.</param>
/// <param name="Icon">Null leaves it alone; <c>""</c> clears it; a slug sets it.</param>
public record ProjectPatchRequest(string? Name, string? Key = null, string? Color = null, string? Icon = null);

/// <summary>
/// One git remote bound to a project, as every client reads it back.
/// </summary>
/// <param name="Canonical">
/// The matching identity <c>RemoteIdentity.Canonical</c> folded <paramref name="Remote"/> to -
/// what a second entry is checked against, never recomputed by a client.
/// </param>
public record ProjectRepositoryDto(string Remote, string Canonical, string? BaseBranch);

/// <summary>
/// One entry in the ordered list a <c>PUT</c> replaces the whole set with -
/// the first in the array is the primary.
/// </summary>
public record ProjectRepositoryWriteRequest(string Remote, string? BaseBranch);

// ---- Statuses ----

/// <param name="Color">
/// <c>#rrggbb</c>, lower case. Carried on every column the board draws, because
/// the colour is what makes a column identifiable at a glance once the cards in
/// it are too narrow to read.
/// </param>
/// <param name="IsDeferred">
/// Whether this column is parked work rather than a lane on the board. Every
/// column is sent on every read, deferred ones included: the board drops them
/// when it draws itself, and the issue page needs them in order to offer the
/// move into one. A reader that wants the board's shape wants the columns this
/// is false on - the same list <c>Columns</c> measures the board off.
/// </param>
/// <param name="IsWip">
/// The stored flag: whether the operator has ticked this column into the WIP
/// section, whatever else is true of it. Not the same question as "does this
/// column count towards the limit" - a deferred or terminal column keeps
/// whatever it was flagged, and it is <c>WipSectionDto.StatusIds</c> that
/// answers the counted question. See <see cref="EfHatchStatus.IsWip"/>.
/// </param>
/// <param name="IsImplementation">
/// The stored tick: whether the operator has named this column as where code
/// gets written. A tick on a deferred or terminal column is carried as stored
/// and ignored by the reader, the same stranded-flag tolerance as
/// <paramref name="IsWip"/>. See <see cref="EfHatchStatus.IsImplementation"/>.
/// </param>
public record StatusDto(
    int Id, string Name, int SortOrder, bool IsTerminal, bool IsDeferred, bool IsWip, string Color,
    bool ExpressSkips = false, bool ParentPulls = false, bool AgentFiles = false, bool IsImplementation = false);

/// <summary>
/// A new column. The optional fields each have a server-side default -
/// rightmost position, neither terminal nor deferred, and
/// <see cref="EfHatchStatus.DefaultColor"/> - so the shortest way to add a
/// column is still a name.
///
/// There is no <c>IsWip</c> here or on <see cref="StatusPatchRequest"/>: this
/// route accepts the <c>hatch</c> scope on every verb, and the WIP section is
/// the operator's to set - see <see cref="EfHatchStatus.IsWip"/> and
/// <c>WipController</c>.
/// </summary>
public record StatusCreateRequest(
    string Name, int? SortOrder, bool? IsTerminal, string? Color = null, bool? IsDeferred = null);

/// <summary>Every field optional: null means "leave this one alone". No <c>IsWip</c> - see <see cref="StatusCreateRequest"/>.</summary>
public record StatusPatchRequest(
    string? Name, int? SortOrder, bool? IsTerminal, string? Color = null, bool? IsDeferred = null);

// ---- WIP ----

/// <summary>
/// The board's WIP section, as the settings route reads it: one set of
/// columns, and every slice Hatch knows - <c>story,bug</c> then <c>epic</c>,
/// always both, in that order.
/// </summary>
/// <param name="StatusIds">
/// The flagged columns that actually count: <see cref="StatusDto.IsWip"/> is
/// true and the column is neither deferred nor terminal, in board order. A flag
/// stranded on a column that has since become deferred or terminal is left out
/// here and cleared by the next <see cref="WipSectionRequest"/> that names
/// <see cref="StatusIds"/>, whatever it names - see <c>Wip.SectionAsync</c>.
/// </param>
/// <param name="Slices">Always two entries, <c>story,bug</c> then <c>epic</c>.</param>
public record WipSectionDto(IReadOnlyList<int> StatusIds, IReadOnlyList<WipSliceDto> Slices);

/// <summary>
/// One slice of the section: which types it counts, and what its limit is, or
/// null where no row is held for it - a slice with no row still exists, and
/// still has a load, it just gates nothing.
/// </summary>
public record WipSliceDto(IReadOnlyList<string> Types, int? Limit);

/// <summary>
/// A write to the section: absent or null leaves a field alone, the bulk rule
/// every other clearable number in Hatch already follows - see
/// <see cref="RunnerPatchRequest"/>.
/// </summary>
/// <param name="Limit">
/// Absent or null leaves the stories-and-bugs limit alone; <c>""</c> (or
/// whitespace) removes the row; a whole number of one or more sets it.
/// Anything else is refused with a sentence.
/// </param>
/// <param name="StatusIds">
/// Absent or null leaves the section alone; otherwise the whole section - not a
/// delta - so <c>[]</c> clears it. Every column in the house is set to
/// <see cref="StatusDto.IsWip"/> true if it is named here and false otherwise.
/// A deferred or terminal column named here is refused with a sentence.
/// </param>
/// <param name="EpicLimit">The same rule as <paramref name="Limit"/>, for the epic slice.</param>
public record WipSectionRequest(string? Limit = null, IReadOnlyList<int>? StatusIds = null, string? EpicLimit = null);

/// <summary>
/// Whether an express issue standing in this column is carried on to the next
/// one with no session. One required boolean, for the reason
/// <see cref="ExpressRequest"/> is: the same route both ticks and unticks it,
/// and the caller says which it meant.
/// </summary>
public record ExpressSkipsRequest(bool ExpressSkips);

/// <summary>
/// Whether a child standing in this column is carried on to the next one with
/// no session while its parent stands in the implementation column. One
/// required boolean, for the reason <see cref="ExpressSkipsRequest"/> is: the
/// same route both ticks and unticks it, and the caller says which it meant.
/// </summary>
public record ParentPullsRequest(bool ParentPulls);

/// <summary>
/// Whether this column is where an issue filed by a program is born. One
/// required boolean, for the reason <see cref="ExpressSkipsRequest"/> is: the
/// same route both ticks and unticks it, and the caller says which it meant.
/// </summary>
public record AgentFilesRequest(bool AgentFiles);

/// <summary>
/// Whether this column is where code gets written. One required boolean, for
/// the reason <see cref="ExpressSkipsRequest"/> is: the same route both ticks
/// and unticks it, and the caller says which it meant.
/// </summary>
public record ImplementationRequest(bool IsImplementation);

// ---- Issues ----

/// <summary>
/// Who an issue belongs to: a person, or an API key, or - said as
/// <c>null</c> wherever this appears - nobody at all.
/// </summary>
/// <remarks>
/// <para>Hatch's own wire shape built from
/// <see cref="Hatch.Api.Services.Auth.Actor"/>, so the module's payload stays
/// the module's and the platform record can grow a field without changing what
/// a board read looks like.</para>
///
/// <para>Null is the only way "nobody" is said: there is no empty-object form,
/// so a client's test is <c>assignee &amp;&amp; …</c> and never a comparison
/// against a sentinel id. An assignee whose person has been deleted or whose
/// key has been revoked reads as null too, at every reader at once - see
/// <see cref="Hatch.Api.Services.Auth.IActorDirectory"/>.</para>
/// </remarks>
/// <param name="Kind"><c>person</c> or <c>key</c> - see <see cref="Hatch.Api.Services.Auth.ActorKind"/>.</param>
public record AssigneeDto(string Kind, Guid Id, string Name);

/// <summary>
/// A card. What the board draws, and nothing more - descriptions and comments
/// are a detail-page request, because the board holds every issue in the house
/// and shipping every description with it would make the first paint the
/// slowest one.
/// </summary>
/// <param name="Key">The display key, <c>AER-12</c>. Computed from the project and number, never stored.</param>
/// <param name="ReadyAt">
/// Carried on the card because the board decides what to fold away with it -
/// see <see cref="BoardDto"/>.
/// </param>
/// <param name="OpenQuestions">
/// How many questions on this issue nobody has answered. A count rather than
/// the questions themselves: the board draws a badge and the detail page is
/// where they are read. Defaulted, because the lists that are not the board -
/// an issue's children, a search result - are not places anybody answers a
/// question from, and counting for them would be a query nobody reads.
/// </param>
/// <param name="Assignee">
/// Who owns this card, or null for nobody - which is most of the board, and
/// which is why the card draws no element at all rather than an empty chip.
/// Trailing and defaulted for the reason <paramref name="OpenQuestions"/> is,
/// though every list that draws a card fills it: an assignee that read one way
/// through the board and another through the plan is the divergence this record
/// exists to prevent.
/// </param>
/// <param name="Expedited">
/// <em>This one first.</em> An expedited card is served above every
/// non-expedited card in its column, whatever its rank, and the client slices
/// the one ordered list rather than sorting for itself - so the board, the plan
/// and the queue cannot disagree about where a card sits. Trailing and
/// defaulted for the reason <paramref name="OpenQuestions"/> is, though every
/// list that draws a card fills it. Derived from the effective level - the one
/// <paramref name="Priority"/> names - as
/// <c>effective >= PriorityLevels.Expedited</c> - nothing writes it directly
/// any more.
/// </param>
/// <param name="Priority">
/// The effective level's name - see <see cref="PriorityLevels"/> -
/// <c>"economy"</c>, <c>"normal"</c>, <c>"expedited"</c> or <c>"emergency"</c>.
/// Walked live, at read, to the nearest ancestor - including this issue itself
/// - that is not Normal; never stored or copied. Trailing and defaulted for
/// the reason <paramref name="OpenQuestions"/> is, though every list that
/// draws a card fills it.
/// </param>
/// <param name="PriorityOwn">The level this issue's own row carries, regardless of what it inherits - see <see cref="EfHatchIssue.Priority"/>.</param>
/// <param name="PriorityFrom">
/// The ancestor <paramref name="Priority"/> was inherited from, or null when
/// the effective level is this issue's own, or Normal.
/// </param>
public record IssueCardDto(
    string Key,
    string ProjectKey,
    string Type,
    string Title,
    int StatusId,
    long Rank,
    string? ParentKey,
    string? ReadyAt,
    string? DueAt,
    int OpenQuestions = 0,
    AssigneeDto? Assignee = null,
    IssueClaimDto? Claim = null,
    bool Expedited = false,
    bool Express = false,
    string Priority = PriorityLevels.NormalName,
    string PriorityOwn = PriorityLevels.NormalName,
    string? PriorityFrom = null);

/// <summary>
/// The lease a running dispatcher holds on an issue, or null where nothing
/// holds it - see <see cref="EfHatchIssue.ClaimToken"/>. An expired claim
/// projects as null rather than as a claim with an old heartbeat: the
/// arithmetic is the server's, and a card drawing a holder that stopped
/// existing four hours ago is worse than a card drawing nothing.
/// </summary>
/// <remarks>
/// There is deliberately no token here. The token is a capability - it is what
/// a heartbeat and a release present - and a board read that carried it would
/// let anybody holding a board read steal or refresh a lease. What a client
/// needs is who, from where, and how recently, which is all of this.
/// </remarks>
/// <param name="Chatter">A line the holder is carrying: what it is doing right now, or null if it has not said.</param>
public record IssueClaimDto(
    string ClaimedBy,
    string Runner,
    DateTimeOffset ClaimedAt,
    DateTimeOffset HeartbeatAt,
    string? Chatter,
    DateTimeOffset? ChatterAt,
    /// <summary>
    /// The lease this claim is judged against, in seconds - the same value
    /// <see cref="ClaimTakenDto"/> carries, on the read as well as on the take.
    /// Here rather than beside the claim so that a claim is self-describing
    /// wherever one is drawn: a card on the board, a child row on the plan, a
    /// section on the issue page. A client that had to fetch the TTL from
    /// somewhere else could draw a claim before it knew what "recently" meant.
    ///
    /// It is not a second copy of the rule. <see cref="IssueClaims.IsLive"/>
    /// still decides whether there is a claim at all, and this only says how
    /// much of the lease there was to spend - which is what lets a client draw
    /// a runner that has gone quiet without deciding for itself when one is
    /// gone.
    /// </summary>
    int TtlSeconds);

/// <summary>One issue, whole - the detail page's payload.</summary>
/// <param name="ChildKeys">Its stories, or its tasks. Keys rather than nested issues: the page links to them and does not draw them.</param>
/// <param name="DependsOnKeys">
/// What must be done before this is implemented, in key order. An edge is
/// satisfied only once the issue it names sits in a terminal column, so a
/// blocker in review still blocks - anything softer and the second story is
/// built on the first one's unmerged branch.
/// </param>
/// <param name="DependentKeys">
/// The issues waiting on this one - the same edges read backwards. Named for
/// what they are rather than "blocked", which has two readings.
/// </param>
/// <param name="ReadyAt">
/// When the issue becomes workable, or null if it always was. A bare date
/// (<c>2026-09-12</c>) or an instant (<c>2026-09-12T17:00:00Z</c>) - the two
/// forms mean different things and <see cref="IssueMoment"/> says how.
/// </param>
/// <param name="DueAt">When it is owed, in the same two forms, or null.</param>
/// <param name="PullRequestUrl">
/// Where the work is being reviewed, or null while it is nowhere. An absolute
/// http(s) URL - see <see cref="EfHatchIssue.PullRequestUrl"/> for why it is one
/// and not a list of them.
/// </param>
/// <param name="ModelOverride">
/// The model every increment dispatched for this issue runs on, or null for
/// whatever the playbook for its next move names. Readable by a key - an agent
/// may see what it is being spent on - and writable only by a person, through
/// <see cref="IssuePlaybookController"/>.
/// </param>
/// <param name="EffortOverride">The same, for the thinking budget, and independent of it.</param>
/// <param name="Assignee">
/// Who owns it, or null for nobody. Beside <paramref name="CreatedBy"/> because
/// the two are the same kind of fact - who filed it, and whose it is now -
/// though only one of them can change. Readable by a key, and writable only by
/// a person through <see cref="AssigneeController"/>: under the loop's
/// <c>people only</c> rule an assignee is a dispatch gate, and a key that could
/// write one could hand itself work somebody had reserved.
/// </param>
/// <param name="Expedited">
/// <em>This one first.</em> The board floats it to the top of its column and
/// the dispatcher considers it before anything else - and nothing else changes,
/// because it is a sort key and not a gate. Readable by a key for the reason
/// <paramref name="ModelOverride"/> is, and writable only by a person through
/// <see cref="IssueExpediteController"/>: a key that could set one could put
/// its own ticket at the front of every night. Derived from the effective
/// level - the one <paramref name="Priority"/> names - as
/// <c>effective >= PriorityLevels.Expedited</c> - nothing writes it directly
/// any more.
/// </param>
/// <param name="Priority">
/// The effective level's name - see <see cref="PriorityLevels"/> -
/// <c>"economy"</c>, <c>"normal"</c>, <c>"expedited"</c> or <c>"emergency"</c>.
/// Walked live, at read, to the nearest ancestor - including this issue itself
/// - that is not Normal; never stored or copied.
/// </param>
/// <param name="PriorityOwn">The level this issue's own row carries, regardless of what it inherits - see <see cref="EfHatchIssue.Priority"/>.</param>
/// <param name="PriorityFrom">
/// The ancestor <paramref name="Priority"/> was inherited from, or null when
/// the effective level is this issue's own, or Normal.
/// </param>
/// <param name="WipLimit">
/// How many of an epic's stories may be in progress at once, or null for
/// <see cref="IssueWipLimitRequest.DefaultLimit"/> - meaningful on an epic and
/// on nothing else. Readable by a key, like every other field a dispatch
/// needs, and writable only by a person through
/// <see cref="IssueWipLimitController"/>: a key that could raise its own
/// epic's ceiling could pull more of its own stories into progress at once.
/// </param>
public record IssueDto(
    string Key,
    int ProjectId,
    string ProjectKey,
    string Type,
    string Title,
    string Description,
    int StatusId,
    long Rank,
    string? ParentKey,
    IReadOnlyList<string> ChildKeys,
    IReadOnlyList<string> DependsOnKeys,
    IReadOnlyList<string> DependentKeys,
    string? ReadyAt,
    string? DueAt,
    string? PullRequestUrl,
    string? ModelOverride,
    string? EffortOverride,
    AssigneeDto? Assignee,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IssueClaimDto? Claim = null,
    bool Expedited = false,
    IReadOnlyList<MergeCheckDto>? MergeChecks = null,
    IReadOnlyList<BuildCheckDto>? BuildChecks = null,
    bool Express = false,
    string Priority = PriorityLevels.NormalName,
    string PriorityOwn = PriorityLevels.NormalName,
    string? PriorityFrom = null,
    int? WipLimit = null);

/// <summary>Taking the lease: who is asking is the credential's to say, so the body names only where from.</summary>
/// <param name="Runner">The checkout holding it - <c>host:/path/to/checkout</c>, as the runner names itself.</param>
public record ClaimRequest(string Runner)
{
    /// <summary>
    /// The longest runner the server will accept. Here rather than on the
    /// entity because both sides of the wire need it: the API refuses a longer
    /// one, and the runner shortens its own name to fit rather than discovering
    /// the limit as a 400 that stops a night's loop.
    /// </summary>
    public const int MaxRunnerLength = 240;

    /// <summary>
    /// The longest line a claim will carry. The server truncates rather than
    /// refusing - a lease is not worth losing to a wide terminal - so this is
    /// the length past which a client is writing for nobody.
    /// </summary>
    public const int MaxChatterLength = 512;
}

/// <summary>
/// A lease, just taken. The TTL rides back with it rather than being configured
/// on both sides: the server is what honours it, so the server is what says
/// what it is.
/// </summary>
/// <param name="Token">
/// The capability. Presented by every heartbeat and by the release, and the
/// only place it is ever handed out - it is on no read anywhere.
/// </param>
/// <param name="StallLapseSeconds">
/// The window this claim goes quiet under - the same one a stall question
/// lapses by, in seconds, or <c>0</c> when lapsing is off. The server is what
/// honours it, so the server is what says what it is, the same reason
/// <paramref name="TtlSeconds"/> rides back rather than being configured on
/// both sides.
/// </param>
public record ClaimTakenDto(Guid Token, string ClaimedBy, DateTimeOffset ClaimedAt, int TtlSeconds, int StallLapseSeconds = 0);

/// <summary>
/// What a heartbeat answers where the caller is the runner the board has
/// chosen to preempt, instead of the plain <c>204</c> it answers every other
/// time - see <c>Preemption</c> for the five rules that decide this. An older
/// <c>hatch</c> reads only whether the call succeeded and is unaffected by the
/// new shape.
/// </summary>
/// <param name="Key">The emergency issue's key - what the runner is being asked to make room for.</param>
/// <param name="Title">Its title, so a runner can say why on the ticket it puts down without a second call.</param>
public record ClaimPreemptedDto(string Key, string Title);

/// <summary>
/// How a release said an increment ended, on <c>DELETE …/claim?token=…</c> -
/// what a runner may name, and nothing else. Absent is accepted exactly as it
/// always has been: the pick's own release, a restart, and an older CLI all
/// send no outcome at all.
/// </summary>
public static class ClaimOutcomes
{
    /// <summary>The increment spent itself and left the ticket where it found it.</summary>
    public const string Dropped = "dropped";

    /// <summary>The increment moved the ticket, or otherwise finished what it set out to do.</summary>
    public const string Worked = "worked";

    /// <summary>
    /// The board itself ordered the ticket put down, for an emergency - not a
    /// choice the increment made, so <c>WorkController.LetGoAsync</c>'s count
    /// of increments in a row that left a ticket where they found it neither
    /// counts this nor resets on it.
    /// </summary>
    public const string Preempted = "preempted";

    public static bool IsValid(string outcome) => outcome is Dropped or Worked or Preempted;
}

/// <summary>
/// The six levels an issue's priority can sit at, ordered - see
/// <see cref="EfHatchIssue.Priority"/>.
/// </summary>
public static class PriorityLevels
{
    public const int Paused = -3;
    public const int Economy = -2;
    public const int Low = -1;
    public const int Normal = 0;
    public const int Expedited = 1;
    public const int Emergency = 2;

    public const string PausedName = "paused";
    public const string EconomyName = "economy";
    public const string LowName = "low";
    public const string NormalName = "normal";
    public const string ExpeditedName = "expedited";
    public const string EmergencyName = "emergency";

    /// <summary>The level's name, for the wire - see <see cref="TryParse"/> for the reverse.</summary>
    public static string Name(int level) => level switch
    {
        Emergency => EmergencyName,
        Expedited => ExpeditedName,
        Low => LowName,
        Economy => EconomyName,
        Paused => PausedName,
        _ => NormalName,
    };

    /// <summary>The name's level, or <see langword="false"/> for anything that is not one of the six.</summary>
    public static bool TryParse(string? name, out int level)
    {
        (level, var ok) = name switch
        {
            EmergencyName => (Emergency, true),
            ExpeditedName => (Expedited, true),
            LowName => (Low, true),
            EconomyName => (Economy, true),
            NormalName => (Normal, true),
            PausedName => (Paused, true),
            _ => (Normal, false),
        };
        return ok;
    }
}

/// <summary>
/// Still here. The token is the whole authorization; the line is optional and
/// cosmetic.
/// </summary>
/// <param name="Chatter">
/// What the holder is doing right now. Absent leaves whatever the claim was
/// carrying alone, and <c>""</c> clears it. Trimmed to its first line and
/// truncated rather than refused - a lease is not worth losing to a wide
/// terminal.
/// </param>
public record ClaimHeartbeatRequest(Guid Token, string? Chatter);

/// <summary>
/// A new issue. It lands in the leftmost status and at the bottom of that
/// column - the server decides both, so no client has to know what "the inbox"
/// is called this week.
/// </summary>
public record IssueCreateRequest(
    int ProjectId,
    string Type,
    string Title,
    string? Description,
    string? ParentKey,
    string? ReadyAt,
    string? DueAt);

/// <summary>
/// An edit. Every field is optional and null means "leave this alone", which
/// leaves one thing that needs saying out loud: an empty
/// <paramref name="ParentKey"/> - <c>""</c> - clears the parent. A JSON body
/// cannot otherwise distinguish "no opinion" from "no parent", and the empty
/// string is unambiguous because no issue key can ever be one.
///
/// <paramref name="ReadyAt"/>, <paramref name="DueAt"/> and
/// <paramref name="PullRequestUrl"/> read the empty string the same way, and for
/// the same reason: no date and no URL is written as "".
/// </summary>
/// <param name="PullRequestUrl">
/// An absolute <c>http</c> or <c>https</c> URL, or <c>""</c> to take the issue
/// off the one it holds. Anything else is refused with a sentence - a relative
/// path or a bare <c>github.com/...</c> is a link that would not open, and the
/// whole point of the field is that it opens.
/// </param>
/// <param name="ProjectId">
/// Which project the issue should belong to. No "clear" sentinel - an issue
/// always belongs to some project, so unlike <paramref name="ParentKey"/>
/// there is no empty-string meaning here.
/// </param>
/// <param name="MoveDescendants">
/// <c>null</c> reads as <c>true</c> - moving the whole subtree is the
/// default, and <c>false</c> is the one a caller has to ask for.
/// </param>
/// <param name="WipOverride">
/// <c>true</c> to move into a full WIP section anyway - a person's call, and a
/// key sending it is refused with <c>403</c>, whatever the load. Bulk never
/// sets it.
/// </param>
public record IssuePatchRequest(
    string? Title,
    string? Description,
    string? Type,
    int? StatusId,
    string? ParentKey,
    string? ReadyAt,
    string? DueAt,
    string? PullRequestUrl,
    int? ProjectId,
    bool? MoveDescendants,
    bool WipOverride = false);

/// <summary>
/// A drop on the board: which column, and which cards it landed between. The
/// client names neighbours and never a rank - the server owns the number
/// (docs/hatch.md, "Rank computation"), which is what keeps every client
/// dumb, Claude included.
/// </summary>
/// <param name="AfterKey">The card immediately above the drop, or null at the top of the column.</param>
/// <param name="BeforeKey">The card immediately below it, or null at the bottom.</param>
/// <param name="FromStatusId">
/// The column the caller believes the card is in. A card that has left that
/// column is refused rather than moved - which is how an undo can never
/// overrule a move somebody made since. Null asks nothing of where it is now.
/// </param>
/// <param name="WipOverride">
/// <c>true</c> to move into a full WIP section anyway - a person's call, and a
/// key sending it is refused with <c>403</c>, whatever the load.
/// </param>
public record IssueMoveRequest(
    int StatusId, string? AfterKey, string? BeforeKey, int? FromStatusId = null, bool WipOverride = false);

// ---- Searching and editing in bulk ----

/// <summary>
/// What a bulk edit acted on, and what it refused. Never an exception and never
/// a partial-looking success: an issue whose edit could not be applied is named
/// here with the sentence saying why, and nothing about it was written.
/// </summary>
/// <param name="Changed">The keys that actually moved. An issue already holding every named value is not one of them.</param>
/// <param name="Unchanged">Keys that matched the request but had nothing to change - re-applying a bulk edit is not an edit.</param>
/// <param name="Failures">Keys the edit was refused for, each with its reason.</param>
/// <param name="Rekeyed">
/// The <c>{from, to}</c> pair for every issue a project move actually moved -
/// the named issue and, when the move carried descendants, every one of
/// those too, named in the request or not. Empty when no project was named.
/// </param>
public record IssueBulkResultDto(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<IssueBulkFailureDto> Failures,
    IReadOnlyList<IssueBulkRekeyedDto> Rekeyed);

public record IssueBulkFailureDto(string Key, string Reason);

public record IssueBulkRekeyedDto(string From, string To);

/// <summary>
/// The <c>409</c> body a move or a patch is refused with when the WIP section
/// is full - <c>{ error, load, limit }</c>, following <c>AuthErrorDto</c>'s
/// <c>{ error }</c> shape (<c>src/Hatch.Api/Models/Auth/Dtos.cs</c>) so the
/// browser can ask without parsing prose. <see cref="Load"/> is the load
/// <em>before</em> the move - what a dialog quotes as "5 of 5" - not the load
/// an accepted override would leave.
/// </summary>
public record WipRefusalDto(string Error, int Load, int Limit);

/// <summary>
/// One edit applied to many issues. The fields are
/// <see cref="IssuePatchRequest"/>'s, minus the three - title, description and
/// pull request URL - that describe a single issue and could only be applied to
/// a hundred of them by mistake.
///
/// Null still means "leave this alone", and the empty string still clears, so
/// "take the due date off all of these" is <c>dueAt: ""</c> and nothing else.
/// </summary>
/// <param name="Keys">
/// The issues to edit, named rather than described. The filter that found them
/// is the client's business: a request that re-ran a query server-side could
/// act on a row that arrived between the operator reading the list and pressing
/// the button, which is the one thing a bulk edit must never do.
/// </param>
/// <param name="ProjectId">
/// Which project the issue should belong to. No "clear" sentinel - an issue
/// always belongs to some project, so unlike <paramref name="ParentKey"/>
/// there is no empty-string meaning here.
/// </param>
/// <param name="MoveDescendants">
/// <c>null</c> reads as <c>true</c> - moving the whole subtree is the
/// default, and <c>false</c> is the one a caller has to ask for.
/// </param>
public record IssueBulkEditRequest(
    IReadOnlyList<string> Keys,
    string? Type = null,
    int? StatusId = null,
    string? ParentKey = null,
    string? ReadyAt = null,
    string? DueAt = null,
    int? ProjectId = null,
    bool? MoveDescendants = null);

// ---- Comments and events ----

/// <param name="Kind">
/// <c>""</c> for an ordinary note, <c>"question"</c>, <c>"answer"</c> or
/// <c>"message"</c> - see <see cref="EfHatchComment.Kind"/> for why those are a
/// column.
/// </param>
/// <param name="AnswersId">The question this answers, on the same issue. Null on everything else.</param>
/// <param name="Options">
/// The answers a question offers, or null on one asked in prose. See
/// <see cref="EfHatchComment.Options"/>.
/// </param>
/// <param name="DeliveredAt">
/// When a <c>message</c> was put in front of a session, or null while it has
/// not been - and always null on any other kind.
/// </param>
/// <param name="DeliveredTo">The runner that was holding the issue then, or the caller's name where nothing was.</param>
public record CommentDto(
    long Id,
    string Author,
    string Body,
    string Kind,
    long? AnswersId,
    IReadOnlyList<QuestionOptionDto>? Options,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt = null,
    string? DeliveredTo = null);

/// <summary>
/// Mark messages as read, and get back the ones this call marked.
/// </summary>
/// <param name="Ids">
/// Exactly these messages, or null for every unread one on the issue. Ids that
/// are not unread messages on this issue are ignored, not refused.
/// </param>
public record MessageDeliverRequest(IReadOnlyList<long>? Ids = null);

/// <summary>
/// One answer a question offers up front.
///
/// A label and a detail rather than one string, because they are read at
/// different moments: the label is what is scanned down a list and what becomes
/// the answer's own text, and the detail is what is read once, by somebody
/// deciding between two of them.
/// </summary>
/// <param name="Label">
/// The choice, as it will be said. Short enough to press and to read back in a
/// thread six months later - "child-weighted", not a sentence about weighting.
/// </param>
/// <param name="Detail">What taking it means, and what it costs. Optional; a self-evident choice does not need one.</param>
/// <param name="Recommended">
/// The one the asker would take. At most one per question - a recommendation
/// that covers two options is not a recommendation.
/// </param>
public record QuestionOptionDto(string Label, string? Detail = null, bool Recommended = false)
{
    public const int MaxLabelLength = 120;
    public const int MaxDetailLength = 2_000;

    /// <summary>
    /// Enough to cover a decision and few enough to read without scrolling. A
    /// question with nine answers is two questions.
    /// </summary>
    public const int MaxPerQuestion = 8;
}

/// <summary>
/// A new comment. <paramref name="Kind"/> is omitted by everything that just
/// wants to say something, which is most callers and every caller that predates
/// questions.
/// </summary>
/// <param name="Options">
/// Offered answers, on a question only. Omitted asks in prose, which stays
/// legal - not every decision is a menu.
/// </param>
public record CommentCreateRequest(
    string Body,
    string? Kind = null,
    long? AnswersId = null,
    IReadOnlyList<QuestionOptionDto>? Options = null);

/// <summary>
/// A question and whatever has been said back to it - what the CLI walks
/// through, what the issue page threads together, and what the board counts.
/// </summary>
/// <param name="IssueKey">
/// Carried on the question rather than looked up, because the house-wide list
/// is read by somebody answering several tickets in a row and a question
/// without its ticket is unanswerable.
/// </param>
/// <param name="Answers">Oldest first. Empty is what "open" means; there is no second flag saying so.</param>
/// <param name="Options">The answers it offers, or null if it was asked in prose.</param>
/// <param name="Obviated">
/// Whether the issue this question is on has reached a terminal column - left
/// out of the list a person answers from, kept in the record.
/// </param>
public record QuestionDto(
    long Id,
    string IssueKey,
    string IssueTitle,
    string Body,
    string AskedBy,
    DateTimeOffset AskedAt,
    IReadOnlyList<QuestionOptionDto>? Options,
    IReadOnlyList<CommentDto> Answers,
    bool Obviated = false);

/// <param name="Payload">
/// <c>{ "from": …, "to": … }</c> for an edit, the source filename for an
/// import - served as JSON rather than as a string of JSON, so a client reads
/// it without a second parse.
/// </param>
public record IssueEventDto(long Id, string Actor, string Kind, JsonElement? Payload, DateTimeOffset At);

/// <summary>One row of <see cref="ActivityPageDto"/> - the same shape as <see cref="IssueEventDto"/>, naming the issue it belongs to.</summary>
public record ActivityEventDto(
    long Id, string IssueKey, string Actor, string Kind, JsonElement? Payload, DateTimeOffset At);

/// <summary>
/// A page of the event trail across every issue, newest first.
/// </summary>
/// <param name="HasMore">
/// Whether another page sits behind this one. There is no total: the page
/// says whether there is another, not how many there are.
/// </param>
public record ActivityPageDto(IReadOnlyList<ActivityEventDto> Events, bool HasMore);

// ---- What is waiting on a person ----

/// <summary>
/// An issue standing in the review column with somewhere to review it, and
/// not held back by a conflict or a failed build.
/// </summary>
/// <param name="PullRequestUrl">
/// Non-null, unlike <see cref="IssueDto.PullRequestUrl"/>: an issue with
/// nowhere to review it is not a row here at all. It is counted instead - see
/// <see cref="AttentionDto.InReviewWithoutPullRequest"/> - because a control
/// that lit up for a ticket nobody can act on is a control nobody reads after
/// a week. Nor is an issue whose pull request is listed under
/// <see cref="AttentionDto.Conflicts"/> or <see cref="AttentionDto.FailingBuilds"/>
/// in the same response - see <see cref="AttentionDto.ReviewsHeldBack"/> - for
/// the same reason: a person cannot act on either.
/// </param>
/// <param name="BuildState">
/// One of <see cref="ReviewBuildStates"/>: whether every repository's build on
/// the branch's current tip passed. "Current tip" and "counted" are
/// <see cref="ReviewWork"/>'s own rules, so this agrees with the dispatcher
/// about what the branch's build is. Because a row held back by
/// <see cref="AttentionDto.FailingBuilds"/> never reaches this list,
/// <see cref="ReviewBuildStates.Failure"/> is drawn and tested but never seen
/// here in practice. Defaults to <see cref="ReviewBuildStates.Unknown"/> so an
/// older client still reads.
/// </param>
/// <param name="HoldsTrunk">
/// Whether the branch already holds the trunk's tip, from the counted
/// repositories' clean merge checks - <c>null</c> when none has said.
/// Non-null only alongside <paramref name="Trunk"/>.
/// </param>
/// <param name="Trunk">The trunk's name as a counted clean merge check reported it, or <c>null</c> when <paramref name="HoldsTrunk"/> is.</param>
public record ReviewDto(
    string Key,
    string Title,
    string Type,
    string PullRequestUrl,
    string BuildState = ReviewBuildStates.Unknown,
    bool? HoldsTrunk = null,
    string? Trunk = null);

/// <summary>
/// The two things that stop a night, in one read: a pull request nobody has
/// reviewed, and a question nobody has answered.
///
/// One endpoint rather than two because the answer is drawn as a single number.
/// Two polls can disagree by a poll interval, and that disagreement shows up as
/// a lit control whose panel is empty - so both halves are read at one instant
/// or neither is.
/// </summary>
/// <param name="Reviews">
/// The review column's own issues that carry a pull request, in that column's
/// board order, and are not held back by a conflict or a failed build - see
/// <see cref="ReviewsHeldBack"/>. Which column is the review column is
/// measured and not named (<c>Columns.AwaitingReview</c>); a board too short
/// to have one answers with none rather than with an error.
/// </param>
/// <param name="InReviewWithoutPullRequest">
/// How many issues stand in that column with no pull request recorded. Not
/// listed and not counted towards the badge, but said out loud in the empty
/// state, so a ticket whose agent forgot <c>hatch pr</c> is still visible
/// without being loud.
/// </param>
/// <param name="Questions">
/// Every open question in the house, oldest first - the same list, the same
/// order and the same definition of open that <c>/api/hatch/questions</c>
/// answers with, because it is the same call.
/// </param>
/// <param name="Conflicts">
/// The review column's issues whose branch conflicts with the trunk in at
/// least one repository, in that column's board order, whether or not they
/// carry a pull request. Not counted towards the badge: a conflict is the
/// loop's to fix, and one it cannot fix becomes a stall, which is a question,
/// which already lights the control.
/// </param>
/// <param name="FailingBuilds">
/// The review column's issues whose build on the branch's tip failed, or is
/// still running with a check that has already failed, in at least one
/// repository, in that column's board order. Not counted towards the badge,
/// for the reason <paramref name="Conflicts"/> is not: the loop fixes a
/// failing build, and one it cannot fix becomes a question, which already
/// lights the control.
/// </param>
/// <param name="ReviewsHeldBack">
/// How many issues in that column carry a pull request but are held back -
/// listed under <paramref name="Conflicts"/> or <paramref name="FailingBuilds"/>
/// in this same response, so a pull request never appears in both halves.
/// Held back because a red build or a conflict is not ready for a person, and
/// the loop is already on it. Defaults to zero so an older client still reads.
/// </param>
/// <param name="TrunkBuilds">
/// Every repository's trunk whose latest build failed, or is still running
/// with a check that has already failed - a person's to fix, unlike
/// <paramref name="FailingBuilds"/>, because no agent owns a trunk. Ordered by
/// canonical, then trunk. Defaults to null so an older client still reads.
/// </param>
/// <param name="ExhaustedRunners">
/// Every runner that is still being heard from and is out of Claude usage,
/// soonest reset first. Not counted towards the badge, which is a dot rather
/// than a pill - see <see cref="AttentionDto"/>'s remarks. Defaults to empty so
/// an older client still reads.
/// </param>
public record AttentionDto(
    IReadOnlyList<ReviewDto> Reviews,
    int InReviewWithoutPullRequest,
    IReadOnlyList<QuestionDto> Questions,
    IReadOnlyList<ConflictDto> Conflicts,
    IReadOnlyList<FailingBuildDto>? FailingBuilds = null,
    int ReviewsHeldBack = 0,
    IReadOnlyList<TrunkBuildDto>? TrunkBuilds = null,
    IReadOnlyList<ExhaustedRunnerDto>? ExhaustedRunners = null);

/// <summary>
/// One runner out of Claude usage, for the top of the attention panel.
/// </summary>
/// <param name="Name">What the runner calls itself - the same string the Runners page keys on.</param>
/// <param name="Where">The machine and checkout it runs from, <c>host:/path</c>, or null when it never said.</param>
/// <param name="ExhaustedUntil">When it expects to reset.</param>
public record ExhaustedRunnerDto(string Name, string? Where, DateTimeOffset ExhaustedUntil);

/// <summary>
/// One issue in review whose branch conflicts with the trunk: what the panel
/// draws, and the words that say where.
/// </summary>
/// <param name="Checks">Only the conflicted verdicts, one per repository, so a clean repository beside a conflicted one is not listed as a conflict.</param>
public record ConflictDto(
    string Key,
    string Title,
    string Type,
    string? PullRequestUrl,
    IReadOnlyList<MergeCheckDto> Checks);

/// <summary>
/// One issue in review whose build failed, or is still running with a check
/// that has already failed: what the panel draws, and the checks that name
/// where.
/// </summary>
/// <param name="Checks">Only the failed or still-failing verdicts, one per repository, so a passing repository beside a failing one is not listed.</param>
public record FailingBuildDto(
    string Key,
    string Title,
    string Type,
    string? PullRequestUrl,
    IReadOnlyList<BuildCheckDto> Checks);

// ---- The board ----

/// <summary>
/// One request, the whole board: the columns in order and every card in the
/// house. Deliberately not paged - this is one household's work, and a board
/// that arrives in pieces cannot answer "what is in progress" in one glance.
/// </summary>
/// <remarks>
/// Every card, including the ones whose <see cref="IssueCardDto.ReadyAt"/> has
/// not arrived. The browser folds those behind a per-column count and the
/// server does not, because a default that silently drops rows leaves a client
/// unable to tell an empty board from a filtered one - and an agent asking what
/// it may work on has one comparison to make instead of a flag to know about.
/// </remarks>
/// <param name="Wip">
/// How full the WIP section is right now, or null only where the board has
/// never turned WIP on at all - no column flagged - so a board that has never
/// turned this on serves exactly what it served before. Present, with both
/// slices' limits null, wherever a column is flagged but no limit has been
/// typed.
/// </param>
public record BoardDto(IReadOnlyList<StatusDto> Statuses, IReadOnlyList<IssueCardDto> Issues, WipDto? Wip = null);

/// <summary>
/// How full the WIP section is right now, the shape <c>GET /board</c> carries
/// and <c>hatch board</c> prints its lines from. Not <see cref="WipSectionDto"/>:
/// that is the settings route's shape (a nullable limit, no load); this one
/// carries a load for every slice, whether or not it has a limit.
/// </summary>
/// <param name="StatusIds">The counted columns, in board order. Never includes a deferred or terminal column, whatever it is flagged.</param>
/// <param name="Slices">Always two entries, <c>story,bug</c> then <c>epic</c>.</param>
public record WipDto(IReadOnlyList<int> StatusIds, IReadOnlyList<WipSliceLoadDto> Slices);

/// <summary>One slice's load against its limit, or against no limit at all.</summary>
/// <param name="Types">The issue types this slice counts.</param>
/// <param name="Limit">How many issues of <paramref name="Types"/> may sit across the counted columns at once, or null for no limit.</param>
/// <param name="Load">How many counted issues are on the board right now - inside the section, plus <paramref name="ClaimedInbound"/>.</param>
/// <param name="ClaimedInbound">Of <paramref name="Load"/>, how many are outside the section but claimed and on their way in.</param>
public record WipSliceLoadDto(IReadOnlyList<string> Types, int? Limit, int Load, int ClaimedInbound);

// ---- The importer ----

/// <summary>
/// How far along an imported node is, before it is matched to a column. The
/// parser reads a shape and this names it; the import endpoint is the only
/// thing that knows which status row a shape lands in, because column names are
/// the operator's to change.
/// </summary>
public enum PlanState
{
    Todo,
    InProgress,
    Done,
}

/// <summary>One checkbox from a plan.</summary>
public record ParsedTask(string Title, string Description, PlanState State);

/// <summary>One <c>## Phase</c> section: its heading, its prose, and its checkboxes.</summary>
public record ParsedStory(string Title, string Description, PlanState State, IReadOnlyList<ParsedTask> Tasks);

/// <summary>
/// One uploaded plan file, as the issues it would become. Handed back by
/// <c>preview</c> and handed in again to <c>import</c> unchanged - so what the
/// operator approved on screen is exactly what gets written, with no second
/// parse in between to disagree with the first.
/// </summary>
public record ParsedEpic(
    string Filename,
    string Title,
    string Description,
    PlanState State,
    IReadOnlyList<ParsedStory> Stories);

/// <summary>
/// One plan typed or pasted straight into the page, rather than uploaded as a
/// file. The same document by the time the parser sees it - which is the point:
/// a plan that only ever existed in somebody's clipboard should reach the board
/// by the same road as one that lives in <c>docs/plans</c>, not by a second
/// implementation of the same reading.
/// </summary>
/// <param name="Title">
/// What this document is called. It plays the part a filename plays for an
/// upload: the name every imported issue's provenance line carries, and the
/// epic's own title when the body has no <c>#</c> heading of its own.
/// </param>
/// <param name="Body">The markdown, exactly as the file would have held it.</param>
public record PastedPlan(string Title, string Body);

/// <param name="Docs">The trees from <c>preview</c>, whichever of them the operator kept.</param>
public record ImportRequest(int ProjectId, IReadOnlyList<ParsedEpic> Docs);

/// <param name="Key">The epic's display key, so the result list can link to what it made.</param>
public record ImportedEpicDto(string Filename, string Key, string Title, int StoryCount, int TaskCount);

public record ImportResultDto(IReadOnlyList<ImportedEpicDto> Epics, int IssueCount);

// ---- Playbooks ----

/// <summary>One row of the matrix: a transition, the types and shape it speaks for, and what to spend on them.</summary>
/// <param name="Types">Empty means every type.</param>
/// <param name="Shape">One of "any", "leaf" or "parent" - whether the issue's children are consulted.</param>
public record PlaybookDto(
    int Id,
    int FromStatusId,
    string FromStatusName,
    int ToStatusId,
    string ToStatusName,
    IReadOnlyList<string> Types,
    string Shape,
    string Prompt,
    string Model,
    string Effort,
    int? Budget,
    DateTimeOffset UpdatedAt);

/// <summary>
/// How many millions of tokens this row budgets. Null means no budget (on
/// create) or leaves it alone (on patch); <c>""</c> or whitespace clears it;
/// a parsed positive integer sets it - the same three states
/// <see cref="IssueWipLimitRequest.Limit"/> carries.
/// </summary>
public record PlaybookCreateRequest(
    int FromStatusId,
    int ToStatusId,
    IReadOnlyList<string>? Types,
    string Prompt,
    string? Model,
    string? Effort,
    string? Budget = null,
    string? Shape = null);

/// <summary>Null leaves a field alone, as everywhere else in Hatch.</summary>
public record PlaybookPatchRequest(
    int? FromStatusId,
    int? ToStatusId,
    IReadOnlyList<string>? Types,
    string? Prompt,
    string? Model,
    string? Effort,
    string? Budget = null,
    string? Shape = null);

/// <summary>
/// What one issue overrides its playbooks with. Null leaves a field alone and
/// <c>""</c> hands it back to the playbook, as everywhere else in Hatch; the
/// two fields are independent, so an issue may carry a model and no effort.
/// </summary>
/// <remarks>
/// Its own request, and its own route, because
/// <see cref="IssuesController"/> accepts the <c>hatch</c> scope and this must
/// not - see <see cref="IssuePlaybookController"/>. There is no prompt here on
/// purpose: a prompt is the <em>method</em> for a transition, and a per-issue
/// method is a paragraph, which is what a description is.
/// </remarks>
public record IssuePlaybookRequest(string? Model, string? Effort);

/// <summary>
/// How many of an epic's stories may run at once. Null leaves it alone, and
/// <c>""</c> clears it back to <see cref="DefaultLimit"/> - the same bulk rule
/// every other clearable field in Hatch follows.
/// </summary>
/// <remarks>
/// The default lives here, not on the entity, because the CLI prints it and
/// cannot see <c>EfHatchIssue</c> - <c>EfHatchIssue.DefaultEpicWipLimit</c>
/// aliases it, the way <c>EfHatchIssue.MaxClaimRunnerLength</c> aliases
/// <see cref="ClaimRequest.MaxRunnerLength"/>.
/// </remarks>
public record IssueWipLimitRequest(string? Limit)
{
    /// <summary>What a null limit reads as: one story at a time.</summary>
    public const int DefaultLimit = 1;
}

/// <summary>
/// Who an issue is to belong to. Both fields null is the unassign; exactly one
/// of them is refused with a sentence, because a kind with no id is a request
/// that meant something and did not say what.
/// </summary>
/// <remarks>
/// What you read is what you write: this is <see cref="AssigneeDto"/> minus the
/// name, so no client has to learn two shapes for one fact. The name is the
/// directory's to say and not a caller's to assert - a request naming a person
/// "Nathan" who is really somebody else would be a second source of truth about
/// a row this process owns.
/// </remarks>
public record AssigneeRequest(string? Kind, Guid? Id);

/// <summary>
/// Whether this one goes first. One required boolean, so the same route both
/// marks and unmarks and the caller says which it meant.
/// </summary>
/// <remarks>
/// Not a toggle, deliberately. A control that sent "the other one" would race
/// two browsers looking at the same card into flipping it back and forth, and
/// the answer to "is this expedited now" would depend on which request landed
/// second rather than on what anybody pressed. The client reads the state, and
/// sends the state it wants.
/// </remarks>
public record ExpediteRequest(bool Expedited);

/// <summary>
/// The level to set, by name - <c>"economy"</c>, <c>"normal"</c>,
/// <c>"expedited"</c> or <c>"emergency"</c> - see <see cref="PriorityLevels"/>.
/// One required string
/// rather than a boolean, for the same reason <see cref="ExpediteRequest"/> is
/// one required boolean: the same route both marks and unmarks, and the
/// caller says which it meant.
/// </summary>
public record PriorityRequest(string Priority);

/// <summary>
/// Whether this issue is carried past a column marked <em>Express skips</em>
/// with no session, as long as it has no unanswered question. One required
/// boolean, for the same reason as <see cref="ExpediteRequest"/>: the same
/// route both marks and unmarks, and the caller says which it meant.
/// </summary>
public record ExpressRequest(bool Express);

/// <summary>
/// The picker's rows and the answer to "who am I", in one read.
/// </summary>
/// <remarks>
/// One request rather than two because <em>Assign to me</em> needs both and a
/// browser that inferred the second from a cookie would be a second
/// implementation of who the caller is. <paramref name="Me"/> is null where
/// nobody is signed in, which is the ordinary state of local development with
/// <c>Auth:Enabled</c> false - and the press is simply absent there.
/// </remarks>
/// <param name="Assignees">
/// Every person and every live key, people first and each A→Z. A revoked key is
/// not in it, which is the same predicate that makes an issue already pointing
/// at one read as unassigned.
/// </param>
public record AssigneeDirectoryDto(AssigneeDto? Me, IReadOnlyList<AssigneeDto> Assignees);

/// <summary>
/// One edge: this issue waits on <paramref name="DependsOnKey"/>.
/// </summary>
/// <remarks>
/// Unlike a playbook or a per-issue override, writing one of these is open to a
/// key. A dependency is a statement about the work rather than about an agent's
/// budget, and a planning session that has just filed five stories is exactly
/// who should chain them.
/// </remarks>
public record IssueDependencyRequest(string DependsOnKey);

// ---- Work ----

/// <summary>
/// Everything a spawned agent needs to do one increment on one issue, decided
/// here rather than in the shell: which issue, which way it is going, what to
/// tell the agent, and how much thought to spend.
/// </summary>
/// <param name="Blocked">
/// Why no agent should be spawned, or null when one should. A sentence rather
/// than a code - it is printed at a terminal and read by a person.
/// </param>
/// <param name="ToStatus">Where the increment ends, or null when there is nowhere to go.</param>
/// <param name="Playbook">The matched row, or null when the matrix says nothing about this transition.</param>
/// <param name="Questions">
/// Every question ever asked about this issue, answered and not. Carried on the
/// dispatch rather than fetched separately because both halves are needed here
/// and for opposite reasons: an answered question is a decision the next
/// session must not re-open, and an open one is why there is no next session
/// yet - see <paramref name="Blocked"/>.
/// </param>
/// <param name="Messages">
/// The messages sent to the session working this issue that no session has read
/// yet, oldest first. Carried on the dispatch so the next session's prompt can
/// say them; the runner marks exactly these read as it spawns.
/// </param>
/// <param name="IssueUrl">
/// The absolute link to this issue's page on this Hatch, for writing into
/// places that are not Hatch - a pull request description, chiefly. Null when
/// the install has not configured a public origin, in which case the caller
/// uses the origin it reached Hatch at. Computed here when it can be because a
/// runner that reaches Hatch as <c>http://api:8080</c> cannot know the address
/// a reviewer's browser uses, and a key cannot read the shell's config to find
/// out.
/// </param>
/// <param name="Kind">
/// One of <see cref="WorkKinds"/>: whether this dispatch moves the issue on
/// (<c>advance</c>), resolves the conflict its branch has with the trunk
/// (<c>conflicts</c>), or fixes the build that fails on its branch's tip
/// (<c>build</c>). Derived and stored nowhere - the last two are exactly the
/// dispatches whose two ends are the same column, and which of them it is comes
/// from what the board holds about the branch.
/// </param>
/// <param name="Hop">
/// True where this issue is express, stands in a column marked
/// <see cref="StatusDto.ExpressSkips"/>, and has no unanswered question - so
/// the caller should carry it across itself, with
/// <c>POST /api/hatch/work/{key}/hop</c>, and spawn nothing.
/// <paramref name="Playbook"/> is always null on a hop, even where one covers
/// the move, so no client can spawn a session for it by accident.
/// </param>
/// <param name="HopKind">
/// Which of <see cref="HopKinds"/> carried this hop, or null where
/// <paramref name="Hop"/> is false - see HA-149 and HA-113.
/// </param>
/// <param name="HopUnder">
/// The key of the epic that carried this hop, when <paramref name="HopKind"/>
/// is <see cref="HopKinds.Under"/> - null for every other kind, and for every
/// client too old to read it.
/// </param>
/// <param name="LetGo">
/// How many increments in a row let this ticket go without moving it: this
/// issue's releases, newest first, whose outcome was <see cref="ClaimOutcomes.Dropped"/>,
/// counted back to the first release whose outcome was
/// <see cref="ClaimOutcomes.Worked"/>, a status change, or an answer a person
/// wrote - whichever comes first. A release with no outcome, and an answer
/// written by a lapse, are skipped rather than counted or stopped at. Zero on
/// a client too old to read it.
/// </param>
/// <param name="InReview">
/// Whether <paramref name="FromStatus"/> is, right now, the board's review
/// column - computed the same way <c>AttentionController</c> counts a ticket
/// as waiting on a pull request, off <c>Columns.AwaitingReview</c> and never
/// off a column's name. False on a client too old to read it.
/// </param>
public record WorkDto(
    IssueDto Issue,
    StatusDto FromStatus,
    StatusDto? ToStatus,
    PlaybookDto? Playbook,
    IReadOnlyList<IssueCardDto> Children,
    IReadOnlyList<WorkRepositoryDto> Repositories,
    IReadOnlyList<QuestionDto> Questions,
    string? Blocked,
    string? IssueUrl,
    string Kind = WorkKinds.Advance,
    IReadOnlyList<CommentDto>? Messages = null,
    bool Hop = false,
    string? HopKind = null,
    string? HopUnder = null,
    int LetGo = 0,
    bool InReview = false);

/// <summary>
/// What a dispatch is for. Three, and the second and third are the only
/// dispatches that do not end in a different column: both are an issue in
/// review, and an agent's work on it is judged by the branch and not by the
/// column.
/// </summary>
public static class WorkKinds
{
    /// <summary>Move the issue from its column to the next: every dispatch there has ever been.</summary>
    public const string Advance = "advance";

    /// <summary>
    /// Resolve the merge conflict between the branch of an issue in review and
    /// the trunk. It starts and ends in the review column, so it is judged by
    /// the branch and not by the column.
    /// </summary>
    public const string Conflicts = "conflicts";

    /// <summary>
    /// Fix the build that fails on the tip of the branch of an issue in review,
    /// whose branch merges cleanly with the trunk. Judged by whether the session
    /// pushed a new tip, and the build on that tip is judged later, by the board.
    /// </summary>
    public const string Build = "build";
}

/// <summary>Which of the four reasons carried a hop - see WorkDto.HopKind.</summary>
public static class HopKinds
{
    /// <summary>An express issue, in a column marked <see cref="StatusDto.ExpressSkips"/>.</summary>
    public const string Express = "express";

    /// <summary>A child a parent pulls, in a column marked ParentPulls - see HA-149.</summary>
    public const string Parent = "parent";

    /// <summary>
    /// An epic standing in a column outside the WIP section whose next column
    /// is inside it, with something filed under it - see HA-113.
    /// </summary>
    public const string Epic = "epic";

    /// <summary>
    /// A story or bug in a ticked column whose parent is an epic standing
    /// inside the WIP section - see HA-113.
    /// </summary>
    public const string Under = "under";
}

/// <summary>
/// One of the project's bound remotes, as a dispatch names it - see
/// <see cref="ProjectRepositoryDto"/> for the write side. The server has
/// already done every comparison; a client maps <paramref name="MatchedRemote"/>
/// back to a path on disk and never canonicalises anything itself.
/// </summary>
/// <param name="Primary">The first entry in the project's own order - where a session starts.</param>
/// <param name="MatchedRemote">
/// The declared remote, spelled exactly as the runner sent it, whose canonical
/// form is this entry's - or null when none of the runner's declared remotes
/// matched it (including when the caller declared nothing at all).
/// </param>
public record WorkRepositoryDto(string Remote, string Canonical, string? BaseBranch, bool Primary, string? MatchedRemote);

/// <summary>
/// One row of a pass: an issue the dispatcher looked at, and what it decided
/// about it.
/// </summary>
/// <remarks>
/// The fields <see cref="WorkDto"/> carries, minus the playbook prompt, the
/// children and the questions. Those three are the payload of a dispatch - one
/// agent, one issue - and loading them for every row would make a whole-board
/// read expensive for nothing, since a scan is read to find out what was
/// skipped and not to do the work.
/// </remarks>
/// <param name="Blocked">
/// Why the pass folded past this issue, or null where it did not. The first
/// entry with a null <c>Blocked</c> is the issue <c>work/next</c> returns for
/// the same arguments, because it is the same walk.
/// </param>
/// <param name="Kind">One of <see cref="WorkKinds"/>, as on <see cref="WorkDto"/>.</param>
/// <param name="Hop">As on <see cref="WorkDto"/>.</param>
/// <param name="HopKind">As on <see cref="WorkDto"/>.</param>
/// <param name="HopUnder">As on <see cref="WorkDto"/>.</param>
/// <param name="ClearNote">
/// Why a row that carries no <paramref name="Blocked"/> is clear at all, where
/// that is not otherwise obvious - today, only that a stall question lapsed
/// (<c>"its stall question lapsed after 5 minutes untouched"</c>). Null on
/// every row that is clear for the ordinary reason, which is most of them.
/// </param>
public record QueueEntryDto(
    IssueDto Issue,
    StatusDto FromStatus,
    StatusDto? ToStatus,
    string? Blocked,
    string Kind = WorkKinds.Advance,
    bool Hop = false,
    string? HopKind = null,
    string? HopUnder = null,
    string? ClearNote = null);

/// <summary>
/// One issue in the review column that a runner holds a checkout for, and what
/// the board holds about its branch - what the runner's poll is read from.
/// </summary>
/// <remarks>
/// A fact about a branch and not work, so it is not narrowed by a claim, a
/// question, a date or an assignee: an issue somebody else is working still has
/// a branch, and whether it conflicts is still worth knowing.
/// </remarks>
/// <param name="Key">The issue's key.</param>
/// <param name="Repositories">
/// The project's bound repositories, each with <see cref="WorkRepositoryDto.MatchedRemote"/>
/// set to the runner's own spelling where the runner has a checkout of it.
/// Empty for a project that binds nothing, which is checked from the runner's
/// standing checkout.
/// </param>
/// <param name="MergeChecks">Every verdict the board holds for the issue, one per repository.</param>
/// <param name="BuildChecks">Every build verdict the board holds for the issue, one per repository. Absent from a board that predates them.</param>
/// <param name="PullRequestUrl">The pull request recorded on the issue, or null where none is. Absent from a board that predates it.</param>
public record ReviewCheckDto(
    string Key,
    IReadOnlyList<WorkRepositoryDto> Repositories,
    IReadOnlyList<MergeCheckDto> MergeChecks,
    IReadOnlyList<BuildCheckDto>? BuildChecks = null,
    string? PullRequestUrl = null);

// ---- Rollups ----

/// <summary>
/// One column's share of a subtree: how many of its leaves are sitting there.
/// </summary>
/// <remarks>
/// A status no leaf is in is absent rather than present with a zero - the
/// client already holds the column list and does not need a row that draws
/// nothing.
/// </remarks>
public record RollupSliceDto(int StatusId, int Count);

/// <summary>
/// What a subtree adds up to: the arithmetic every meter is drawn from, done
/// once on the server so the Plan page, the issue page and anything holding an
/// API key all read the same number. See <see cref="Rollup"/> for what a leaf
/// is and why it is the unit.
/// </summary>
/// <param name="Leaves">
/// The total the slices sum to - the number of issues with no children beneath
/// this one, or one when this issue is a leaf itself.
/// </param>
/// <param name="Done">Leaves sitting in a terminal column. The numerator of "how far along is this".</param>
/// <param name="Waiting">
/// Open questions on this issue and every descendant, at any depth. What says
/// an epic is blocked on a person rather than on an agent, counted through
/// <see cref="Questions.WaitingCountsAsync"/> so there is still one definition of
/// "open".
/// </param>
/// <param name="Slices">In board order (<c>sortOrder</c>, then id), empty columns absent.</param>
public record RollupDto(int Leaves, int Done, int Waiting, IReadOnlyList<RollupSliceDto> Slices);

/// <summary>One direct child of the issue asked about, with its own rollup.</summary>
/// <param name="IsLeaf">
/// Whether it has children of its own - the flag that tells a client to draw a
/// status pill rather than a bar. A field rather than something inferred from
/// <see cref="RollupDto.Leaves"/> being one, because a story with a single task
/// and a task with none must not look alike on the wire.
/// </param>
public record ChildRollupDto(IssueCardDto Issue, bool IsLeaf, RollupDto Rollup);

/// <summary>
/// One subtree and the row under it: what the issue page draws beneath an epic
/// or a story. The children are the direct ones, in rank order, each carrying
/// the rollup of everything beneath <em>it</em> - so a stack of meters agrees
/// with the one above it by construction.
/// </summary>
public record IssueRollupDto(string Key, RollupDto Rollup, IReadOnlyList<ChildRollupDto> Children);

/// <summary>
/// One epic on the Plan page: the card, what everything beneath it adds up to,
/// and the epics beneath it drawn the same way.
/// </summary>
/// <param name="Rollup">
/// The whole subtree, not only the epics in <paramref name="Children"/> - every
/// story, task and bug under it at any depth. An epic's meter would otherwise
/// read as empty until somebody filed an epic inside it.
/// </param>
/// <param name="Children">
/// The epics below this one, each appearing exactly here and not again at the
/// top level, so the page draws the tree once. Ordered by key.
/// </param>
public record PlanEntryDto(IssueCardDto Issue, bool IsLeaf, RollupDto Rollup, IReadOnlyList<PlanEntryDto> Children);

/// <summary>
/// The landscape in one request: every epic in the tracker with what it adds up
/// to, so the Plan page is a list of meters rather than a question per bar.
/// </summary>
/// <param name="Epics">The epics with no parent, each carrying the epics beneath it. Ordered by key.</param>
/// <param name="Loose">
/// The work that hangs under no epic at all - the leaves below every root issue
/// that is not an epic, and <c>leaves: 0</c> when there is none. It is here so
/// that the Plan page cannot quietly become a view that hides half the tracker:
/// an operator who files a story without a parent should be able to see that
/// they did.
/// </param>
public record PlanDto(IReadOnlyList<PlanEntryDto> Epics, RollupDto Loose);

// ---- The work log ----

/// <summary>
/// What one model cost inside one session - the row's breakdown, one entry per
/// model the session used.
/// </summary>
/// <remarks>
/// Hatch's names, not Anthropic's. The CLI's <c>result</c> event spells these
/// <c>inputTokens</c>, <c>cacheCreationInputTokens</c>, <c>costUSD</c> and so
/// on, and the translation happens once, in the runner's stream renderer
/// (<c>src/Hatch.Cli/StreamRender.cs</c>), the same file that is the only one
/// that knows the battery's vendor spelling. Nothing downstream of the wire
/// should have to know two vocabularies.
/// </remarks>
public record WorkLogModelUseDto(
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    decimal CostUsd);

/// <summary>
/// One finished session, as the dispatcher reports it.
/// </summary>
/// <remarks>
/// Two fields on the stored row are deliberately absent here, and both for the
/// same reason: they are derived, and a caller that could send them is a caller
/// that could make the row disagree with itself.
///
/// The four token counts are the sum over <paramref name="Models"/>, computed by
/// the endpoint. And <c>Described</c> is whether a title or a summary actually
/// arrived - a run that died before it could say what it did still gets its row,
/// with every metric intact and the mark on the wire rather than left for a
/// client to infer from an empty string.
/// </remarks>
/// <param name="SessionId">What <c>claude --resume</c> takes.</param>
/// <param name="StartedAt">Wall clock at the spawn.</param>
/// <param name="EndedAt">Wall clock as the stream closed.</param>
/// <param name="DurationMs">The session's own <c>duration_ms</c>, which is the smaller number and the honest one.</param>
/// <param name="Title">What the session did, in a few words, or null when it never said.</param>
/// <param name="Summary">The same at length. Clipped rather than refused when it runs long - see <see cref="IssueWorkLogController"/>.</param>
/// <param name="Models">
/// The per-model breakdown. Absent on a run that fell over before the accounting
/// arrived, which records zero tokens and whatever cost was reported: a session
/// that ended badly still had an id and still cost something.
/// </param>
public record WorkLogEntryRequest(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long DurationMs,
    string? Title,
    string? Summary,
    bool IsError,
    int Turns,
    decimal CostUsd,
    IReadOnlyList<WorkLogModelUseDto>? Models,
    int? Requests = null,
    long? PeakContextTokens = null,
    int? PromptChars = null);

/// <summary>
/// One row of the work log, as the issue page draws it.
/// </summary>
/// <param name="Described">
/// Whether the session said what it did. On the wire rather than inferred from
/// an empty title, because "this run never described itself" is a fact about the
/// run and a client deducing it from an absent string is a client guessing.
/// </param>
/// <param name="TotalTokens">
/// The four counts added up, carried rather than left to the client, so the
/// headline figure has one definition.
/// </param>
public record WorkLogEntryDto(
    long Id,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long DurationMs,
    string? Title,
    string? Summary,
    bool Described,
    bool IsError,
    int Turns,
    decimal CostUsd,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long TotalTokens,
    IReadOnlyList<WorkLogModelUseDto> Models,
    int? Requests,
    long? PeakContextTokens,
    int? PromptChars);

/// <summary>
/// What a set of sessions cost, added up. See <see cref="WorkLogRollup"/> for
/// why this addition is not the leaf rule <see cref="Rollup"/> uses.
/// </summary>
/// <param name="Sessions">How many rows are in the sum.</param>
/// <param name="Errors">How many of them ended badly. Their spend is in the totals either way.</param>
/// <param name="TotalTokens">The four counts added up - one definition of the headline, as on an entry.</param>
public record WorkLogTotalsDto(
    int Sessions,
    int Errors,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long TotalTokens,
    decimal CostUsd);

/// <summary>
/// An issue's work log: what everything beneath it has cost, what it has cost
/// on its own, and its own sessions.
/// </summary>
/// <remarks>
/// The asymmetry is the point and is worth saying out loud: <b>entries are the
/// issue's own, totals are the subtree's.</b> An epic showing four sessions of
/// its own and 1.4M tokens is not a bug, it is the feature - and
/// <paramref name="Own"/> is here so a page can say which number is which
/// instead of letting one pass for the other.
/// </remarks>
/// <param name="Totals">This issue and every descendant, at any depth.</param>
/// <param name="Own">Only the entries on this issue.</param>
/// <param name="Entries">This issue's own sessions, newest first.</param>
public record WorkLogDto(
    string Key,
    WorkLogTotalsDto Totals,
    WorkLogTotalsDto Own,
    IReadOnlyList<WorkLogEntryDto> Entries);

/// <summary>
/// One equal slice of the work log's time axis, and what ended inside it.
/// </summary>
/// <remarks>
/// Half-open: a session belongs to the bucket with
/// <c>Start &lt;= EndedAt &lt; End</c>, so it is counted once and the buckets add
/// up to the range's own total.
///
/// A bucket with no sessions in it carries a zeroed
/// <see cref="WorkLogTotalsDto"/> rather than being left out, and that is the
/// rule worth stating: an hour in which nothing ran is an hour that cost
/// nothing, which is a measurement. A poll's history has gaps because a missing
/// reading means nobody looked; a work log has none.
/// </remarks>
public record WorkLogBucketDto(DateTimeOffset Start, DateTimeOffset End, WorkLogTotalsDto Totals);

/// <summary>
/// What the work log recorded over a range of time, in equal buckets - the
/// shape a graph is drawn from.
/// </summary>
/// <remarks>
/// <paramref name="From"/> and <paramref name="To"/> are the requested range
/// snapped outward onto whole buckets, so the range, the bucket list and
/// <paramref name="Totals"/> all describe one window and the totals are the sum
/// of the buckets by construction.
/// </remarks>
/// <param name="Bucket">The size actually used, <c>hour</c> or <c>day</c> - which may be one the server chose rather than one the caller asked for.</param>
/// <param name="Totals">The whole range, so a caller does not have to add the buckets up.</param>
/// <param name="FirstSessionAt">
/// The earliest session in the filtered log, <em>ignoring the range</em>, or
/// null when nothing has ever run under this filter. It is what lets a page
/// choose a sensible range, and tell "nothing has run yet" apart from "nothing
/// ran in the range you asked for".
/// </param>
/// <param name="LastSessionAt">The latest, read the same way.</param>
/// <param name="Buckets">Oldest first, one per bucket in the range, empty ones included.</param>
public record WorkLogHistoryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    string Bucket,
    WorkLogTotalsDto Totals,
    DateTimeOffset? FirstSessionAt,
    DateTimeOffset? LastSessionAt,
    IReadOnlyList<WorkLogBucketDto> Buckets);

/// <summary>
/// One agent session as the leaderboard reads it: what it was run against, what
/// it said about itself, and what it cost.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="WorkLogEntryDto"/> in two places, and
/// both absences are the point. <c>Summary</c> is up to 2000 characters and this
/// answers a hundred rows - the issue page is where a session is read at length.
/// <c>Models</c> is a list per row and nothing on the leaderboard draws a
/// per-model breakdown.
/// </remarks>
/// <param name="Title">What the session said it did, or null when it never said - see <paramref name="Described"/>.</param>
/// <param name="TotalTokens">The four counts added up - one definition of the headline, as everywhere else in the module.</param>
public record WorkLogSessionDto(
    long Id,
    string SessionId,
    string IssueKey,
    string IssueTitle,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long DurationMs,
    string? Title,
    bool Described,
    bool IsError,
    int Turns,
    decimal CostUsd,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long TotalTokens,
    int? Requests,
    long? PeakContextTokens,
    int? PromptChars);

/// <summary>
/// The sessions in a range, ranked - and what that whole range cost, whether or
/// not every row of it came back.
/// </summary>
/// <remarks>
/// <paramref name="Totals"/> covers <b>the whole filter and not the returned
/// page</b>, which is what lets a capped table add up honestly; a caller tells
/// the two apart by comparing <c>Totals.Sessions</c> with the length of
/// <paramref name="Sessions"/> and says so on screen.
///
/// <paramref name="From"/> and <paramref name="To"/> are the range as given.
/// Nothing is snapped here, deliberately unlike <see cref="WorkLogHistoryDto"/>:
/// there is no bucket grid on this read to align to.
/// </remarks>
/// <param name="Sort">The sort as resolved - <c>tokens</c>, <c>cost</c> or <c>ended</c>.</param>
/// <param name="FirstSessionAt">
/// The earliest session in the population <c>ancestorKey</c> names,
/// <em>ignoring the range</em>, or null when that population is empty. Read
/// exactly as <see cref="WorkLogHistoryDto.FirstSessionAt"/> is, and here so a
/// page can tell "nothing has ever been logged" from "nothing ran in the range
/// you asked for" without fetching the graph's data to say it.
/// </param>
/// <param name="LastSessionAt">The latest, read the same way.</param>
public record WorkLogSessionsDto(
    DateTimeOffset From,
    DateTimeOffset To,
    string Sort,
    WorkLogTotalsDto Totals,
    DateTimeOffset? FirstSessionAt,
    DateTimeOffset? LastSessionAt,
    IReadOnlyList<WorkLogSessionDto> Sessions);

// ---- Runners ----

/// <summary>
/// What a runner is: a loop that will ask again, or a single increment that
/// will not.
/// </summary>
/// <remarks>
/// Spelled on the contract rather than on either side of the wire, because both
/// sides act on it: the runner says which it is, and the board decides from it
/// whether to draw a control surface at all.
/// </remarks>
public static class RunnerKinds
{
    /// <summary><c>go-to-work</c>: it heartbeats every pass and obeys what comes back.</summary>
    public const string Loop = "loop";

    /// <summary><c>work</c>, and <c>go-to-work --once</c>: one heartbeat, and no second pass to apply an instruction to.</summary>
    public const string Once = "once";
}

/// <summary>
/// What a runner found when it merged an issue's branch against the trunk of
/// one repository - the four things it can say. Spelled on the contract because
/// both sides act on them: the runner writes one, and the board and the
/// dispatcher read it.
/// </summary>
public static class MergeVerdicts
{
    /// <summary>The branch merges with the trunk without conflict. Nothing for an agent to do.</summary>
    public const string Clean = "clean";

    /// <summary>The branch conflicts with the trunk, in the files the verdict names.</summary>
    public const string Conflicted = "conflicted";

    /// <summary>No unmerged branch on origin is named for the issue.</summary>
    public const string None = "none";

    /// <summary>Two or more unmerged branches on origin are named for it, and nothing here guesses which is its own.</summary>
    public const string Ambiguous = "ambiguous";

    public static readonly IReadOnlyList<string> All = [Clean, Conflicted, None, Ambiguous];
}

/// <summary>
/// A verdict, as the runner reports it. Who took it is the credential's to say,
/// and when is the board's, so the body names neither.
/// </summary>
/// <param name="Remote">The repository as the runner spells it. The board keys the verdict on its canonical form.</param>
/// <param name="Trunk">The trunk's name, and <paramref name="TrunkSha"/> where it stood when the merge was tried.</param>
/// <param name="Verdict">One of <see cref="MergeVerdicts"/>.</param>
/// <param name="Branch">The issue's branch, and <paramref name="BranchSha"/> where it stood. Absent for <c>none</c> and <c>ambiguous</c>, which carry neither.</param>
/// <param name="Files">The conflicted paths. Required for <c>conflicted</c> and ignored otherwise.</param>
/// <param name="Runner">The checkout that took it - <c>host:/path/to/checkout</c>, as <see cref="ClaimRequest.Runner"/> is.</param>
/// <param name="HoldsTrunk">
/// True when the branch already had the trunk's tip - <c>--is-ancestor</c>, not a
/// merge that would work. False when it merges cleanly but does not yet hold it,
/// or when it conflicts. Null for <c>none</c> and <c>ambiguous</c>, which name no
/// branch.
/// </param>
public record MergeCheckRequest(
    string Remote,
    string Trunk,
    string TrunkSha,
    string Verdict,
    string? Branch,
    string? BranchSha,
    IReadOnlyList<string>? Files,
    string Runner,
    bool? HoldsTrunk = null);

/// <summary>
/// A runner reporting that the pull request recorded on an issue has merged.
/// </summary>
/// <param name="Url">The pull request the runner read as merged, compared against the one recorded on the issue.</param>
/// <param name="Runner">The checkout that read it - <c>host:/path/to/checkout</c>, as <see cref="ClaimRequest.Runner"/> is. Not validated or persisted; it rides the body for shape parity with the requests that do use it.</param>
public record PullRequestMergedRequest(string Url, string Runner);

/// <summary>One stored verdict: the issue's branch against one repository's trunk.</summary>
/// <param name="Remote">The remote as the runner spelled it.</param>
/// <param name="Canonical">The remote's canonical form - the verdict's identity within its issue.</param>
/// <param name="HoldsTrunk">See <see cref="MergeCheckRequest.HoldsTrunk"/>.</param>
/// <param name="CheckedAt">When the board took it.</param>
/// <param name="CheckedBy">The name of the credential it arrived under.</param>
public record MergeCheckDto(
    string Remote,
    string Canonical,
    string Trunk,
    string TrunkSha,
    string Verdict,
    string? Branch,
    string? BranchSha,
    IReadOnlyList<string> Files,
    bool? HoldsTrunk,
    DateTimeOffset CheckedAt,
    string Runner,
    string CheckedBy);

/// <summary>
/// What a build on one sha of an issue's branch came to - the four things a
/// runner can say. Spelled on the contract because both sides act on them: the
/// runner writes one, and the board and the dispatcher read it.
/// </summary>
/// <remarks>
/// A verdict is about one sha, the branch's tip on origin. A build that has
/// concluded on a sha does not change, short of a re-run, and a verdict about
/// any other sha says nothing about the branch as it stands.
/// </remarks>
public static class BuildVerdicts
{
    /// <summary>Every check on the sha has concluded and none of them failed. Nothing for an agent to do.</summary>
    public const string Passed = "passed";

    /// <summary>
    /// Every check on the sha has concluded and at least one failed. A check run
    /// fails on <c>failure</c>, <c>timed_out</c> or <c>startup_failure</c>; a
    /// commit status on <c>failure</c> or <c>error</c>. Cancelled, skipped,
    /// neutral, stale and action-required are not failures.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>Something on the sha is still queued or running. A runner waits for every check to conclude, so one increment sees every failure at once.</summary>
    public const string Pending = "pending";

    /// <summary>No check ran on the sha.</summary>
    public const string None = "none";

    public static readonly IReadOnlyList<string> All = [Passed, Failed, Pending, None];
}

/// <summary>
/// What a forge says a pull request's own state is - the only place that tells
/// a merged one from one closed without merging. Neither git nor the absence
/// of a branch can: a squash merge puts none of the branch's commits in the
/// trunk, and a branch only disappears where the repository is set to delete
/// it on merge.
/// </summary>
public static class PullRequestStates
{
    /// <summary>The pull request was merged. The forge reports this and never <see cref="Closed"/> for one that was.</summary>
    public const string Merged = "merged";

    /// <summary>The pull request is still open.</summary>
    public const string Open = "open";

    /// <summary>The pull request was closed without merging.</summary>
    public const string Closed = "closed";

    /// <summary>The forge named a state this does not know. Not the same as a read that failed.</summary>
    public const string Unknown = "unknown";

    public static readonly IReadOnlyList<string> All = [Merged, Open, Closed, Unknown];
}

/// <summary>The three states of a review row's build icon - see <see cref="ReviewDto.BuildState"/>.</summary>
public static class ReviewBuildStates
{
    /// <summary>Every counted repository with a clean merge check has a build verdict on that check's own sha, and all of them passed.</summary>
    public const string Success = "success";

    /// <summary>Any counted repository's build on its clean merge check's sha failed, or is still running with a check that has already failed.</summary>
    public const string Failure = "failure";

    /// <summary>Anything else: no verdict, a build still running with nothing failed yet, no checks ran, a verdict about an older sha, or no clean merge check at all.</summary>
    public const string Unknown = "unknown";

    public static readonly IReadOnlyList<string> All = [Success, Failure, Unknown];
}

/// <summary>One check that failed: its name, and a link to it where the forge gave one.</summary>
public record FailingCheckDto(string Name, string? Url = null);

/// <summary>
/// A build verdict, as the runner reports it. Who took it is the credential's to
/// say, and when is the board's, so the body names neither.
/// </summary>
/// <param name="Remote">The repository as the runner spells it. The board keys the verdict on its canonical form.</param>
/// <param name="Branch">The issue's branch, and <paramref name="Sha"/> its tip on origin - what the verdict is about.</param>
/// <param name="Verdict">One of <see cref="BuildVerdicts"/>.</param>
/// <param name="Failing">The checks that failed. Required for <c>failed</c> and ignored otherwise.</param>
/// <param name="Runner">The checkout that took it - <c>host:/path/to/checkout</c>, as <see cref="ClaimRequest.Runner"/> is.</param>
/// <param name="PushedByIncrement">
/// True when the runner is saying that a build increment pushed
/// <paramref name="Sha"/>. A poll that only reads the build leaves it false, and
/// the board never lowers it for the same sha: it is what decides whether a
/// build that fails again is another increment's work or a question.
/// </param>
public record BuildCheckRequest(
    string Remote,
    string Branch,
    string Sha,
    string Verdict,
    IReadOnlyList<FailingCheckDto>? Failing,
    string Runner,
    bool PushedByIncrement = false);

/// <summary>One stored build verdict: the build on the tip of the issue's branch in one repository.</summary>
/// <param name="Remote">The remote as the runner spelled it.</param>
/// <param name="Canonical">The remote's canonical form - the verdict's identity within its issue.</param>
/// <param name="ShaSince">When the board first heard about <paramref name="Sha"/>. What ten minutes of asking again about <c>none</c> is counted from.</param>
/// <param name="PushedByIncrement">Whether a build increment pushed <paramref name="Sha"/>.</param>
/// <param name="CheckedAt">When the board took it.</param>
/// <param name="CheckedBy">The name of the credential it arrived under.</param>
public record BuildCheckDto(
    string Remote,
    string Canonical,
    string Branch,
    string Sha,
    DateTimeOffset ShaSince,
    string Verdict,
    IReadOnlyList<FailingCheckDto> Failing,
    bool PushedByIncrement,
    DateTimeOffset CheckedAt,
    string Runner,
    string CheckedBy);

/// <summary>
/// A trunk's build verdict, as the runner reports it - the same fact as
/// <see cref="BuildCheckRequest"/>, but about a repository's trunk rather than
/// an issue's branch, because a trunk build is nobody's issue. Who took it is
/// the credential's to say, and when is the board's, so the body names neither.
/// </summary>
/// <param name="Remote">The repository as the runner spells it. The board keys the verdict on its canonical form.</param>
/// <param name="Trunk">The trunk's name, as the runner's own workspace reported it.</param>
/// <param name="Verdict">One of <see cref="BuildVerdicts"/>.</param>
/// <param name="Failing">The checks that failed. Required for <c>failed</c> and ignored otherwise.</param>
/// <param name="Runner">The checkout that took it - <c>host:/path/to/checkout</c>, as <see cref="ClaimRequest.Runner"/> is.</param>
public record TrunkBuildRequest(
    string Remote,
    string Trunk,
    string Sha,
    string Verdict,
    IReadOnlyList<FailingCheckDto>? Failing,
    string Runner);

/// <summary>One stored trunk verdict: the build on the tip of one repository's trunk.</summary>
/// <param name="Remote">The remote as the runner spelled it.</param>
/// <param name="Canonical">The remote's canonical form - the verdict's identity across every project, not only one issue's.</param>
/// <param name="ShaSince">When the board first heard about <paramref name="Sha"/>. What ten minutes of asking again about <c>none</c> is counted from.</param>
/// <param name="CheckedAt">When the board took it.</param>
/// <param name="CheckedBy">The name of the credential it arrived under.</param>
/// <param name="BugIssueKey">The bug filed while this trunk was failing, or null when none is attached yet - see HA-95.</param>
public record TrunkBuildDto(
    long Id,
    string Remote,
    string Canonical,
    string Trunk,
    string Sha,
    DateTimeOffset ShaSince,
    string Verdict,
    IReadOnlyList<FailingCheckDto> Failing,
    DateTimeOffset CheckedAt,
    string Runner,
    string CheckedBy,
    string? BugIssueKey);

/// <summary>
/// The stall guard's two answers, in its words. A question the board opens for a
/// build that failed again on the agent's own fix offers the same two, so they
/// live here and neither side spells them.
/// </summary>
public static class StallAnswers
{
    public const string LeaveIt = "leave it";
    public const string TryAgain = "try again";

    /// <summary>The two options, in the order they are offered. Neither is recommended: nothing here knows why it happened.</summary>
    public static IReadOnlyList<QuestionOptionDto> Options() =>
    [
        new(LeaveIt,
            "It waits for you. Nothing is dispatched at it while this question is open, so answer once you have looked - or once you have moved it somewhere the loop does not reach."),
        new(TryAgain,
            "Spend another increment on the same ticket. The next session is handed this stall, and your answer, among the decisions already made."),
    ];

    /// <summary>
    /// Whether a question offered exactly this pair - the labels alone, in
    /// either order, and nothing else. A question with a different label, a
    /// third option, or asked in prose is never a stall question, however much
    /// its body reads like one: the label is what a runner acts on, not the
    /// prose around it.
    /// </summary>
    public static bool IsStall(IReadOnlyList<QuestionOptionDto>? options) =>
        options is { Count: 2 } &&
        options.Select(o => o.Label).OrderBy(l => l, StringComparer.Ordinal)
            .SequenceEqual(Options().Select(o => o.Label).OrderBy(l => l, StringComparer.Ordinal));
}

/// <summary>
/// What the board would like a runner to do: carry on, hold, or finish and
/// stop.
/// </summary>
/// <remarks>
/// Three and no more, and none of them is "kill it". Nothing on the server
/// reaches into a process; every one of these is picked up by the loop itself,
/// between increments, which is what makes them work for a runner behind a
/// router nothing can reach.
/// </remarks>
public static class RunnerStates
{
    /// <summary>Take the next ticket, as ever.</summary>
    public const string Running = "running";

    /// <summary>Keep saying you are here; take nothing.</summary>
    public const string Paused = "paused";

    /// <summary>Finish whatever is in flight and exit without picking another.</summary>
    public const string Stopping = "stopping";

    /// <summary>The three, in the order a control surface should offer them.</summary>
    public static readonly string[] All = [Running, Paused, Stopping];
}

/// <summary>
/// One runner as the board draws it: who it is, what it is doing, when it was
/// last heard from, and what it has been asked to do next.
/// </summary>
/// <remarks>
/// Nothing here is a second copy of the claim. <paramref name="ClaimKey"/> and
/// the line beside it are read live off whichever issue this runner holds at
/// the moment the request is served, so an operator who clears a claim sees the
/// runner go idle on the next poll rather than seeing a column that remembers
/// the ticket.
/// </remarks>
/// <param name="Name">What the runner calls itself - <c>host:/path/to/checkout</c>, the same string its claims carry.</param>
/// <param name="Kind">
/// <c>loop</c> for <c>go-to-work</c>, which asks for instructions between
/// increments, or <c>once</c> for a single increment that will never read one.
/// A control surface is worth drawing only for the first.
/// </param>
/// <param name="ClaimKey">The issue it holds right now, or null between increments.</param>
/// <param name="Line">
/// The last thing it said: the claim's chatter while it holds one, and its own
/// last line when it does not. One field because it is one question - what is
/// this runner doing - and which row answered it is not the reader's problem.
/// </param>
/// <param name="GoneAfterSeconds">
/// How long a runner may go unheard from before it is gone, the way a claim
/// carries its own TTL: the server honours it, so the server says what it is,
/// and a client can draw "quiet" without deciding for itself what quiet means.
/// </param>
/// <param name="Repositories">
/// The checkouts this runner is serving, canonical and in the order the last
/// heartbeat named them - a fact about the running process, so it is written
/// on every beat rather than seeded once.
/// </param>
/// <param name="Clones">Whether this runner makes a clone for itself when it lacks one.</param>
/// <param name="Mine">Whether this runner was started with `do-my-work` or `--mine` - working its owner's tickets only.</param>
/// <param name="Where">
/// The machine and checkout this runner runs from, <c>host:/path</c> - a fact
/// about the process, drawn under its name rather than as the name, now that
/// <see cref="Name"/> is a character and not a path.
/// </param>
/// <param name="ExhaustedUntil">
/// This runner's own Claude account ran out of usage, and this is when it
/// expects to reset - null on an ordinary runner. A fact this runner reports
/// about itself, never a person's to set.
/// </param>
public record RunnerDto(
    string Name,
    string Kind,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string? ClaimKey,
    string? Line,
    DateTimeOffset? LineAt,
    string State,
    string? Under,
    int? MaxRuns,
    decimal? MaxSpend,
    DateTimeOffset? UntilAt,
    int GoneAfterSeconds,
    string[] Repositories,
    bool? Clones,
    bool? Mine,
    string? Where,
    DateTimeOffset? ExhaustedUntil = null);

/// <summary>
/// Still here, and what should I do next - the one call a runner makes about
/// itself.
/// </summary>
/// <remarks>
/// The four bounds are <em>reported</em>, not requested: they are what this
/// process started with, and they are written only when the row is being
/// created. A restart of the same loop sends them again and the server ignores
/// them, which is what stops a restart from undoing an operator's edit.
/// </remarks>
/// <param name="Kind">
/// <see cref="RunnerDto.Kind"/>, and the runner is what knows it: a loop asks
/// again, a single increment does not.
/// </param>
/// <param name="Line">
/// What it is doing right now, on the terms
/// <see cref="ClaimHeartbeatRequest.Chatter"/> has: absent leaves the last one
/// alone, <c>""</c> clears it, and anything longer than
/// <see cref="ClaimRequest.MaxChatterLength"/> is truncated rather than
/// refused.
/// </param>
/// <param name="Where">
/// The machine and checkout this runner runs from, <c>host:/path</c> - a fact
/// about the process, written on every beat like <see cref="Remotes"/>. Absent
/// from an older client, which is never refused on that account alone.
/// </param>
/// <param name="Exhausted">
/// This runner's own account ran out of Claude usage - or, sent false, it is
/// not. Absent (an older CLI, or <c>hatch work</c>'s single beat, which knows
/// nothing about the account it ran under) leaves whatever the row already
/// says alone. A <c>DateTimeOffset?</c> alone cannot tell "leave alone" from
/// "clear", so this carries the tri-state and <see cref="ExhaustedUntil"/>
/// carries the value.
/// </param>
/// <param name="ExhaustedUntil">When the account resets, read only when <see cref="Exhausted"/> is true.</param>
/// <param name="Usage">
/// The account's usage windows, as this runner's own session last read them -
/// Hatch's own vocabulary, the same fields <c>UtilizationLimit</c> carries
/// minus the tone, which is the server's to decide. Absent or empty leaves
/// whatever the row already holds alone: a reading never becomes wrong, only
/// old, so a runner that has not yet seen a <c>rate_limit_event</c> - one that
/// has just restarted mid-night - sends nothing rather than blanking it.
/// </param>
/// <param name="UsageReadAt">
/// When this runner read <see cref="Usage"/>, its own word for it - stored as
/// reported, so an idle runner re-sending an hour-old reading on every poll
/// does not make it look new.
/// </param>
public record RunnerHeartbeatRequest(
    string? Kind = null,
    string? Line = null,
    string? Under = null,
    int? MaxRuns = null,
    decimal? MaxSpend = null,
    DateTimeOffset? UntilAt = null,
    IReadOnlyList<string>? Remotes = null,
    bool? Clones = null,
    bool? Mine = null,
    string? Where = null,
    bool? Exhausted = null,
    DateTimeOffset? ExhaustedUntil = null,
    IReadOnlyList<RunnerUsageWindowDto>? Usage = null,
    DateTimeOffset? UsageReadAt = null);

/// <summary>
/// One usage window off a runner's own session stream, or off its own CLI's
/// login between sessions - Hatch's own vocabulary, the same four fields
/// <c>UtilizationLimit</c> carries minus the tone, which the server decides
/// from the percentage alone.
/// </summary>
/// <param name="Window">
/// <c>session</c>, <c>weekly</c>, <c>weeklyModel</c> or <c>extra</c> - a
/// window Hatch has never seen is still kept as sent.
/// </param>
/// <param name="Label">What the runner's own console calls this window.</param>
/// <param name="Percent">0-100, clamped on the way in.</param>
/// <param name="ResetsAt">When this window resets, if the account said so.</param>
public record RunnerUsageWindowDto(
    string Window,
    string Label,
    int Percent,
    DateTimeOffset? ResetsAt = null);

/// <summary>
/// What the board would like this runner to do, answered to its own heartbeat
/// and read by nobody else.
/// </summary>
/// <remarks>
/// The loop folds all five into its bounds before its next pick, so a person
/// editing a row changes what that loop does from the next ticket onward -
/// never in the middle of one. That is not a check anywhere: the heartbeat
/// happens at the top of a pass, which is the one moment no claim is held.
/// </remarks>
/// <param name="For">
/// The person this runner works for, resolved the same way a <c>--mine</c>
/// dispatch pass is: the calling key's owner, or the local person where the
/// wall is off. A key with no owner falls back to the key's own name, the same
/// fallback the claim's own "for &lt;name&gt;" already makes. Absent from a
/// Hatch too old to answer with it, which the console reads the same way as
/// a key belonging to nobody.
/// </param>
public record RunnerInstructionDto(
    string State,
    string? Under,
    int? MaxRuns,
    decimal? MaxSpend,
    DateTimeOffset? UntilAt,
    string? For = null);

/// <summary>
/// The operator's half: keep going, pause, stop after this one - and the bounds
/// that were flags on the command line.
/// </summary>
/// <remarks>
/// Every field is a string on the wire, and absent / <c>""</c> / a value are
/// leave alone / clear / set, which is the convention the two dates on
/// <see cref="IssuePatchRequest"/> already established. The numbers wear the
/// same clothes rather than inventing a second tri-state for themselves - a
/// nullable number can say "leave it" or "set it" but not "take the cap off".
/// </remarks>
/// <param name="State">One of <c>running</c>, <c>paused</c>, <c>stopping</c>. Not clearable - a runner is always in one of the three.</param>
/// <param name="UntilAt">An instant (<c>2026-09-12T17:00:00Z</c>), read by <c>IssueMoment</c> the way a ready date is.</param>
public record RunnerPatchRequest(
    string? State = null,
    string? Under = null,
    string? MaxRuns = null,
    string? MaxSpend = null,
    string? UntilAt = null);

/// <summary>
/// The Claude token this Hatch holds, wrapped for the wire.
/// </summary>
/// <remarks>
/// <para>The one place in Hatch where a live secret is handed back out of the
/// API on purpose, and it exists for one caller: the container runner's
/// entrypoint, which has to authenticate a <c>claude</c> CLI it starts itself
/// and has nowhere else to read the token from. Every other secret-valued
/// setting is redacted on read, and the route that answers this one refuses
/// outright wherever the wall is up - see docs/hatch.md, "API surface".</para>
///
/// <para>Wrapped rather than plain because it crosses a network, over the
/// scheme the store already uses (<c>SecretProtector</c>). That is a courtesy
/// and not a lock: the algorithm is a fixed XOR key both sides carry, which is
/// exactly what lets a container holding nothing but <c>git</c> and this
/// binary reverse it without a second round trip. A token that has to be safe
/// in transit needs TLS, which is the operator's to put in front of Hatch.</para>
/// </remarks>
/// <param name="ProtectedToken">The token as <c>SecretProtector.Protect</c> wrote it: <c>v1:&lt;base64&gt;</c>.</param>
public record ClaudeTokenDto(string ProtectedToken);

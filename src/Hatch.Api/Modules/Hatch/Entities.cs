using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A project, which is really just a key namespace: it exists so that issues
/// can be called <c>AER-12</c> instead of <c>#4471</c>, and so that two efforts
/// can number themselves independently.
///
/// It is deliberately not a container. The board (docs/hatch.md, "Goals")
/// shows every issue from every project at once, because the operator has one
/// pair of hands and switching boards to find out what is next is the thing
/// markdown files already do badly.
/// </summary>
[Table("Projects")]
[Index(nameof(Key), IsUnique = true)]
public class EfHatchProject
{
    /// <summary>
    /// <c>AER</c>, <c>OPS</c>. Upper case, starts with a letter, two to six
    /// characters - short because it is typed into a chat window and read in a
    /// card corner.
    /// </summary>
    public const string KeyPattern = "^[A-Z][A-Z0-9]{1,5}$";

    public const int MaxKeyLength = 6;
    public const int MaxNameLength = 120;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// The prefix of every issue key in this project. Changeable, behind a
    /// speed bump, and the cost of that is worth stating: every <c>AER-12</c>
    /// written into a commit message, a chat log, or a branch name goes dead,
    /// because those are references this database has never seen and cannot
    /// rewrite.
    /// </summary>
    /// <remarks>
    /// What does <em>not</em> break is the part that matters: parentage is a
    /// foreign key on <see cref="EfHatchIssue.ParentId"/>, and issue numbers are
    /// their own column, so a rekeyed project keeps every story under its epic
    /// and every task under its story - <c>AER-12</c> becomes <c>OPS-12</c>,
    /// same issue, same tree. Only text that spelled the old key out loud is
    /// left pointing at nothing.
    /// </remarks>
    [MaxLength(MaxKeyLength)]
    public required string Key { get; set; }

    [MaxLength(MaxNameLength)]
    public required string Name { get; set; }

    /// <summary>
    /// The number the next issue in this project will take. Kept as a column
    /// rather than derived from <c>MAX(Number) + 1</c> because a deleted issue
    /// must not hand its number to the next one: <c>AER-12</c> in an old chat
    /// log should be a dead link, never a different ticket.
    /// </summary>
    /// <remarks>
    /// <see cref="ConcurrencyCheckAttribute"/> is what makes the mint safe
    /// without a lock or a sequence. Two concurrent creates both read 12, and
    /// the second <c>SaveChanges</c> finds the row no longer holding what it
    /// read and throws <c>DbUpdateConcurrencyException</c> - which the create
    /// path catches and retries (docs/hatch.md, "Issue numbering"). The
    /// unique index on <c>(ProjectId, Number)</c> is the backstop underneath,
    /// so the worst case is a refused request rather than two issues wearing
    /// the same key.
    /// </remarks>
    [ConcurrencyCheck]
    public int NextIssueNumber { get; set; } = 1;

    public required DateTimeOffset CreatedAt { get; set; }

    public ICollection<EfHatchIssue> Issues { get; set; } = [];

    /// <summary>The remotes bound to this project, in <see cref="EfHatchProjectRepository.SortOrder"/>.</summary>
    public ICollection<EfHatchProjectRepository> Repositories { get; set; } = [];

    public static bool IsValidKey(string? key) =>
        key is not null && Regex.IsMatch(key, KeyPattern, RegexOptions.None, TimeSpan.FromSeconds(1));
}

/// <summary>
/// One git remote bound to a project, at its position in the ordered list -
/// the first is the primary. Readable by every client; writable only by a
/// person, because a runner that could bind one could point every runner on
/// the board at a repository nobody chose.
///
/// Two projects may bind the same remote: a monorepo with two key namespaces
/// is a real shape, and nothing here says a repository belongs to one project.
/// </summary>
[Table("ProjectRepositories")]
[Index(nameof(ProjectId), nameof(Canonical), IsUnique = true)]
public class EfHatchProjectRepository
{
    public const int MaxRemoteLength = 500;
    public const int MaxBaseBranchLength = 120;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public EfHatchProject? Project { get; set; }

    /// <summary>The remote as typed. Never canonicalised on the way in by anything but this server - see <see cref="RemoteIdentity"/>.</summary>
    [MaxLength(MaxRemoteLength)]
    public required string Remote { get; set; }

    /// <summary>
    /// <see cref="RemoteIdentity.Canonical"/> of <see cref="Remote"/> - the
    /// matching identity, and the unique index's other half.
    /// </summary>
    [MaxLength(MaxRemoteLength)]
    public required string Canonical { get; set; }

    /// <summary>The branch a checkout of this remote starts from, or null for whatever the remote calls its default.</summary>
    [MaxLength(MaxBaseBranchLength)]
    public string? BaseBranch { get; set; }

    /// <summary>
    /// Position in the list; the first is the primary. A plain 0-based index
    /// is enough - the whole list is replaced together on every write, so
    /// there is no gap ever to insert into.
    /// </summary>
    public int SortOrder { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One column on the board. Global rather than per-project - the board shows
/// every project at once, so a per-project status set would have no column to
/// put a foreign issue in.
///
/// Rows rather than an enum because the operator reorders and renames them from
/// the Statuses page, and an enum would make "add a review column" a deploy.
/// </summary>
[Table("Statuses")]
[Index(nameof(Name), IsUnique = true)]
public class EfHatchStatus
{
    public const int MaxNameLength = 60;
    public const int MaxColorLength = 7;

    /// <summary>What a column with nothing said about it wears: the neutral grey of a fact, not of a warning.</summary>
    public const string DefaultColor = "#6b7280";

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(MaxNameLength)]
    public required string Name { get; set; }

    /// <summary>
    /// Left-to-right column order. Sparse on purpose (the seed is 10/20/30/40)
    /// so inserting a column between two is a single write rather than a
    /// renumber - the same trick <see cref="EfHatchIssue.Rank"/> plays a size
    /// up.
    /// </summary>
    public required int SortOrder { get; set; }

    /// <summary>
    /// Whether landing here means the work shipped. Nothing in the MVP reads
    /// this for behaviour except the importer (a checked box lands terminal);
    /// it is carried because "how much did we finish in March" is a query
    /// against the event log plus this flag, and a flag added later would be
    /// blank for every row that already exists.
    /// </summary>
    public bool IsTerminal { get; set; }

    /// <summary>
    /// Whether landing here means the work is parked: shelved, not shipped, and
    /// not coming back on its own.
    ///
    /// <para>A deferred column is not drawn on the board and has no drop target,
    /// so the only way into one is the issue page's status bar. That is
    /// deliberate: parking a ticket is a decision somebody makes about a
    /// particular ticket, not a lane work drifts into, and a column nobody can
    /// drag to is a column nothing lands in by accident.</para>
    ///
    /// <para>Separate from <see cref="IsTerminal"/> rather than a flavour of it
    /// because the two answer different questions. Terminal is "this shipped",
    /// which is what a dependency waits for and what a quarter is counted by.
    /// Deferred is "stop counting this", which takes the work out of the
    /// denominator without ever claiming it was done - see
    /// <see cref="Rollup"/>.</para>
    ///
    /// <para>Both flags on one column is not refused. It would mean a column
    /// that ships work and hides it from the board at once, which is a strange
    /// thing to want and an easy thing to undo; the reads that care take
    /// deferred first, so such a column behaves as a parked one.</para>
    /// </summary>
    public bool IsDeferred { get; set; }

    /// <summary>
    /// Whether this column is in the WIP section: the lane the operator wants
    /// bounded. Read only where the column is neither <see cref="IsDeferred"/>
    /// nor <see cref="IsTerminal"/> - see <see cref="Wip"/>, which is the one
    /// place that says which columns count - and written only by
    /// <see cref="WipController"/>, never through <see cref="StatusPatchRequest"/>
    /// or <see cref="StatusCreateRequest"/>: the key-writable status route must
    /// not be able to reach a flag the WIP limit is measured against.
    /// </summary>
    public bool IsWip { get; set; }

    /// <summary>
    /// Whether an express issue standing here is carried on to the next column
    /// with no session, as long as it has no unanswered question - see
    /// <see cref="EfHatchIssue.Express"/>.
    ///
    /// <para>Not a "whose column is this" flag: that stays derived from the
    /// playbook matrix, for the reasons <c>docs/hatch.md</c> (<em>Status</em>)
    /// argues. This box only says which columns an express issue is carried
    /// past; who works the columns it lands in is unaffected.</para>
    ///
    /// <para>Writing it is closed to an API key
    /// (<see cref="StatusesController.PutExpressSkips"/>) for the same reason
    /// as <see cref="EfHatchIssue.Express"/>: it decides which gates the loop
    /// may pass unattended, and that is a playbook's kind of power.</para>
    /// </summary>
    public bool ExpressSkips { get; set; }

    /// <summary>
    /// Whether a child standing here is carried on to the next column with no
    /// session while its parent stands in the implementation column - see
    /// <see cref="EfHatchIssue"/>'s parent/child relationship.
    ///
    /// <para>Writing it is closed to an API key
    /// (<see cref="StatusesController.PutParentPulls"/>) for the same reason as
    /// <see cref="ExpressSkips"/>: it decides which gates the loop may pass
    /// unattended, and that is a playbook's kind of power.</para>
    /// </summary>
    public bool ParentPulls { get; set; }

    /// <summary>
    /// The column's colour, as <c>#rrggbb</c>. A row rather than a lookup in
    /// the frontend for the same reason the name is a row: the operator invents
    /// columns, and a palette keyed on the four names shipped here would leave
    /// "review" grey forever and would lose a column's colour the moment it was
    /// renamed.
    /// </summary>
    /// <remarks>
    /// Stored as a hex string rather than as a token name because the value has
    /// to survive a theme the operator has not chosen yet, and because a status
    /// picker that offers eight token names is a picker that says no to the
    /// ninth colour somebody wants.
    /// </remarks>
    [MaxLength(MaxColorLength)]
    public string Color { get; set; } = DefaultColor;

    private static readonly Regex ColorShape =
        new("^#[0-9a-f]{6}$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Whether a string is a colour this will store. Six digits and a hash,
    /// deliberately narrow: three-digit shorthands, <c>rgb()</c> and named
    /// colours would all have to be normalised somewhere before a stylesheet or
    /// a contrast calculation could read them, and one shape stored is one
    /// shape to reason about.
    /// </summary>
    public static bool IsValidColor(string? color) =>
        color is not null && ColorShape.IsMatch(color);

    /// <summary>The stored form of a colour a client sent: lower case, so two spellings of one colour compare equal.</summary>
    public static string NormalizeColor(string color) => color.Trim().ToLowerInvariant();
}

/// <summary>
/// A unit of work - an epic, a story, a task, or a bug. The board's card, the
/// detail page's subject, and the thing Claude is handed a link to.
/// </summary>
/// <remarks>
/// The display key (<c>AER-12</c>) is deliberately not a column. It is
/// <c>Project.Key + "-" + Number</c>, computed at the edge, so there is exactly
/// one fact about a key stored anywhere and no chance of the two disagreeing
/// after a project rename that should never have been allowed anyway.
/// </remarks>
[Table("Issues")]
// The numbering backstop: whatever the retry loop in the create path does, the
// database will not hold two AER-12s.
[Index(nameof(ProjectId), nameof(Number), IsUnique = true)]
// The board's one query - every issue, ordered by column then rank.
[Index(nameof(StatusId), nameof(Rank))]
public class EfHatchIssue
{
    public const int MaxTitleLength = 300;
    public const int MaxDescriptionLength = 200_000;
    public const int MaxTypeLength = 16;

    /// <summary>
    /// Room for a pull request URL. Generous against the forge URLs anybody
    /// actually pastes - a long branch name on a long repository path is a
    /// couple of hundred characters - and short enough that the column is not
    /// somewhere a description ends up by mistake.
    /// </summary>
    public const int MaxPullRequestUrlLength = 500;

    /// <summary>
    /// Room for <c>host:/path/to/checkout</c>, which is how a runner names
    /// itself. The same 240 a person's name takes, because it is the same kind
    /// of thing: a label somebody reads in a refusal.
    /// </summary>
    public const int MaxClaimRunnerLength = ClaimRequest.MaxRunnerLength;

    /// <summary>
    /// Room for a line of chatter. A sentence, not a log - what is stored is
    /// what a card draws under "working on it", and anything longer is
    /// truncated to this.
    /// </summary>
    public const int MaxClaimChatterLength = ClaimRequest.MaxChatterLength;

    /// <summary>The four types, in the order a picker should offer them.</summary>
    public static readonly string[] Types = ["epic", "story", "task", "bug"];

    /// <summary>
    /// Which parents each type may take. Advisory shape rather than a
    /// hierarchy: every issue may also have no parent at all, which is the
    /// ordinary state of a freshly captured bug. A task may also hang directly
    /// under an epic: the model permits more than the planning habit (an epic
    /// takes stories, a story takes tasks) uses.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> LegalParentTypes =
        new Dictionary<string, string[]>
        {
            ["epic"] = ["epic"],
            ["story"] = ["epic"],
            ["task"] = ["story", "bug", "epic"],
            ["bug"] = ["epic", "story"],
        };

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public required int ProjectId { get; set; }
    public EfHatchProject? Project { get; set; }

    /// <summary>The <c>12</c> in <c>AER-12</c>, serial within the project and never reused.</summary>
    public required int Number { get; set; }

    [MaxLength(MaxTypeLength)]
    public required string Type { get; set; }

    [MaxLength(MaxTitleLength)]
    public required string Title { get; set; }

    /// <summary>
    /// Markdown, stored exactly as typed. Rendered by the client
    /// (<c>marked</c> + <c>dompurify</c>, the pair the docs app already
    /// bundles), never by the server - so what the database holds is what
    /// somebody wrote, and a change of renderer is a frontend change.
    /// </summary>
    public string Description { get; set; } = "";

    public required int StatusId { get; set; }
    public EfHatchStatus? Status { get; set; }

    /// <summary>
    /// The epic above this story, or the story above this task. Nullable and
    /// usually null; validated server-side for existence, same project, legal
    /// type pairing, and no cycles.
    /// </summary>
    public long? ParentId { get; set; }
    public EfHatchIssue? Parent { get; set; }
    public ICollection<EfHatchIssue> Children { get; set; } = [];

    /// <summary>
    /// Position within the column. Sparse integers with 1024-sized gaps, so a
    /// drop between two cards is the midpoint and costs one UPDATE; when the
    /// gap closes the whole column is renumbered (see <c>RankService</c>).
    /// Lexorank strings were considered and rejected - string midpoint maths
    /// has sharp edges, and a column here holds tens of cards, not millions.
    /// </summary>
    public required long Rank { get; set; }

    /// <summary>
    /// The day this becomes workable, and nothing about it before then. An
    /// issue whose <c>ReadyAt</c> is in the future is folded off the board
    /// (<c>BoardPage</c>), which is what lets a ticket be filed the moment it is
    /// thought of rather than the moment it can be started: buy a certificate in
    /// September and the renewal appears next August on its own.
    /// </summary>
    /// <remarks>
    /// A gate, not a schedule. Nothing refuses to move a card that is not ready
    /// yet, and nothing checks this against <see cref="DueAt"/> - an issue ready
    /// after it is due is a mistake worth seeing on screen, not one worth a 400.
    /// </remarks>
    public DateTimeOffset? ReadyAt { get; set; }

    /// <summary>Whether <see cref="ReadyAt"/>'s time of day was meant - see <see cref="IssueMoment"/>.</summary>
    public bool ReadyAtHasTime { get; set; }

    /// <summary>
    /// When it is owed. Drawn on the card as a chip that warms as the date
    /// approaches, and the field the eventual automatic prioritisation will
    /// sort on.
    /// </summary>
    /// <remarks>
    /// A date in the past is accepted without comment. Half of what a tracker
    /// is for is recording that something was due last Tuesday, and a form that
    /// argues about it is a form people stop telling the truth to.
    /// </remarks>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Whether <see cref="DueAt"/>'s time of day was meant - see <see cref="IssueMoment"/>.</summary>
    public bool DueAtHasTime { get; set; }

    /// <summary>
    /// Where the work is being reviewed: an absolute http(s) URL, or null until
    /// something opens one. Held here so that "show me the pull request" is a
    /// link on the issue rather than a search through the comments for a URL
    /// somebody remembered to paste.
    /// </summary>
    /// <remarks>
    /// One URL rather than a list, deliberately. The field answers "where is
    /// this being reviewed <em>now</em>", and the question "what has it been"
    /// is already answered by the event log, which keeps every value this
    /// column has ever held. A list would be a second history beside that one,
    /// and worse than it.
    ///
    /// Not a foreign key to anything and not parsed for a host: Hatch has no
    /// opinion about whose forge an operator uses, and a column that only
    /// accepted one would be a fact about exactly one installation
    /// (docs/ethos.md).
    /// </remarks>
    [MaxLength(MaxPullRequestUrlLength)]
    public string? PullRequestUrl { get; set; }

    /// <summary>
    /// The model every agent increment dispatched for this issue runs on,
    /// whichever playbook speaks for the move - or null, which is every issue
    /// on a stock board and means "whatever the playbook says".
    /// </summary>
    /// <remarks>
    /// A playbook prices a transition, and that is the right unit almost
    /// always. What it cannot say is that <em>this</em> story is the hard one:
    /// the knob for that used to be a flag on one command at a terminal, which
    /// is no use at three in the morning when the loop is the only thing
    /// typing. So the fact lives on the ticket somebody looked at.
    ///
    /// It reaches this issue and nothing beneath it. An epic set to
    /// <c>opus</c> does not spend <c>opus</c> on its stories - a task that
    /// needs the big model says so itself, and the alternative is an
    /// expensive decision made once at the top of a tree and inherited by
    /// work nobody weighed.
    ///
    /// Sized by the playbook's own constants because it holds the playbook's
    /// own values, and validated by
    /// <see cref="EfHatchPlaybook.IsValidModel"/> - one definition of a legal
    /// model, so what an issue may be set to and what a playbook may be set
    /// to cannot drift apart. Writing it is closed to an API key
    /// (<see cref="IssuePlaybookController"/>) for the same reason writing a
    /// playbook is.
    /// </remarks>
    [MaxLength(EfHatchPlaybook.MaxModelLength)]
    public string? ModelOverride { get; set; }

    /// <summary>
    /// The thinking budget those increments run at, or null for the
    /// playbook's - independent of <see cref="ModelOverride"/> in both
    /// directions, because "this one is subtle" and "this one is large" are
    /// different complaints about a ticket.
    /// </summary>
    [MaxLength(EfHatchPlaybook.MaxEffortLength)]
    public string? EffortOverride { get; set; }

    /// <summary>
    /// <em>This one first.</em> Set by a person, honoured by both halves of
    /// Hatch: the board floats the card to the top of its column, and the
    /// dispatcher considers every issue at a higher level before anything at a
    /// lower one - see <see cref="Hatch.Contracts.PriorityLevels"/> for the
    /// three levels and their order.
    /// </summary>
    /// <remarks>
    /// <para>A sort key, not a gate. Every fold still applies exactly as it
    /// applies to any other issue - an open question, an unmet dependency, a
    /// ready date in the future, a live claim, a missing playbook, a person's
    /// name on the ticket and a terminal column all fold an expedited or
    /// emergency issue the same (<see cref="WorkController"/>). This changes
    /// the order candidates are <em>considered</em> in, and nothing else.</para>
    ///
    /// <para>It marks the issue it is set on and nothing beneath it that
    /// already exists. Every type in a walkable column is dispatchable, so a
    /// level on one issue means something wherever it is set - and "point
    /// tonight at this epic" is already <c>work --under</c>, which is the
    /// subtree mechanism. A second one beside it would be two answers to one
    /// question.</para>
    ///
    /// <para>Taken from the parent at filing <em>only when the parent is
    /// Emergency</em>, and at no other time: a child filed under an emergency
    /// parent is born emergency, the same as <see cref="Express"/> is taken
    /// from an express parent. Expedited never inherits this way, and
    /// reparenting an issue under an emergency parent does not mark it, nor
    /// does reparenting one away unmark it - the level an issue is born with is
    /// a fact about how it came to exist, not a fact that follows its parent
    /// around.</para>
    ///
    /// <para>Writing it is closed to an API key
    /// (<see cref="IssueExpediteController"/>) for the reason writing an
    /// assignee and a playbook is: priority decides what the loop reaches for
    /// first, so a key that could set one could put its own ticket at the front
    /// of every night. Reading is open, like everything else a dispatch needs -
    /// an agent is entitled to know why it was sent where it was sent.</para>
    /// </remarks>
    public int Priority { get; set; }

    /// <summary>
    /// A gate-passer, not a sort key - the opposite shape from
    /// <see cref="Priority"/>. An issue marked express is carried past a
    /// column marked <see cref="EfHatchStatus.ExpressSkips"/> with no session,
    /// as long as it has no unanswered question; every other fold still holds
    /// it exactly as it holds any other issue (<see cref="WorkController"/>).
    /// It changes no order, on the board or in the queue.
    /// </summary>
    /// <remarks>
    /// <para>Set by a person
    /// (<see cref="IssueExpressController"/>), for the same reason as
    /// <see cref="Priority"/>: it decides which gates the loop may pass
    /// unattended, and a key that could set it could carry its own ticket
    /// through the night unattended.</para>
    ///
    /// <para>Taken from the parent at filing, and at no other time: an issue
    /// created under an express parent is born express, whoever files it and
    /// however. Reparenting an issue under an express parent does not mark it,
    /// and reparenting one away does not unmark it - the flag is a fact about
    /// how an issue came to exist, not a fact that follows its parent
    /// around.</para>
    /// </remarks>
    public bool Express { get; set; }

    // ---- The claim ----
    //
    // A lease on this issue held by a running dispatcher: taken before an
    // increment, refreshed while it runs, released after it, and expiring on
    // its own when the runner dies. Seven columns on the issue row rather than
    // a table of their own, because a claim is a fact about the issue with at
    // most one of it at a time - and because taking one is then a single
    // conditional UPDATE against a row the dispatcher is already reading.
    //
    // Expiry is lazy and there is no sweeper. A claim is dead when
    // <see cref="ClaimHeartbeatAt"/> is older than the TTL, which is a
    // predicate every reader evaluates (see IssueClaims.IsLive); nothing has to
    // run for a dead runner's ticket to become claimable again. The columns are
    // left as they are until somebody takes the lease over, so the trail of who
    // last held it survives the lease itself.
    //
    // There is deliberately no index on ClaimHeartbeatAt, and nobody should add
    // one on a hunch. The predicate is only ever evaluated against rows already
    // selected by the board's own (StatusId, Rank) index or by primary key, and
    // this table holds hundreds of rows.

    /// <summary>
    /// The fencing token, or null where nothing holds this issue. A heartbeat
    /// or a release presenting a token that is not this one is refused, which
    /// is what keeps a runner whose lease expired mid-increment from clearing
    /// the lease that replaced it.
    /// </summary>
    /// <remarks>
    /// Never leaves the server on a board read - see <see cref="IssueClaimDto"/>.
    /// It is a capability, not a fact about the issue, and a card carrying it
    /// would let anyone holding a board read steal or refresh a lease.
    /// </remarks>
    public Guid? ClaimToken { get; set; }

    /// <summary>
    /// Who holds it, as a name rather than a foreign key - the same column
    /// shape <see cref="CreatedBy"/> and <see cref="EfHatchIssueEvent.Actor"/>
    /// take, and for the same reason: the trail has to keep reading after the
    /// person or the key it named is gone.
    /// </summary>
    [MaxLength(Common.PersonName.MaxChars)]
    public string? ClaimedBy { get; set; }

    /// <summary>
    /// Which checkout is holding it - <c>host:/path/to/checkout</c>, as the
    /// runner names itself. A claim answers "who" and "from where" separately
    /// because two checkouts on one box under one key are the case the whole
    /// feature exists for.
    /// </summary>
    [MaxLength(MaxClaimRunnerLength)]
    public string? ClaimRunner { get; set; }

    /// <summary>When the lease was taken. Not moved by a heartbeat - "how long has this been running" is a question about this column.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>When the holder was last heard from. The lease is over when this is older than the TTL.</summary>
    public DateTimeOffset? ClaimHeartbeatAt { get; set; }

    /// <summary>
    /// A line the holder is carrying: what it is doing right now, replaced
    /// whole by each heartbeat that names one. Cosmetic, and truncated rather
    /// than refused - killing a live lease because a terminal printed something
    /// wide would be the wrong trade.
    /// </summary>
    [MaxLength(MaxClaimChatterLength)]
    public string? ClaimChatter { get; set; }

    /// <summary>When that line arrived, so a stale one reads as stale.</summary>
    public DateTimeOffset? ClaimChatterAt { get; set; }

    /// <summary>
    /// The person this issue belongs to, or null. At most one of this and
    /// <see cref="AssigneeApiKeyId"/> is ever set - the controller clears the
    /// other on every write, and a check constraint refuses a row wearing both.
    /// </summary>
    /// <remarks>
    /// <para>A bare <see cref="Guid"/> and not a foreign key, for the reason
    /// Quill's <c>QuillNote.PersonId</c> is one and
    /// <see cref="CreatedBy"/> is a name: Hatch owns its own schema and its own
    /// migration history, and a constraint from a module into
    /// <c>public.People</c> is the compile-time coupling Modules/README.md
    /// exists to prevent.</para>
    ///
    /// <para>What <c>ON DELETE SET NULL</c> would have bought is bought instead
    /// by a predicate every reader applies: an id that does not resolve to a
    /// live identity reads as unassigned, in the DTO, on the card, in the filter
    /// and at the dispatcher, at the same instant and with nothing to sweep. See
    /// <see cref="Services.Auth.IActorDirectory"/>, which owns that rule.</para>
    ///
    /// <para>Deliberately not a claim. A claim is a machine lease that comes and
    /// goes with an increment; this is a durable statement about who owns the
    /// ticket, written by a person and surviving every restart - which is why
    /// writing it is closed to an API key
    /// (<see cref="AssigneeController"/>).</para>
    /// </remarks>
    public Guid? AssigneePersonId { get; set; }

    /// <summary>
    /// The API key this issue belongs to, or null - the same column for the
    /// other kind of actor, and mutually exclusive with
    /// <see cref="AssigneePersonId"/>.
    ///
    /// A revoked key still has a row (<see cref="Ef.EfApiKey.RevokedAt"/>), so this
    /// pointing at one is not an error and is not cleaned up: it reads as
    /// unassigned, and the <c>assignee_changed</c> event still names who it was.
    /// </summary>
    public Guid? AssigneeApiKeyId { get; set; }

    /// <summary>
    /// Who filed it, as a name rather than a foreign key. The audit trail wants
    /// to read the same after a person row is deleted, and Phase 6 puts API key
    /// names in this column beside human ones - neither of which a
    /// <c>People</c> FK from a module schema could express (Modules/README.md).
    /// </summary>
    [MaxLength(Common.PersonName.MaxChars)]
    public required string CreatedBy { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }

    public ICollection<EfHatchComment> Comments { get; set; } = [];
    public ICollection<EfHatchIssueEvent> Events { get; set; } = [];
    public ICollection<EfHatchMergeCheck> MergeChecks { get; set; } = [];
    public ICollection<EfHatchBuildCheck> BuildChecks { get; set; } = [];
    public ICollection<EfHatchWorkLogEntry> WorkLog { get; set; } = [];

    public static bool IsValidType(string? type) => type is not null && Types.Contains(type);
}

/// <summary>
/// A comment on an issue. Markdown, like the description, and rendered the same
/// way.
///
/// Most comments are notes - a commit sha, a summary for a reviewer, a change
/// of mind. Two of them are not, and those two carry a <see cref="Kind"/>: a
/// <see cref="Question"/> is an agent saying it cannot proceed without a
/// decision only the operator can make, and an <see cref="Answer"/> is that
/// decision, bound to the question it settles by <see cref="AnswersId"/>.
/// </summary>
/// <remarks>
/// A question is a row rather than a heading in a comment body for the same
/// reason a ready date is a column rather than a line saying "not until March":
/// something has to be able to act on it. An unanswered question is why a
/// ticket cannot move, so <see cref="WorkController"/> reads these to refuse a
/// run and the board reads them to say which cards are waiting on a person.
/// Prose in a thread can be searched; it cannot be counted.
/// </remarks>
[Table("Comments")]
[Index(nameof(IssueId), nameof(CreatedAt))]
[Index(nameof(AnswersId))]
public class EfHatchComment
{
    public const int MaxBodyLength = 100_000;
    public const int MaxKindLength = 16;

    /// <summary>An ordinary comment. The empty string rather than null, so the column never has two ways to say "nothing special".</summary>
    public const string Note = "";

    /// <summary>A decision being asked for. Open until some comment answers it.</summary>
    public const string Question = "question";

    /// <summary>A decision being given, pointing at the question it settles.</summary>
    public const string Answer = "answer";

    /// <summary>
    /// Something said to whichever session is working the issue. Not every
    /// comment is one: the server cannot tell an operator's comment from the
    /// session's own, so delivering all of them would feed a session its own
    /// notes. A message is the one kind that is written to be read now, by the
    /// agent, and is the only kind that carries <see cref="DeliveredAt"/>.
    /// </summary>
    public const string Message = "message";

    public static bool IsValidKind(string kind) => kind is Note or Question or Answer or Message;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// Not <c>required</c>, unlike most of what a row needs: a comment added
    /// through <see cref="EfHatchIssue.Comments"/> has this filled in by EF's
    /// fixup, which is what lets an issue and its first rows be written in one
    /// <c>SaveChanges</c>.
    /// </summary>
    public long IssueId { get; set; }
    public EfHatchIssue? Issue { get; set; }

    [MaxLength(Common.PersonName.MaxChars)]
    public required string Author { get; set; }

    public required string Body { get; set; }

    /// <summary>
    /// <see cref="Note"/>, <see cref="Question"/> or <see cref="Answer"/>.
    /// Defaulted rather than required: every comment written before this column
    /// existed is a note, and so is every comment written by a client that does
    /// not know about the other two.
    /// </summary>
    [MaxLength(MaxKindLength)]
    public string Kind { get; set; } = Note;

    /// <summary>
    /// The question this answers, on the same issue. Null on everything else.
    /// </summary>
    /// <remarks>
    /// The link points from the answer to the question and not the other way
    /// round, which is what lets a question be answered twice without an edit -
    /// somebody refining a decision writes a second answer, and the first stays
    /// where it was said. "Open" is therefore a question with no answers
    /// pointing at it, computed and never stored, so the two can never disagree.
    /// </remarks>
    public long? AnswersId { get; set; }
    public EfHatchComment? Answers { get; set; }

    /// <summary>The answers to this question, if it is one.</summary>
    public ICollection<EfHatchComment> AnsweredBy { get; set; } = [];

    /// <summary>
    /// The answers a question offers, as <c>jsonb</c>: a small ordered list of
    /// <see cref="QuestionOptionDto"/>. Null on a question asked in prose, and
    /// on everything that is not a question.
    /// </summary>
    /// <remarks>
    /// A column rather than a convention in the body, for the reason the kind
    /// is a column: something has to be able to act on it. An agent asking
    /// "leaf-weighted or child-weighted?" is not writing an essay, it is
    /// offering a choice - and a choice the reader can press is a decision made
    /// in one gesture instead of a paragraph parsed by eye. Prose in a body can
    /// be read; it cannot be clicked.
    ///
    /// <c>jsonb</c> and not a table of its own, on the same grounds as
    /// <see cref="EfHatchIssueEvent.Payload"/>: this is a closed list read whole
    /// with the row that owns it and never queried across, so a table would buy
    /// a join and a cascade in exchange for nothing.
    ///
    /// Which option was taken is deliberately not stored. An answer's body is
    /// the option's label, which is the sentence a person reads six months
    /// later and the sentence the next agent's prompt carries - and a second
    /// column saying the same thing in numbers is a second thing that can come
    /// to disagree with the first.
    /// </remarks>
    public string? Options { get; set; }

    /// <summary>
    /// When a <see cref="Message"/> was put in front of a session, or null while
    /// it has not been. Written once, by a conditional <c>UPDATE</c> whose
    /// <c>WHERE</c> repeats "and still null" - see
    /// <see cref="IssueThreadController"/> - so two checks that race deliver it
    /// once between them. Null on everything that is not a message.
    /// </summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>
    /// The runner that was holding the issue when it was delivered, or the
    /// caller's name where nothing held it. Null while undelivered.
    /// </summary>
    [MaxLength(ClaimRequest.MaxRunnerLength)]
    public string? DeliveredTo { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// What a runner found when it merged an issue's branch against the trunk of
/// one repository, and what it was looking at when it did.
///
/// One row per issue per repository, keyed on <see cref="Canonical"/>: a
/// project may bind several repositories and a branch is entered in every
/// checkout that has one, so a single verdict on the issue would let a clean
/// repository overwrite a conflicted one. An issue conflicts if any of its rows
/// does. A second write for the same pair replaces the first.
/// </summary>
/// <remarks>
/// The shas are what make a verdict comparable: the branch is unchanged, and so
/// is the verdict, for as long as both refs still name what they named here.
/// <see cref="Verdict"/> <c>none</c> and <c>ambiguous</c> carry no branch and no
/// sha - there is no one branch to name - and that is deliberate: the poll
/// keeps what it saw for those two in memory rather than asking the board for a
/// fingerprint of something that is not there.
/// </remarks>
[Table("MergeChecks")]
[Index(nameof(IssueId), nameof(Canonical), IsUnique = true)]
public class EfHatchMergeCheck
{
    public const int MaxRemoteLength = EfHatchProjectRepository.MaxRemoteLength;
    public const int MaxRefLength = 200;

    /// <summary>Wide enough for a SHA-256 object name; a SHA-1 one is 40.</summary>
    public const int MaxShaLength = 64;
    public const int MaxVerdictLength = 16;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long IssueId { get; set; }
    public EfHatchIssue? Issue { get; set; }

    /// <summary>The remote as the runner spelled it.</summary>
    [MaxLength(MaxRemoteLength)]
    public required string Remote { get; set; }

    /// <summary><see cref="RemoteIdentity.Canonical"/> of <see cref="Remote"/> - the identity, and the unique index's other half.</summary>
    [MaxLength(MaxRemoteLength)]
    public required string Canonical { get; set; }

    /// <summary>The trunk the branch was merged against, and where it stood.</summary>
    [MaxLength(MaxRefLength)]
    public required string Trunk { get; set; }

    [MaxLength(MaxShaLength)]
    public required string TrunkSha { get; set; }

    /// <summary>The issue's branch, and where it stood. Null for <c>none</c> and <c>ambiguous</c>.</summary>
    [MaxLength(MaxRefLength)]
    public string? Branch { get; set; }

    [MaxLength(MaxShaLength)]
    public string? BranchSha { get; set; }

    /// <summary>One of <see cref="MergeVerdicts"/>.</summary>
    [MaxLength(MaxVerdictLength)]
    public required string Verdict { get; set; }

    /// <summary>The conflicted paths, newline-joined the way <see cref="EfHatchRunner.Remotes"/> is. Null when there are none.</summary>
    public string? Files { get; set; }

    /// <summary>See <see cref="MergeCheckRequest.HoldsTrunk"/>. Null for <c>none</c> and <c>ambiguous</c>, and on a verdict stored before this field existed.</summary>
    public bool? HoldsTrunk { get; set; }

    /// <summary>When the board took it. The board's clock, not the runner's, so verdicts from two machines are ordered by one.</summary>
    public required DateTimeOffset CheckedAt { get; set; }

    /// <summary>The checkout that took it, as it names itself.</summary>
    [MaxLength(ClaimRequest.MaxRunnerLength)]
    public required string Runner { get; set; }

    /// <summary>The name of the credential it arrived under - a name and not an id, for the reason <see cref="EfHatchIssue.CreatedBy"/> is.</summary>
    [MaxLength(Common.PersonName.MaxChars)]
    public required string CheckedBy { get; set; }

    public static string? JoinFiles(IReadOnlyList<string> files) => files.Count == 0 ? null : string.Join('\n', files);

    public static IReadOnlyList<string> SplitFiles(string? files) =>
        files?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];
}

/// <summary>
/// What the build on the tip of an issue's branch came to in one repository,
/// and which sha it is about.
///
/// One row per issue per repository, keyed on <see cref="Canonical"/> for the
/// reason <see cref="EfHatchMergeCheck"/> is: a project may bind several
/// repositories, and a single verdict would let a passing one overwrite a
/// failing one. An issue's build failed if any of its rows says so. A second
/// write for the same pair replaces the first.
/// </summary>
/// <remarks>
/// <para>The sha is what makes a verdict comparable. A build that has concluded
/// on a sha does not change, short of a re-run, so a verdict about any other
/// sha says nothing about the branch as it stands now, and the dispatcher
/// treats it as not read yet.</para>
///
/// <para><see cref="ShaSince"/> is when the board first heard about the sha,
/// and the runner counts ten minutes of asking again about <c>none</c> from
/// it: a push's checks take a few seconds to appear, so a <c>none</c> straight
/// after a push is usually premature. <see cref="PushedByIncrement"/> is what
/// tells a build that fails on an agent's own fix - which is a question - from
/// one that fails on somebody else's push, which is new work.</para>
/// </remarks>
[Table("BuildChecks")]
[Index(nameof(IssueId), nameof(Canonical), IsUnique = true)]
public class EfHatchBuildCheck
{
    public const int MaxRemoteLength = EfHatchMergeCheck.MaxRemoteLength;
    public const int MaxRefLength = EfHatchMergeCheck.MaxRefLength;
    public const int MaxShaLength = EfHatchMergeCheck.MaxShaLength;
    public const int MaxVerdictLength = EfHatchMergeCheck.MaxVerdictLength;

    /// <summary>The most failing checks one verdict carries. Past this the list is not one anybody reads, and the log is where the rest is.</summary>
    public const int MaxFailing = 100;
    public const int MaxCheckNameLength = 200;
    public const int MaxCheckUrlLength = 2000;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long IssueId { get; set; }
    public EfHatchIssue? Issue { get; set; }

    /// <summary>The remote as the runner spelled it.</summary>
    [MaxLength(MaxRemoteLength)]
    public required string Remote { get; set; }

    /// <summary><see cref="RemoteIdentity.Canonical"/> of <see cref="Remote"/> - the identity, and the unique index's other half.</summary>
    [MaxLength(MaxRemoteLength)]
    public required string Canonical { get; set; }

    /// <summary>The issue's branch, and the sha of its tip that the verdict is about.</summary>
    [MaxLength(MaxRefLength)]
    public required string Branch { get; set; }

    [MaxLength(MaxShaLength)]
    public required string Sha { get; set; }

    /// <summary>When the board first heard about <see cref="Sha"/>. Kept across writes for the same sha; the board's clock, as <see cref="CheckedAt"/> is.</summary>
    public required DateTimeOffset ShaSince { get; set; }

    /// <summary>One of <see cref="BuildVerdicts"/>.</summary>
    [MaxLength(MaxVerdictLength)]
    public required string Verdict { get; set; }

    /// <summary>The failing checks as <c>[{ name, url }]</c>, sorted by name. jsonb, as <see cref="EfHatchComment.Options"/> is.</summary>
    public string? Failing { get; set; }

    /// <summary>Whether a build increment pushed <see cref="Sha"/>. Once set it stays set for the same sha.</summary>
    public bool PushedByIncrement { get; set; }

    public required DateTimeOffset CheckedAt { get; set; }

    /// <summary>The checkout that took it, as it names itself.</summary>
    [MaxLength(ClaimRequest.MaxRunnerLength)]
    public required string Runner { get; set; }

    /// <summary>The name of the credential it arrived under - a name and not an id, for the reason <see cref="EfHatchIssue.CreatedBy"/> is.</summary>
    [MaxLength(Common.PersonName.MaxChars)]
    public required string CheckedBy { get; set; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string? WriteFailing(IReadOnlyList<FailingCheckDto> failing) =>
        failing.Count == 0 ? null : JsonSerializer.Serialize(failing, Json);

    /// <summary>The stored list, or empty. A row that will not parse reads as empty rather than throwing the verdict away.</summary>
    public static IReadOnlyList<FailingCheckDto> ReadFailing(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<FailingCheckDto>>(stored, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// What the build on the tip of a repository's trunk came to - the same fact as
/// <see cref="EfHatchBuildCheck"/>, but keyed on the repository and its trunk
/// rather than on an issue's branch, because a trunk build is nobody's issue.
///
/// One row per repository per trunk, keyed on <see cref="Canonical"/> and
/// <see cref="Trunk"/>: two projects may bind one repository with different
/// base branches, and a single verdict would let one project's trunk overwrite
/// another's. A second write for the same pair replaces the first.
/// </summary>
/// <remarks>
/// The sha is what makes a verdict comparable, for the reason
/// <see cref="EfHatchBuildCheck"/>'s is - a verdict about any other sha says
/// nothing about the trunk as it stands now. <see cref="BugIssueId"/> is the
/// bug filed while this trunk was failing (HA-95): it stays attached across new
/// failing shas, because a fix that fails again is the same outage, and is let
/// go the moment a build on this trunk passes, so the next failure offers the
/// button again.
/// </remarks>
[Table("TrunkBuilds")]
[Index(nameof(Canonical), nameof(Trunk), IsUnique = true)]
public class EfHatchTrunkBuild
{
    public const int MaxRemoteLength = EfHatchBuildCheck.MaxRemoteLength;
    public const int MaxRefLength = EfHatchBuildCheck.MaxRefLength;
    public const int MaxShaLength = EfHatchBuildCheck.MaxShaLength;
    public const int MaxVerdictLength = EfHatchBuildCheck.MaxVerdictLength;
    public const int MaxFailing = EfHatchBuildCheck.MaxFailing;
    public const int MaxCheckNameLength = EfHatchBuildCheck.MaxCheckNameLength;
    public const int MaxCheckUrlLength = EfHatchBuildCheck.MaxCheckUrlLength;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>The remote as the runner spelled it.</summary>
    [MaxLength(MaxRemoteLength)]
    public required string Remote { get; set; }

    /// <summary><see cref="RemoteIdentity.Canonical"/> of <see cref="Remote"/> - half of the unique index.</summary>
    [MaxLength(MaxRemoteLength)]
    public required string Canonical { get; set; }

    /// <summary>The trunk's name, as the runner's workspace reported it - the other half of the unique index.</summary>
    [MaxLength(MaxRefLength)]
    public required string Trunk { get; set; }

    /// <summary>The trunk's tip that the verdict is about.</summary>
    [MaxLength(MaxShaLength)]
    public required string Sha { get; set; }

    /// <summary>When the board first heard about <see cref="Sha"/>. Kept across writes for the same sha; the board's clock, as <see cref="CheckedAt"/> is.</summary>
    public required DateTimeOffset ShaSince { get; set; }

    /// <summary>One of <see cref="BuildVerdicts"/>.</summary>
    [MaxLength(MaxVerdictLength)]
    public required string Verdict { get; set; }

    /// <summary>The failing checks as <c>[{ name, url }]</c>, sorted by name. jsonb, as <see cref="EfHatchBuildCheck.Failing"/> is.</summary>
    public string? Failing { get; set; }

    public required DateTimeOffset CheckedAt { get; set; }

    /// <summary>The checkout that took it, as it names itself.</summary>
    [MaxLength(ClaimRequest.MaxRunnerLength)]
    public required string Runner { get; set; }

    /// <summary>The name of the credential it arrived under - a name and not an id, for the reason <see cref="EfHatchIssue.CreatedBy"/> is.</summary>
    [MaxLength(Common.PersonName.MaxChars)]
    public required string CheckedBy { get; set; }

    /// <summary>The bug filed while this trunk was failing, or null when none is attached - see this type's own summary.</summary>
    public long? BugIssueId { get; set; }
    public EfHatchIssue? BugIssue { get; set; }
}

/// <summary>
/// One thing that happened to an issue. Append-only, written by every mutating
/// endpoint, never edited and never deleted except with its issue.
///
/// There is no reporting in the MVP and this table is still written from Phase
/// 1, because an event log is the one feature that cannot be added
/// retroactively: turned on in March, it answers nothing about February. The
/// cost is a row per edit and the benefit is that throughput, cycle time, and
/// "when did this actually move" are a query away rather than a migration away.
/// </summary>
/// <remarks>
/// Rank-only moves are deliberately not events. Dragging a card up its column
/// is board hygiene, not work, and logging it would bury the status changes
/// that matter under a hundred lines of tidying.
/// </remarks>
[Table("IssueEvents")]
[Index(nameof(IssueId), nameof(At))]
public class EfHatchIssueEvent
{
    public const int MaxKindLength = 32;

    /// <summary>The kinds, all of them. A string rather than an enum so a new one is a write, not a migration.</summary>
    public const string Created = "created";
    public const string Retitled = "retitled";
    public const string Redescribed = "redescribed";
    public const string Retyped = "retyped";
    public const string StatusChanged = "status_changed";
    public const string ParentChanged = "parent_changed";
    public const string ReadyChanged = "ready_changed";
    public const string DueChanged = "due_changed";

    /// <summary>
    /// The issue was pointed at a pull request, or taken off one. The field
    /// holds one URL; this is what makes the trail hold every URL it has ever
    /// held - see <see cref="EfHatchIssue.PullRequestUrl"/>.
    /// </summary>
    public const string PullRequestChanged = "pull_request_changed";

    /// <summary>
    /// The issue was given to somebody, handed to somebody else, or taken off
    /// everybody. The payload's <c>from</c> and <c>to</c> each carry a
    /// <c>name</c> beside the kind and the id, and that is load-bearing: an
    /// assignee resolves only while the identity is live, so an id alone would
    /// stop reading the day a person is deleted or a key is revoked - which is
    /// exactly when "whose was this in March" gets asked. The same argument
    /// <see cref="EfHatchIssue.CreatedBy"/> makes for being a name.
    /// </summary>
    public const string AssigneeChanged = "assignee_changed";

    /// <summary>
    /// The issue was pinned to a model, moved to another, or handed back to
    /// the playbook. What a ticket was spent on is a question somebody asks
    /// after the bill, so the trail holds every value the column has held -
    /// see <see cref="EfHatchIssue.ModelOverride"/>.
    /// </summary>
    public const string ModelOverrideChanged = "model_override_changed";

    /// <summary>The same, for the thinking budget - see <see cref="EfHatchIssue.EffortOverride"/>.</summary>
    public const string EffortOverrideChanged = "effort_override_changed";

    /// <summary>
    /// The issue's priority level changed - see
    /// <see cref="EfHatchIssue.Priority"/>. The payload carries both sides by
    /// name, so the trail says which way it went rather than only that
    /// somebody touched it: "who put this at the front of the night, and
    /// when" is the question a level that reorders a whole board has to be
    /// able to answer.
    /// </summary>
    public const string PriorityChanged = "priority_changed";

    /// <summary>
    /// The issue was marked express, or unmarked - see
    /// <see cref="EfHatchIssue.Express"/>. The payload carries both sides, the
    /// same as <see cref="PriorityChanged"/>.
    /// </summary>
    public const string ExpressChanged = "express_changed";

    /// <summary>
    /// The issue was made to wait on another, or freed from one. Written on the
    /// issue that waits and on it alone - the edge is that issue's, and a second
    /// event on the blocker would be the same fact filed twice.
    /// </summary>
    public const string DependencyAdded = "dependency_added";

    /// <summary>The reverse - see <see cref="DependencyAdded"/>.</summary>
    public const string DependencyRemoved = "dependency_removed";

    /// <summary>
    /// A runner took the lease on this issue - see
    /// <see cref="EfHatchIssue.ClaimToken"/>. Written because the trail's
    /// question is "who took this ticket", and on a board worked by several
    /// checkouts that is a question with a wrong answer.
    /// </summary>
    public const string ClaimTaken = "claim_taken";

    /// <summary>
    /// The claim a take found on the row had already gone quiet past its lease -
    /// silence, a session that stopped printing, or a runner giving up on its
    /// own after a partition - and the take that follows is a takeover rather
    /// than a fresh claim. Written before <see cref="ClaimTaken"/>, so the trail
    /// says who stopped answering and when before it says who holds it now. A
    /// kind is a string, so this needed no migration.
    /// </summary>
    public const string ClaimLapsed = "claim_lapsed";

    /// <summary>
    /// The holder let go of it, presenting the token it was given. The payload
    /// carries <c>outcome</c> beside <c>from</c> and <c>to</c> - one of
    /// <see cref="ClaimOutcomes"/>, or absent where the caller did not say how
    /// the increment ended, which is a release nothing here refuses.
    /// </summary>
    public const string ClaimReleased = "claim_released";

    /// <summary>
    /// A runner's verdict on whether the issue's branch merges with the trunk
    /// changed - see <see cref="EfHatchMergeCheck"/>. The payload carries the
    /// canonical remote, because an issue may hold one verdict per repository,
    /// and <c>from</c> and <c>to</c> as <c>{ verdict, files }</c> (<c>from</c> is
    /// null for the first verdict on that repository). A verdict that repeats
    /// the stored one writes none: the trail is for when a branch started and
    /// stopped conflicting, not for how often somebody looked.
    /// </summary>
    public const string MergeCheckChanged = "merge_check_changed";

    /// <summary>
    /// A runner's verdict on the build on the tip of the issue's branch changed -
    /// see <see cref="EfHatchBuildCheck"/>. The payload carries the canonical
    /// remote, because an issue may hold one verdict per repository, and
    /// <c>from</c> and <c>to</c> as <c>{ verdict, sha, failing }</c> (<c>from</c>
    /// is null for the first verdict on that repository; <c>failing</c> is the
    /// names of the checks). Written when the verdict, the sha or the set of
    /// failing names changes, so the trail says when a build started failing and
    /// when it stopped. A verdict that repeats the stored one, and a mark that
    /// only sets whether an increment pushed the sha, write none.
    /// </summary>
    public const string BuildCheckChanged = "build_check_changed";

    /// <summary>
    /// The operator took it off somebody - the same column cleared, and a
    /// different fact. A runner letting go and a person prising a ticket loose
    /// are told apart by the kind and not by the payload, which is the same
    /// <c>{ from, to }</c> shape either way.
    /// </summary>
    public const string ClaimCleared = "claim_cleared";

    /// <summary>
    /// This issue's runner was told, at a claim heartbeat, that the board had
    /// chosen it to make room for an emergency issue - see <c>Preemption</c>
    /// for the five rules. The payload carries <c>emergencyKey</c> and
    /// <c>emergencyTitle</c>, its own shape rather than <c>{ from, to }</c>:
    /// this is a fact plus a detail, not a transition, the same reasoning
    /// <see cref="MergeCheckChanged"/>'s payload gets its own remark for. Also
    /// doubles as the record of "already told" - a heartbeat asks whether an
    /// issue carries one of these since its own <c>ClaimedAt</c> the same way
    /// <c>WorkController.LetGoAsync</c> asks whether a trail carries a release
    /// since its last status change, so nothing about a preemption is ever
    /// nominated or written down in advance.
    /// </summary>
    public const string ClaimPreempted = "claim_preempted";

    public const string Commented = "commented";

    /// <summary>A message was sent to whichever session is working the issue.</summary>
    public const string Messaged = "messaged";

    /// <summary>A message was put in front of a session. The payload names the comment and the runner.</summary>
    public const string MessageDelivered = "message_delivered";

    /// <summary>A question was asked, and the issue is waiting on a person until it is answered.</summary>
    public const string Asked = "asked";

    /// <summary>A question was answered. The payload names which one.</summary>
    public const string Answered = "answered";

    /// <summary>
    /// A move into a full WIP section was let through because a person said
    /// <em>move anyway</em> - see <see cref="WipGate"/>.
    /// Written beside <see cref="StatusChanged"/> by the same save, actor and
    /// instant, with payload <c>{ limit, load, to }</c> where <c>load</c> is the
    /// load the move left, counting the card itself (so a 5-of-5 refusal that is
    /// overridden writes "6 of 5"). Written only when the move would otherwise
    /// have been refused - an override on a move that had room, or on an issue
    /// already counted, writes nothing.
    /// </summary>
    public const string WipOverridden = "wip_overridden";

    public const string Imported = "imported";

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>Set by EF fixup when the event is added through <see cref="EfHatchIssue.Events"/> - see <see cref="EfHatchComment.IssueId"/>.</summary>
    public long IssueId { get; set; }
    public EfHatchIssue? Issue { get; set; }

    [MaxLength(Common.PersonName.MaxChars)]
    public required string Actor { get; set; }

    [MaxLength(MaxKindLength)]
    public required string Kind { get; set; }

    /// <summary>
    /// What changed, as <c>jsonb</c>: <c>{ "from": ..., "to": ... }</c> for an
    /// edit, the source filename for an import. Schemaless on purpose - the
    /// shape differs per kind, and a column per field would be a migration
    /// every time a new verb is logged.
    /// </summary>
    public string? Payload { get; set; }

    public required DateTimeOffset At { get; set; }
}

/// <summary>
/// What an agent should be told, and how much thought to spend, when it moves
/// an issue of some type from one column to the next.
///
/// The matrix exists because "do the next increment of work" is not one job.
/// Turning a paragraph of intent into an epic with stories under it is the
/// hardest thinking in the flow and wants the largest model at the highest
/// effort; picking up an already-specified task and writing the code is
/// ordinary work that a smaller one does well. Encoding that as rows rather
/// than as branches in a script means the operator retunes it from a page
/// after watching a run go badly, which is the only way anybody ever finds the
/// right settings.
/// </summary>
/// <remarks>
/// Deliberately not writable by an API key - see
/// <see cref="PlaybooksController"/>. A playbook chooses the model and the
/// prompt for the next agent, so an agent that could edit one could widen its
/// own instructions and its own budget, and the loop that results has no
/// natural end. The operator writes these; agents read them.
/// </remarks>
[Table("Playbooks")]
[Index(nameof(FromStatusId), nameof(ToStatusId), nameof(Types), nameof(Shape), IsUnique = true)]
public class EfHatchPlaybook
{
    public const int MaxTypesLength = 60;
    public const int MaxModelLength = 60;
    public const int MaxEffortLength = 10;
    public const int MaxShapeLength = 10;
    public const int MaxPromptLength = 20_000;

    /// <summary>
    /// The thinking budget, as the Claude Code CLI spells it - what
    /// <c>--effort</c> accepts and nothing else, because a value this does not
    /// recognise is one the CLI refuses at spawn time, long after the operator
    /// has stopped looking at the page they typed it on.
    /// </summary>
    public static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>
    /// The model aliases the CLI resolves to whatever is current. Aliases
    /// rather than pinned ids because a playbook says "the big one" and means
    /// it a year from now; a full <c>claude-…</c> name is accepted too, for an
    /// operator who has a reason to pin.
    /// </summary>
    public static readonly string[] ModelAliases = ["haiku", "sonnet", "opus", "fable"];

    /// <summary>A pinned model id, for the operator who wants exactly one.</summary>
    private const string FullModelPattern = "^claude-[a-z0-9][a-z0-9.-]{0,48}$";

    public const string AnyShape = "any";
    public const string LeafShape = "leaf";
    public const string ParentShape = "parent";

    /// <summary>
    /// Whether the issue's children are consulted at all (<see cref="AnyShape"/>),
    /// or it must have none (<see cref="LeafShape"/>) or at least one
    /// (<see cref="ParentShape"/>).
    /// </summary>
    public static readonly string[] Shapes = [AnyShape, LeafShape, ParentShape];

    /// <summary>
    /// What a row created without an opinion takes. The middle of the range on
    /// both axes, deliberately: a playbook nobody has tuned yet should do the
    /// work adequately and cost adequately, so that the first thing the
    /// operator learns from it is what the transition actually needs.
    /// </summary>
    public const string DefaultModel = "sonnet";
    public const string DefaultEffort = "medium";
    public const string DefaultShape = AnyShape;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The column the issue is sitting in when the agent picks it up.</summary>
    public required int FromStatusId { get; set; }
    public EfHatchStatus? FromStatus { get; set; }

    /// <summary>The column it is meant to be in when the agent stops.</summary>
    public required int ToStatusId { get; set; }
    public EfHatchStatus? ToStatus { get; set; }

    /// <summary>
    /// Which issue types this applies to, comma separated and normalised by
    /// <see cref="NormalizeTypes"/> - or empty, which means every type.
    ///
    /// A string rather than a join table because the set has four members and
    /// is read whole every time it is read at all; the unique index above is
    /// what the normalisation is for, and it is why "task,bug" and "bug, task"
    /// must not be two rows.
    /// </summary>
    [MaxLength(MaxTypesLength)]
    public required string Types { get; set; }

    /// <summary>
    /// Whether the issue's children matter to this row: <see cref="AnyShape"/>
    /// means they are not consulted, <see cref="LeafShape"/> means the issue
    /// must have none, <see cref="ParentShape"/> means it must have at least
    /// one. A fixed word, not a CSV like <see cref="Types"/> - a row speaks for
    /// exactly one shape.
    /// </summary>
    [MaxLength(MaxShapeLength)]
    public required string Shape { get; set; } = AnyShape;

    /// <summary>
    /// What the agent is told before it is shown the ticket. The ticket body is
    /// the brief; this is the method - what "one increment" means for this
    /// transition, and what shape the answer takes.
    /// </summary>
    [MaxLength(MaxPromptLength)]
    public required string Prompt { get; set; }

    [MaxLength(MaxModelLength)]
    public required string Model { get; set; }

    [MaxLength(MaxEffortLength)]
    public required string Effort { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Lower case, de-duplicated, in the order <see cref="EfHatchIssue.Types"/>
    /// declares them, joined with commas. One set of types has exactly one
    /// spelling, so the unique index can do its job.
    /// </summary>
    public static string NormalizeTypes(IEnumerable<string>? types)
    {
        if (types is null) return "";

        var wanted = types
            .Select(t => t?.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToHashSet();

        // Every type named is the same as none named - both mean "any issue" -
        // and storing it as the empty set keeps one meaning to one row.
        if (wanted.Count == 0 || EfHatchIssue.Types.All(wanted.Contains)) return "";

        return string.Join(",", EfHatchIssue.Types.Where(wanted.Contains));
    }

    /// <summary>The stored string back as a list. Empty stays empty.</summary>
    public static string[] SplitTypes(string? types) =>
        string.IsNullOrEmpty(types) ? [] : types.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether this playbook speaks for an issue of this type and shape.</summary>
    public bool Covers(string issueType, bool isParent) =>
        (Types.Length == 0 || SplitTypes(Types).Contains(issueType)) &&
        (Shape == AnyShape || (Shape == ParentShape) == isParent);

    /// <summary>
    /// How closely it speaks for it. A playbook that names the type beats one
    /// that names every type, so "inbox to todo, epics" can say something
    /// different from "inbox to todo, anything else" without either having to
    /// know about the other. A playbook that names the shape beats one that
    /// does not, on the same footing - but naming the type still outweighs
    /// naming the shape, so a row with a shape and no type (1) never outranks
    /// a row with a type and no shape (2); only a row naming both (3) beats a
    /// row naming just the type.
    /// </summary>
    public int Specificity => (Types.Length == 0 ? 0 : 2) + (Shape == AnyShape ? 0 : 1);

    public static bool IsValidEffort(string? effort) =>
        effort is not null && Efforts.Contains(effort);

    public static bool IsValidModel(string? model) =>
        model is not null &&
        (ModelAliases.Contains(model) ||
         Regex.IsMatch(model, FullModelPattern, RegexOptions.None, TimeSpan.FromSeconds(1)));

    public static bool IsValidShape(string? shape) =>
        shape is not null && Shapes.Contains(shape);
}

/// <summary>
/// A WIP limit: how much of one slice of the board the operator will let stand
/// at once, across every column <see cref="EfHatchStatus.IsWip"/> flags. One row
/// per slice, keyed by <see cref="Types"/>. Hatch knows exactly two slices,
/// <see cref="Slices"/>, in that order - <see cref="StoriesAndBugs"/> and
/// <see cref="Epics"/> - and no row for a slice means no limit, which is why a
/// limit is a nullable read rather than a row that is always there holding a
/// large number.
/// </summary>
[Table("WipLimits")]
[Index(nameof(Types), IsUnique = true)]
public class EfHatchWipLimit
{
    /// <summary>The stories-and-bugs slice, comma separated the way <see cref="EfHatchPlaybook.Types"/> is.</summary>
    public const string StoriesAndBugs = "story,bug";

    /// <summary>The epic slice.</summary>
    public const string Epics = "epic";

    /// <summary>Every slice Hatch knows, in the fixed order the board and the CLI print them in.</summary>
    public static readonly string[] Slices = [StoriesAndBugs, Epics];

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Which issue types this limit counts, normalised the way <see cref="EfHatchPlaybook.NormalizeTypes"/> does.</summary>
    [MaxLength(EfHatchPlaybook.MaxTypesLength)]
    public required string Types { get; set; }

    /// <summary>How many issues of these types may sit across the WIP columns at once.</summary>
    public required int Limit { get; set; }
}

/// <summary>
/// One issue waits on another: an edge saying that <see cref="DependsOn"/> must
/// be done before <see cref="Issue"/> is implemented.
///
/// The dependency, and not shared parentage, is what serialises work. An epic
/// whose five stories must land one after another gets a chain of these and the
/// loop walks it in order; an epic whose five stories are independent gets none
/// and they go in whatever order the board puts them in. The rule this replaced
/// - no sibling awaiting review - was a guess about every parent on the board,
/// true of the epics whose stories touch the same files and wrong about the
/// rest.
/// </summary>
/// <remarks>
/// A row with an <c>Id</c> and an author rather than a bare join table, for the
/// reason every other table in the module has one: who said two things must
/// land in order, and when, is worth keeping, and a join entity that already
/// exists is where the next field goes.
///
/// The unique index is what makes re-adding an edge a no-op rather than a
/// second row - a property of the table instead of a check somebody remembered.
/// The index on <see cref="DependsOnId"/> alone is the reverse direction, which
/// the <c>Blocks</c> list and the dispatcher's gate both read, and is there for
/// the reason <see cref="EfHatchComment.AnswersId"/> carries one.
/// </remarks>
[Table("IssueDependencies")]
[Index(nameof(IssueId), nameof(DependsOnId), IsUnique = true)]
[Index(nameof(DependsOnId))]
public class EfHatchIssueDependency
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>The issue that waits.</summary>
    public required long IssueId { get; set; }
    public EfHatchIssue? Issue { get; set; }

    /// <summary>The issue waited on. Satisfied only once it sits in a terminal column.</summary>
    public required long DependsOnId { get; set; }
    public EfHatchIssue? DependsOn { get; set; }

    [MaxLength(Common.PersonName.MaxChars)]
    public required string CreatedBy { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// What one agent session cost, on the ticket it was spent on.
///
/// A row per unattended increment: the session id <c>claude --resume</c> takes,
/// how long it ran, what it burned in tokens and in notional dollars, and a
/// sentence saying what it did. Account-wide utilization is honest but
/// anonymous - no arithmetic over it can say which epic ate the evening - and
/// the dispatcher is the only thing in the system that knows which ticket a
/// session's spend was for. This table is that knowledge, written down.
///
/// The dollars are notional API list price as the CLI reports it, not money
/// that left an account: on a subscription nothing was billed per session. Both
/// figures are stored and both go out on the wire, so which one is the headline
/// is a decision the page makes and not one the schema has baked in.
/// </summary>
/// <remarks>
/// No author column, and it is the one table in the module without one. Every
/// other row records who acted; here the actor <em>is</em> the session, whose
/// id is already the row's identity.
///
/// Two limits are accepted rather than designed around. An interactive session
/// - <c>hatch.sh work -i</c>, or one resumed by hand - leaves no row, because
/// nothing is watching its exit; that hole is smaller than making every
/// terminal session a writer. And a run that dies before it can describe itself
/// still gets a row, marked undescribed: losing an evening's spend because a
/// session could not compose a summary would be the wrong trade.
///
/// Nothing prunes these. One row per increment is small, and the day it is not,
/// a retention window is a sampler's worth of work rather than a schema change.
/// </remarks>
[Table("WorkLogEntries")]
// What makes a retried write idempotent rather than a second row - the same
// property EfHatchIssueDependency's unique index has, and for the same reason:
// the caller is a shell script at the end of a run, and a run can be re-run.
[Index(nameof(IssueId), nameof(SessionId), IsUnique = true)]
// The issue page's one query: this issue's entries, newest first.
[Index(nameof(IssueId), nameof(EndedAt))]
public class EfHatchWorkLogEntry
{
    public const int MaxSessionIdLength = 64;
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 2000;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>Set by EF fixup when the entry is added through <see cref="EfHatchIssue.WorkLog"/> - see <see cref="EfHatchComment.IssueId"/>.</summary>
    public long IssueId { get; set; }
    public EfHatchIssue? Issue { get; set; }

    /// <summary>What <c>claude --resume</c> takes, and this row's identity within its issue.</summary>
    [MaxLength(MaxSessionIdLength)]
    public required string SessionId { get; set; }

    /// <summary>Wall clock either side of the CLI, taken by the dispatcher.</summary>
    /// <remarks>
    /// Deliberately not reconciled with <see cref="DurationMs"/>, which is what
    /// the session itself reported. The pair says when the increment occupied
    /// the machine; the duration says how long it was thinking, which is the
    /// smaller number and the honest answer to "how long did this take".
    /// </remarks>
    public required DateTimeOffset StartedAt { get; set; }
    public required DateTimeOffset EndedAt { get; set; }

    /// <summary>The session's own <c>duration_ms</c> - see <see cref="StartedAt"/>.</summary>
    public required long DurationMs { get; set; }

    /// <summary>What the session did, in a few words. Null when it never said.</summary>
    [MaxLength(MaxTitleLength)]
    public string? Title { get; set; }

    /// <summary>The same, at length. Sessions are asked to keep it under 100 words.</summary>
    [MaxLength(MaxSummaryLength)]
    public string? Summary { get; set; }

    /// <summary>
    /// Whether the session said what it did. Set by the server from
    /// <see cref="Title"/> and <see cref="Summary"/> and never sent by a caller,
    /// so the two cannot come to disagree.
    /// </summary>
    /// <remarks>
    /// A column rather than a client-side test for an empty title, because
    /// "this run never described itself" is a fact about the run and a client
    /// inferring it from an absent string is a client guessing.
    /// </remarks>
    public bool Described { get; set; }

    /// <summary>The session's <c>is_error</c>. Its spend counted either way.</summary>
    public bool IsError { get; set; }

    /// <summary>The session's <c>num_turns</c>.</summary>
    public int Turns { get; set; }

    /// <summary>
    /// Notional API list price, in dollars, as the CLI reported it. Eight
    /// places because a short session costs a fraction of a cent and rounding
    /// it to two would make a night of them add up to nothing.
    /// </summary>
    public decimal CostUsd { get; set; }

    /// <summary>
    /// The session's four token counts, summed over <see cref="ModelUsage"/>.
    /// </summary>
    /// <remarks>
    /// A denormalisation, said out loud because a denormalisation nobody wrote
    /// down is a bug waiting for its second writer: these are the sum of the
    /// breakdown and are computed from it by the endpoint that writes the row,
    /// never sent independently. They are columns and not a fold over the json
    /// because every total in the hierarchy is an aggregate over them.
    /// </remarks>
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long CacheReadTokens { get; set; }

    /// <summary>
    /// The per-model breakdown the four counts are the sum of, as <c>jsonb</c>:
    /// an ordered list of <see cref="WorkLogModelUseDto"/>. Null when the run
    /// ended before the accounting arrived.
    /// </summary>
    /// <remarks>
    /// <c>jsonb</c> and not a table, on the grounds
    /// <see cref="EfHatchIssueEvent.Payload"/> is: it is read whole with the row
    /// that owns it, and a table would buy a join and a cascade for nothing. The
    /// day somebody asks which model a quarter went on, it is a query against
    /// this column rather than a migration.
    /// </remarks>
    public string? ModelUsage { get; set; }

    /// <summary>When the row landed, which is not when the session ended.</summary>
    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A runner: one <c>go-to-work</c> or <c>work</c> process, and what the board
/// would like it to do next.
///
/// The row is written from two directions and they never overlap. The process
/// writes <see cref="Kind"/>, <see cref="LastSeenAt"/> and <see cref="Line"/>
/// on every heartbeat - "I am here, and this is what I am doing". The operator
/// writes <see cref="State"/> and the four bounds - "keep going", "finish and
/// stop", "stay under this epic" - and a heartbeat never overwrites one of
/// those once the row exists. What the loop reads back off its own heartbeat is
/// the second half, which is the whole round trip: nothing on the server starts
/// or stops a process, it says what it would like and the runner obeys between
/// increments.
/// </summary>
/// <remarks>
/// <para>There is no claim key here, deliberately, and it is the one thing
/// somebody adding a column to this table is most likely to want. What a runner
/// is working is <see cref="EfHatchIssue.ClaimRunner"/> read back the other way
/// - <c>Issues WHERE ClaimRunner = Name</c>, judged live - the same way an open
/// question is computed rather than stored. A column here would be a copy that
/// drifts the moment an operator clears a claim out from under the runner
/// holding it.</para>
///
/// <para>Nothing sweeps this table. A row ages through idle, then gone, then
/// out of the read entirely - all three by arithmetic against
/// <see cref="LastSeenAt"/> at the moment somebody asks, which is the same lazy
/// expiry the claim uses and for the same reason: there is no timeout to tune
/// and no sweeper to notice has stopped.</para>
/// </remarks>
[Table("Runners")]
public class EfHatchRunner
{
    /// <summary>Room for <c>host:/path/to/checkout</c> - the same name a claim carries, because it is the same name.</summary>
    public const int MaxNameLength = ClaimRequest.MaxRunnerLength;

    /// <summary>Room for a line the runner printed, on the terms a claim's chatter has.</summary>
    public const int MaxLineLength = ClaimRequest.MaxChatterLength;

    public const int MaxKindLength = 16;
    public const int MaxStateLength = 16;

    /// <summary>Room for an issue key, which is what <c>--under</c> takes.</summary>
    public const int MaxUnderLength = 32;

    // The five words both sides of the wire act on, read off the contract
    // rather than written down again here - the runner sends one of the kinds
    // and obeys one of the states, and the copy that would be wrong is always
    // the one nobody was looking at.
    public const string LoopKind = RunnerKinds.Loop;
    public const string OnceKind = RunnerKinds.Once;
    public const string Running = RunnerStates.Running;
    public const string Paused = RunnerStates.Paused;
    public const string Stopping = RunnerStates.Stopping;

    /// <summary>The three a <c>PATCH</c> accepts, in the order a control surface should offer them.</summary>
    public static readonly string[] States = RunnerStates.All;

    /// <summary>
    /// What the runner calls itself - <c>host:/path/to/checkout</c>, or
    /// <c>HATCH_RUNNER</c>'s override. The key, because a runner already has
    /// exactly one identity and it is this one; a surrogate id would be a
    /// second name to keep in step with the claim's.
    /// </summary>
    [Key]
    [MaxLength(MaxNameLength)]
    public required string Name { get; set; }

    /// <summary>
    /// <see cref="LoopKind"/> or <see cref="OnceKind"/> - whether anything will
    /// ever read an instruction back. Written by every heartbeat rather than
    /// only on insert: the same checkout runs both commands, and a row that
    /// still said <c>loop</c> would offer a control surface for a process that
    /// exited an hour ago.
    /// </summary>
    [MaxLength(MaxKindLength)]
    public required string Kind { get; set; }

    /// <summary>The first time this name was ever seen, and never moved afterwards.</summary>
    public required DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>
    /// The last heartbeat. Every judgement about this row - idle, gone, dropped
    /// - is arithmetic against this and nothing else.
    /// </summary>
    public required DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// The last line the runner printed while holding no claim: what it is
    /// waiting for, why it is idle, why it stopped. In-increment chatter rides
    /// the claim instead and is read from there, so this is only ever drawn for
    /// a runner between tickets.
    /// </summary>
    [MaxLength(MaxLineLength)]
    public string? Line { get; set; }

    /// <summary>When that line arrived, so a stale one reads as stale.</summary>
    public DateTimeOffset? LineAt { get; set; }

    /// <summary>
    /// What the board would like this runner to do: <see cref="Running"/>,
    /// <see cref="Paused"/> or <see cref="Stopping"/>. Written by a person and
    /// never by a heartbeat - an agent that could set its own state could
    /// un-stop itself.
    /// </summary>
    [MaxLength(MaxStateLength)]
    public required string State { get; set; }

    /// <summary>
    /// The epic to stay inside, or null for the whole board -
    /// <c>go-to-work --under</c>, moved to the board.
    /// </summary>
    [MaxLength(MaxUnderLength)]
    public string? Under { get; set; }

    /// <summary>
    /// The cap on increments, compared against a count the runner keeps itself.
    /// Lowering it below what a night has already spent stops that loop at its
    /// next pass, which is the honest reading of a cap.
    /// </summary>
    public int? MaxRuns { get; set; }

    /// <summary>The cap on dollars, on the same terms.</summary>
    public decimal? MaxSpend { get; set; }

    /// <summary>
    /// When to stop, always as an instant. The command line takes a wall clock
    /// (<c>--until 06:00</c>) because somebody is typing it at a terminal in a
    /// timezone; a row on a page is read by a browser that knows its own, so
    /// the stored form is the unambiguous one.
    /// </summary>
    public DateTimeOffset? UntilAt { get; set; }

    /// <summary>
    /// The checkouts this runner is serving, canonical and newline-joined - a
    /// fact about the running process, overwritten on every heartbeat that
    /// names any, the way <see cref="Under"/> and the other bounds are not.
    /// </summary>
    public string? Remotes { get; set; }

    /// <summary>Whether this runner makes a clone for itself when it lacks one.</summary>
    public bool? Clones { get; set; }

    /// <summary>
    /// Whether this runner was started with <c>do-my-work</c> or
    /// <c>--mine</c> - working its owner's tickets only. A fact about the
    /// running process, written on every beat like <see cref="Clones"/> and
    /// <see cref="Remotes"/>, and shown rather than editable: unlike
    /// <see cref="Under"/> it is not a page's to set on a runner that is
    /// already going.
    /// </summary>
    public bool? Mine { get; set; }

    /// <summary>
    /// The machine and checkout this runner runs from, <c>host:/path</c> - a
    /// fact about the running process, overwritten on every heartbeat that
    /// names one, the same as <see cref="Remotes"/>. It used to be
    /// <see cref="Name"/> itself; now the name is a character, and this is
    /// where a person goes to find the box that character is running on.
    /// </summary>
    [MaxLength(MaxNameLength)]
    public string? Where { get; set; }

    /// <summary>
    /// This runner's own account ran out of Claude usage, and this is when it
    /// expects to reset - a fact about the process, like <see cref="Mine"/>,
    /// never a person's to set. A heartbeat that names one writes it; a loop
    /// heartbeat that says it is not out clears it at once, which is the only
    /// way it ever clears other than the value expiring by arithmetic at read
    /// time, the same lazy expiry the rest of this table uses.
    /// </summary>
    public DateTimeOffset? ExhaustedUntil { get; set; }

    /// <summary>
    /// The account's usage windows, as this runner's own session last read
    /// them off its <c>rate_limit_event</c> stream - JSON, an array of
    /// <see cref="RunnerUsageWindowDto"/>. Written by a heartbeat that carries
    /// a non-empty reading and never cleared: a reading never becomes wrong,
    /// only old, so an incarnation that has just restarted mid-night holds no
    /// reading until its next session's first event, rather than blanking the
    /// one already stored.
    /// </summary>
    public string? Usage { get; set; }

    /// <summary>
    /// When this runner read <see cref="Usage"/>, its own word for it - stored
    /// as reported, so an idle runner re-sending an hour-old reading on every
    /// poll does not make it look new.
    /// </summary>
    public DateTimeOffset? UsageReadAt { get; set; }

    /// <summary>
    /// The person this runner works for - the calling key's owner, the same
    /// resolution <c>--mine</c> takes, written on every beat because whose key
    /// this is can change on the API Keys page and the row must follow. Null
    /// is ordinary: a key that belongs to nobody.
    /// </summary>
    public Guid? ForPersonId { get; set; }
}

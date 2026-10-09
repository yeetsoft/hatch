using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Issues: the create, the read, the edit, the delete, and the drag.
///
/// Two things here are worth reading before changing anything. Every mutation
/// writes an <see cref="EfHatchIssueEvent"/>, from the first release, because
/// an event log is the one feature that cannot be added retroactively - turned
/// on in March it answers nothing about February. And the create path mints its
/// own number under optimistic concurrency rather than reaching for a sequence,
/// so it stays testable in memory and its failure mode is a retry rather than a
/// duplicated key (docs/hatch.md, "Issue numbering").
/// </summary>
[ApiController]
[Route("api/hatch/issues")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class IssuesController(
    HatchContext db,
    RankService ranks,
    IActorDirectory actors,
    IssueClaims claims,
    ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// How many times a create will re-read the project and try again. Five is
    /// far past what two humans and an agent can produce; the point of a bound
    /// is that a pathological loop ends in a 409 rather than in a spinning
    /// request.
    /// </summary>
    private const int MintAttempts = 5;

    /// <summary>
    /// How many issues one bulk edit may name. Far past a board's worth of
    /// cards, and low enough that a request built by a loop somewhere cannot
    /// turn into a write nobody is watching.
    /// </summary>
    private const int MaxBulkKeys = 500;

    // ---- Reading ----

    [HttpGet("{key}")]
    public async Task<ActionResult<IssueDto>> GetIssue(string key, CancellationToken ct)
    {
        var issue = await LoadAsync(key, ct);
        return issue is null ? NotFound() : await ToDtoAsync(issue, ct);
    }

    /// <summary>
    /// The issues matching a filter, as cards. What the bulk-edit page runs
    /// before it changes anything, and what a script asks when "every task
    /// under this epic" is the set it means.
    ///
    /// Every parameter is optional and they combine with AND. No parameters at
    /// all is the whole board, which is the honest answer to an empty filter -
    /// there is no page size here, because a household's tracker holds hundreds
    /// of rows and a paged answer would be a second thing for every caller to
    /// get right.
    /// </summary>
    /// <param name="parentKey">
    /// The direct parent. The empty string means "no parent at all", which is
    /// the one filter that cannot be written any other way and is exactly how
    /// the orphans get found.
    /// </param>
    /// <param name="ancestorKey">
    /// Anything below this issue at any depth - the epic's stories and their
    /// tasks - and not the issue itself, because "under AER-1" is a question
    /// about what hangs beneath it.
    /// </param>
    /// <param name="text">
    /// A dumb case-insensitive substring of the title, plus the issue a key
    /// names outright, so pasting <c>AER-12</c> into a search box finds
    /// <c>AER-12</c>.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IssueCardDto>>> SearchIssues(
        [FromQuery] int? projectId,
        [FromQuery] string? type,
        [FromQuery] int? statusId,
        [FromQuery] string? parentKey,
        [FromQuery] string? ancestorKey,
        [FromQuery] string? text,
        CancellationToken ct)
    {
        if (type is not null && !EfHatchIssue.IsValidType(type))
            return BadRequest($"an issue is one of {string.Join(", ", EfHatchIssue.Types)} - not \"{type}\"");

        var query = db.Issues.AsNoTracking().AsQueryable();

        if (projectId is { } project) query = query.Where(i => i.ProjectId == project);
        if (type is not null) query = query.Where(i => i.Type == type);
        if (statusId is { } status) query = query.Where(i => i.StatusId == status);

        if (parentKey is not null)
        {
            if (parentKey.Length == 0)
            {
                query = query.Where(i => i.ParentId == null);
            }
            else
            {
                var parent = await LoadAsync(parentKey, ct);
                if (parent is null) return BadRequest($"there is no {parentKey}");
                query = query.Where(i => i.ParentId == parent.Id);
            }
        }

        if (!string.IsNullOrWhiteSpace(ancestorKey))
        {
            var ancestor = await LoadAsync(ancestorKey, ct);
            if (ancestor is null) return BadRequest($"there is no {ancestorKey}");

            // The shared walk, not a second one - a filter and a meter that
            // disagreed about what is under an epic would be a bug nobody
            // notices until the two are on the same screen.
            var descendants = await Rollup.DescendantIdsAsync(db, ancestor.Id, ct);
            query = query.Where(i => descendants.Contains(i.Id));
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim().ToLowerInvariant();

            // A key typed into a search box is a lookup, not a substring - and
            // it is resolved here rather than left to the title match because
            // the key is computed at the edge and is not a column to compare
            // against.
            long? named = IssueKey.TryParse(needle, out var keyProject, out var number)
                ? await db.Issues.WithKey(keyProject, number).Select(i => (long?)i.Id).FirstOrDefaultAsync(ct)
                : null;

            query = query.Where(i => i.Title.ToLower().Contains(needle) || (named != null && i.Id == named));
        }

        var rows = await query
            .OrderBy(i => i.Project!.Key)
            .ThenBy(i => i.Number)
            .Select(i => new
            {
                i.Id,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Type,
                i.Title,
                i.StatusId,
                i.Rank,
                ParentProjectKey = i.Parent == null ? null : i.Parent.Project!.Key,
                ParentNumber = i.Parent == null ? (int?)null : i.Parent.Number,
                i.ReadyAt,
                i.ReadyAtHasTime,
                i.DueAt,
                i.DueAtHasTime,
                i.AssigneePersonId,
                i.AssigneeApiKeyId,
                i.Priority,
                i.Express,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
            })
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        var priorities = await PriorityTree.ForAsync(db, ct);

        var cards = new List<IssueCardDto>(rows.Count);
        foreach (var i in rows)
        {
            var (effective, effectiveFrom) = priorities.Effective(i.Id);

            cards.Add(new IssueCardDto(
                IssueKey.Format(i.ProjectKey, i.Number),
                i.ProjectKey,
                i.Type,
                i.Title,
                i.StatusId,
                i.Rank,
                i.ParentNumber is { } n ? IssueKey.Format(i.ParentProjectKey!, n) : null,
                IssueMoment.Format(i.ReadyAt, i.ReadyAtHasTime),
                IssueMoment.Format(i.DueAt, i.DueAtHasTime),
                // A search result is a card, and a card that read one way here
                // and another on the board is the divergence IssueCardDto's own
                // docstring exists to prevent.
                Assignee: await IssueProjection.ToAssigneeAsync(actors, i.AssigneePersonId, i.AssigneeApiKeyId, ct),
                Claim: claims.Project(i.Claim, now),
                Expedited: effective >= PriorityLevels.Expedited,
                Express: i.Express,
                Priority: PriorityLevels.Name(effective),
                PriorityOwn: PriorityLevels.Name(i.Priority),
                PriorityFrom: effectiveFrom));
        }

        return cards;
    }

    // ---- Creating ----

    /// <summary>
    /// Files an issue. It lands in the leftmost column and at the bottom of it,
    /// both decided here - so no client has to know what the inbox is called
    /// this week, and a script that files a bug is the same three fields as the
    /// dialog.
    /// </summary>
    [HttpPost]
    public Task<ActionResult<IssueDto>> CreateIssue(IssueCreateRequest request, CancellationToken ct) =>
        CreateIssueAsync(request, PriorityLevels.Normal, ct);

    /// <summary>
    /// The whole of <see cref="CreateIssue"/>, plus the one thing a key must
    /// never set for itself: <paramref name="priority"/>. Internal, and called
    /// only from <see cref="TrunkBuildBugController"/> - the bug a failing
    /// trunk's button files is born expedited the way an issue under an express
    /// parent is born express (<see cref="EfHatchIssue.Express"/>), in the same
    /// save and with no second event, because expediting at birth is not a
    /// change from anything.
    /// </summary>
    internal async Task<ActionResult<IssueDto>> CreateIssueAsync(IssueCreateRequest request, int priority, CancellationToken ct)
    {
        var title = request.Title?.Trim();
        if (Invalid(title, request.Description, request.Type) is { } invalid) return BadRequest(invalid);

        if (!ReadMoment(request.ReadyAt, "readyAt", out var readyAt, out var momentError)) return BadRequest(momentError);
        if (!ReadMoment(request.DueAt, "dueAt", out var dueAt, out momentError)) return BadRequest(momentError);

        // Who is a program is ICallerIdentity's own narrowing - an API key, or
        // a keyless runner that named itself - asked once, the way
        // IssueWorkLogController and IssueClaimController do, so this cannot
        // come to disagree with them about what an agent is.
        //
        // A stranded tick is not read: a ticked column that has since been
        // made deferred or terminal does not count, exactly as IsWip does not
        // where the column became either (Wip.cs) - filing falls back to the
        // leftmost column instead of landing in a column nobody can see.
        //
        // The leftmost read is unchanged and stays the fallback, so a board
        // with nothing ticked, or a person filing, behaves exactly as it does
        // today.
        var status = await caller.IsProgramAsync(ct)
            ? await db.Statuses.FirstOrDefaultAsync(s => s.AgentFiles && !s.IsDeferred && !s.IsTerminal, ct)
              ?? await Leftmost(ct)
            : await Leftmost(ct);
        if (status is null) return Conflict("this board has no columns to put an issue in");

        Task<EfHatchStatus?> Leftmost(CancellationToken token) =>
            db.Statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).FirstOrDefaultAsync(token);

        var actor = await caller.ActorNameAsync(ct);
        var now = time.GetUtcNow();

        for (var attempt = 1; ; attempt++)
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);
            if (project is null) return NotFound();

            var parent = await ResolveParentAsync(request.ParentKey, project.Id, request.Type, null, ct);
            if (parent.Error is { } error) return BadRequest(error);

            // Taken from the parent at filing, and at no other time: an issue
            // created under an express parent is born express, whoever files
            // it and however - see EfHatchIssue.Express.
            var expressFrom = parent.Issue?.Express == true ? parent.Issue : null;

            var issue = new EfHatchIssue
            {
                ProjectId = project.Id,
                Number = project.NextIssueNumber,
                Type = request.Type,
                Title = title!,
                Description = request.Description ?? "",
                StatusId = status.Id,
                ParentId = parent.Issue?.Id,
                Rank = await ranks.BottomAsync(status.Id, ct),
                ReadyAt = readyAt?.At,
                ReadyAtHasTime = readyAt?.HasTime ?? false,
                DueAt = dueAt?.At,
                DueAtHasTime = dueAt?.HasTime ?? false,
                Express = expressFrom is not null,
                Priority = priority,
                CreatedBy = actor,
                CreatedAt = now,
                UpdatedAt = now,
            };

            // The mint and the issue in one SaveChanges. NextIssueNumber is a
            // concurrency token, so a second request that read the same number
            // fails here rather than writing a second AER-12 - and the unique
            // index on (ProjectId, Number) is the backstop under that.
            project.NextIssueNumber++;
            object createdPayload = expressFrom switch
            {
                null => new { type = issue.Type, title = issue.Title },
                { } e => new { type = issue.Type, title = issue.Title, expressFrom = await KeyOfAsync(e, ct) },
            };
            issue.Events.Add(Event(actor, EfHatchIssueEvent.Created, createdPayload, now));
            db.Issues.Add(issue);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < MintAttempts)
            {
                // Someone took the number between our read and our write. Drop
                // everything this attempt staged - including the failed insert,
                // which would otherwise be retried alongside the next one - and
                // read the project again.
                db.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateException)
            {
                return Conflict("could not mint an issue number - try again");
            }

            return CreatedAtAction(nameof(GetIssue), new { key = await KeyOfAsync(issue, ct) }, await ToDtoAsync(issue, ct));
        }
    }

    // ---- Editing ----

    /// <summary>
    /// Changes whichever fields the body names - null means "leave this alone",
    /// with the one exception the DTO spells out: an empty
    /// <see cref="IssuePatchRequest.ParentKey"/> clears the parent.
    ///
    /// One event per changed field, and nothing at all for a field re-sent
    /// unchanged. An audit trail that logs a no-op edit is one nobody reads.
    /// </summary>
    [HttpPatch("{key}")]
    public async Task<ActionResult<IssueDto>> PatchIssue(string key, IssuePatchRequest request, CancellationToken ct)
    {
        var issue = await LoadAsync(key, ct);
        if (issue is null) return NotFound();

        if (await OverrideRefusal(request.WipOverride, ct) is { } notAPerson) return notAPerson;
        if (await ProjectMoveRefusal(request.ProjectId, ct) is { } notAPersonMove) return notAPersonMove;

        if (Invalid(request.Title?.Trim(), request.Description, request.Type, required: false) is { } invalid)
            return BadRequest(invalid);

        if (!ReadEdit(request, out var edit, out var editError)) return BadRequest(editError);

        var actor = await caller.ActorNameAsync(ct);
        var now = time.GetUtcNow();

        // Decided once, against the issue as LoadAsync first returned it.
        // StageEditAsync still re-validates the target on every re-entry, so
        // an unknown target refuses on attempt one with no retry wasted on
        // it - this flag only chooses which of the two paths below wraps the
        // call.
        var movingRequested = edit.ProjectId is { } targetId && targetId != issue.ProjectId;

        if (!movingRequested)
        {
            var gate = new WipGate(db, claims, now);
            var (changed, error, conflict, full) = await StageEditAsync(
                issue, edit, actor, now, new ColumnBottoms(ranks), gate, request.WipOverride, ct);
            if (full is not null) return Conflict(full);
            if (conflict is not null) return Conflict(conflict);
            if (error is not null) return BadRequest(error);

            if (changed) await db.SaveChangesAsync(ct);

            return await ToDtoAsync(issue, ct);
        }

        // A real move mints a new number in the target project, which can
        // race the same way CreateIssueAsync's mint can - so it gets the same
        // retry, copied from CreateIssueAsync (:257-323) exactly.
        // ChangeTracker.Clear() drops every tracked entity, including the
        // issue already loaded above, so the reload and the staging are
        // inside the loop, not just the save.
        for (var attempt = 1; ; attempt++)
        {
            issue = await LoadAsync(key, ct);
            if (issue is null) return NotFound();

            var gate = new WipGate(db, claims, now);
            var (changed, error, conflict, full) = await StageEditAsync(
                issue, edit, actor, now, new ColumnBottoms(ranks), gate, request.WipOverride, ct);
            if (full is not null) return Conflict(full);
            if (conflict is not null) return Conflict(conflict);
            if (error is not null) return BadRequest(error);

            try
            {
                if (changed) await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < MintAttempts)
            {
                db.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateException)
            {
                return Conflict("could not mint an issue number - try again");
            }

            return await ToDtoAsync(issue, ct);
        }
    }

    /// <summary>
    /// One edit, applied to many issues. The board's other half: a filter finds
    /// a hundred cards, and this moves them.
    ///
    /// Three properties hold it together. Anything wrong with the <em>edit</em>
    /// - an unknown type, a date that is not a date, a column that does not
    /// exist - refuses the whole request, because that is a request nobody
    /// meant. Anything wrong with one <em>issue</em> - a parent it may not hang
    /// under, a key naming nothing - is reported against that key and leaves it
    /// untouched, because the other ninety-nine were fine. And an issue already
    /// holding every named value is reported as unchanged rather than as
    /// edited, so re-applying a bulk edit writes no events at all.
    /// </summary>
    /// <remarks>
    /// The whole batch is one <c>SaveChanges</c>, so a database that refuses
    /// the write takes nothing with it - there is no half-applied bulk edit to
    /// discover afterwards.
    /// </remarks>
    [HttpPost("bulk")]
    public async Task<ActionResult<IssueBulkResultDto>> BulkEdit(IssueBulkEditRequest request, CancellationToken ct)
    {
        if (await ProjectMoveRefusal(request.ProjectId, ct) is { } notAPersonMove) return notAPersonMove;
        if (request.ProjectId is { } targetId && !await db.Projects.AnyAsync(p => p.Id == targetId, ct))
            return BadRequest($"there is no project {targetId}");

        // Deduplicated rather than refused: a client that sent AER-1 twice meant
        // it once, and reporting the second as a failure would be a refusal
        // about nothing.
        var keys = (request.Keys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0) return BadRequest("name at least one issue to edit");
        if (keys.Count > MaxBulkKeys)
            return BadRequest($"a bulk edit covers at most {MaxBulkKeys} issues at once - this one named {keys.Count}");

        var patch = new IssuePatchRequest(
            null, null, request.Type, request.StatusId, request.ParentKey, request.ReadyAt, request.DueAt, null,
            request.ProjectId, request.MoveDescendants);

        if (Invalid(null, null, request.Type, required: false) is { } invalid) return BadRequest(invalid);
        if (!ReadEdit(patch, out var edit, out var editError)) return BadRequest(editError);
        if (edit.IsEmpty) return BadRequest("a bulk edit has to change something");

        var actor = await caller.ActorNameAsync(ct);
        var now = time.GetUtcNow();

        // One tracker for the batch: fifty issues sent to the same column are
        // fifty appends, and nothing is saved between them - see ColumnBottoms.
        var bottoms = new ColumnBottoms(ranks);

        // And the same for the WIP load: two stories in one bulk request aimed
        // at one free slot must not both be admitted. Bulk takes no override.
        var gate = new WipGate(db, claims, now);

        var changed = new List<string>();
        var unchanged = new List<string>();
        var failures = new List<IssueBulkFailureDto>();

        foreach (var key in keys)
        {
            var issue = await LoadAsync(key, ct);
            if (issue is null)
            {
                failures.Add(new IssueBulkFailureDto(key, $"there is no {key}"));
                continue;
            }

            var (moved, error, conflict, full) = await StageEditAsync(issue, edit, actor, now, bottoms, gate, wipOverride: false, ct);
            if (full is not null) failures.Add(new IssueBulkFailureDto(key, full.Error));
            else if (conflict is not null) failures.Add(new IssueBulkFailureDto(key, conflict));
            else if (error is not null) failures.Add(new IssueBulkFailureDto(key, error));
            else if (moved) changed.Add(await KeyOfAsync(issue, ct));
            else unchanged.Add(await KeyOfAsync(issue, ct));
        }

        if (changed.Count > 0) await db.SaveChangesAsync(ct);

        return new IssueBulkResultDto(changed, unchanged, failures);
    }

    /// <summary>
    /// Applies an edit to one issue, in two halves that must stay in this
    /// order: everything that can be refused is looked up first, and only then
    /// is anything on the issue touched.
    ///
    /// That ordering is what makes the bulk path safe. Both paths share one
    /// <c>SaveChanges</c> per request, so an edit that mutated a title and then
    /// discovered an illegal parent would leave the title change staged and
    /// written on behalf of a request that was refused.
    /// </summary>
    /// <returns>
    /// Whether anything changed, the sentence to refuse with if it could not be
    /// applied, a second sentence for the one refusal that is a plain
    /// <c>409</c> rather than a <c>400</c> - a live claim somewhere in a moving
    /// set - and lastly the WIP refusal, separate because it carries numbers
    /// rather than a sentence alone.
    /// </returns>
    private async Task<(bool Changed, string? Error, string? Conflict, WipRefusalDto? Full)> StageEditAsync(
        EfHatchIssue issue, IssueEdit edit, string actor, DateTimeOffset now, ColumnBottoms bottoms,
        WipGate gate, bool wipOverride, CancellationToken ct)
    {
        // ---- What could be refused ----

        // Resolved first, ahead of the parent lookup below, which needs to
        // know the target project before it runs. Naming the issue's own
        // project is read as no opinion at all - the same no-op a bulk edit
        // re-sending a value already held falls through as.
        EfHatchProject? targetProject = null;
        if (edit.ProjectId is { } targetProjectId)
        {
            var found = await db.Projects.FirstOrDefaultAsync(p => p.Id == targetProjectId, ct);
            if (found is null) return (false, $"there is no project {targetProjectId}", null, null);
            if (found.Id != issue.ProjectId) targetProject = found;
        }

        List<long>? movingIds = null;
        if (targetProject is not null)
        {
            movingIds = (edit.MoveDescendants ?? true)
                ? [issue.Id, ..await Rollup.DescendantIdsAsync(db, issue.Id, ct)]
                : [issue.Id];

            var holders = await db.Issues
                .Where(i => movingIds.Contains(i.Id))
                .Select(i => new
                {
                    i.Id,
                    Claim = new ClaimSnapshot(
                        i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                        i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
                })
                .ToListAsync(ct);

            var held = holders.Where(h => claims.IsLive(h.Claim, now)).ToList();
            if (held.Count > 0)
            {
                var names = new List<string>(held.Count);
                foreach (var h in held) names.Add((await KeyOfAsync(h.Id, ct))!);
                return (false, null, $"{string.Join(", ", names)} - nothing moved", null);
            }
        }

        EfHatchStatus? status = null;
        if (edit.StatusId is { } statusId && statusId != issue.StatusId)
        {
            status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == statusId, ct);
            if (status is null) return (false, $"there is no column {statusId}", null, null);
        }

        // Present-but-empty is the clear; absent is no opinion. See
        // IssuePatchRequest. A real move defaults that "no opinion" to the
        // clear - an ancestor can never be in the moving set, so the issue's
        // own parent is always left behind unless this same request also
        // names one.
        var parentKey = edit.ParentKey ?? (targetProject is not null ? "" : null);
        (EfHatchIssue? Issue, string? Error) parent = (null, null);
        if (parentKey is not null)
        {
            var parentProjectId = targetProject?.Id ?? issue.ProjectId;
            parent = await ResolveParentAsync(parentKey, parentProjectId, edit.Type ?? issue.Type, issue.Id, ct);
            if (parent.Error is { } parentError) return (false, parentError, null, null);
        }

        // Asked last, and only when the column is actually changing, so a
        // board with no limit set pays no read and nothing after this can
        // refuse the edit once the gate has admitted it.
        WipVerdict? wip = null;
        if (status is not null)
        {
            // The parent the card is left with after this edit: the one it is
            // being reparented to, when it is, otherwise its own - the same
            // rule the type is already judged by just above.
            var parentId = edit.ParentKey is not null ? parent.Issue?.Id : issue.ParentId;
            wip = await gate.AdmitAsync(issue, status.Id, edit.Type ?? issue.Type, parentId, wipOverride, ct);
            if (wip.Kind == WipVerdictKind.Refused) return (false, null, null, wip.Refusal);
        }

        // ---- What is written ----

        var events = new List<EfHatchIssueEvent>();

        if (edit.Title is { Length: > 0 } title && title != issue.Title)
        {
            events.Add(Event(actor, EfHatchIssueEvent.Retitled, new { from = issue.Title, to = title }, now));
            issue.Title = title;
        }

        if (edit.Description is { } description && description != issue.Description)
        {
            events.Add(Event(actor, EfHatchIssueEvent.Redescribed, new { from = issue.Description, to = description }, now));
            issue.Description = description;
        }

        if (edit.Type is { } type && type != issue.Type)
        {
            events.Add(Event(actor, EfHatchIssueEvent.Retyped, new { from = issue.Type, to = type }, now));
            issue.Type = type;
        }

        if (status is not null)
        {
            // The whole row rather than the name, because two of its flags are
            // read below - what is being left as well as what is being entered.
            var from = await db.Statuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == issue.StatusId, ct);
            events.Add(Event(actor, EfHatchIssueEvent.StatusChanged, new { from = from?.Name, to = status.Name }, now));

            issue.StatusId = status.Id;
            // A column change through PATCH has no neighbours to sit between,
            // so the card goes to the bottom of the new column. The board sends
            // its drops to `move`, which does have them.
            issue.Rank = await bottoms.NextAsync(status.Id, ct);

            if (wip is { Kind: WipVerdictKind.Overridden } overridden)
                foreach (var over in overridden.Overrides)
                    events.Add(Event(actor, EfHatchIssueEvent.WipOverridden, WipOverriddenPayload(over, status.Name), now));

            // Newly shelved - not shuffled between two deferred columns. See
            // Deferrals: the issues waiting on this one are about to wait
            // indefinitely, and they are told on their own tickets.
            if (status.IsDeferred && from?.IsDeferred != true)
                await Deferrals.NoteAsync(db, [issue.Id], status, actor, now, ct);
        }

        // Both dates read present-but-empty as the clear, the same way ParentKey
        // does. The comparison is on the formatted string rather than on the
        // instant and the flag separately: those two are one fact, and comparing
        // them apart is how "2026-09-12" re-sent unchanged ends up logged as an
        // edit against the midnight it is stored as.
        if (edit.SetReady)
        {
            var from = IssueMoment.Format(issue.ReadyAt, issue.ReadyAtHasTime);
            var to = IssueMoment.Format(edit.ReadyAt?.At, edit.ReadyAt?.HasTime ?? false);

            if (to != from)
            {
                events.Add(Event(actor, EfHatchIssueEvent.ReadyChanged, new { from, to }, now));
                issue.ReadyAt = edit.ReadyAt?.At;
                issue.ReadyAtHasTime = edit.ReadyAt?.HasTime ?? false;
            }
        }

        if (edit.SetDue)
        {
            var from = IssueMoment.Format(issue.DueAt, issue.DueAtHasTime);
            var to = IssueMoment.Format(edit.DueAt?.At, edit.DueAt?.HasTime ?? false);

            if (to != from)
            {
                events.Add(Event(actor, EfHatchIssueEvent.DueChanged, new { from, to }, now));
                issue.DueAt = edit.DueAt?.At;
                issue.DueAtHasTime = edit.DueAt?.HasTime ?? false;
            }
        }

        // Present-but-empty clears, the same as the dates. The event is what
        // makes the trail hold every pull request the issue ever had, while the
        // column holds the one it is being reviewed at now.
        if (edit.SetPullRequest && edit.PullRequestUrl != issue.PullRequestUrl)
        {
            events.Add(Event(
                actor,
                EfHatchIssueEvent.PullRequestChanged,
                new { from = issue.PullRequestUrl, to = edit.PullRequestUrl },
                now));

            issue.PullRequestUrl = edit.PullRequestUrl;
        }

        if (parentKey is not null && parent.Issue?.Id != issue.ParentId)
        {
            var from = issue.ParentId is null ? null : await KeyOfAsync(issue.ParentId.Value, ct);
            var to = parent.Issue is null ? null : await KeyOfAsync(parent.Issue, ct);
            events.Add(Event(actor, EfHatchIssueEvent.ParentChanged, new { from, to }, now));
            issue.ParentId = parent.Issue?.Id;
        }

        if (targetProject is not null)
        {
            var target = targetProject;

            // Rank-then-id - the same "board order" convention
            // IssueClaims.LineageAsync, Rollup's NodesAsync and Dispatch
            // already use. The identity map hands back the same tracked
            // instance for issue.Id that LoadAsync already attached, so the
            // root is not double-tracked.
            var moving = await db.Issues
                .Where(i => movingIds!.Contains(i.Id))
                .OrderBy(i => i.Rank).ThenBy(i => i.Id)
                .ToListAsync(ct);

            // Every member shares one project before the move - captured
            // from the root now, since re-deriving a moving issue's old key
            // after its own fields change below would read the new project
            // instead.
            var oldProjectKey = issue.Project!.Key;
            var oldKeys = moving.ToDictionary(m => m.Id, m => IssueKey.Format(oldProjectKey, m.Number));

            foreach (var m in moving)
            {
                m.Number = target.NextIssueNumber++;
                m.ProjectId = target.Id;

                // EF does not refresh an already-loaded reference navigation
                // just because the FK scalar changed - IssueProjection's
                // ToDtosAsync fallback would otherwise hand back the old
                // project's key in the response DTO despite ProjectId
                // already being correct underneath.
                m.Project = target;
                m.UpdatedAt = now;

                object payload = m.Id == issue.Id
                    ? new { from = oldKeys[m.Id], to = IssueKey.Format(target.Key, m.Number), descendants = moving.Count - 1 }
                    : new { from = oldKeys[m.Id], to = IssueKey.Format(target.Key, m.Number) };
                m.Events.Add(Event(actor, EfHatchIssueEvent.ProjectChanged, payload, now));
            }

            // Left behind rather than carried with the root, only when the
            // request asked for that split - DeleteIssue's own outdent
            // writes no event for the same shape of change, but there the
            // parent disappears; here it survives elsewhere, so the child's
            // own trail is the only place that says why it lost it.
            if (!(edit.MoveDescendants ?? true))
            {
                var children = await db.Issues.Where(i => i.ParentId == issue.Id).ToListAsync(ct);
                foreach (var child in children)
                {
                    child.ParentId = null;
                    child.UpdatedAt = now;
                    child.Events.Add(Event(
                        actor, EfHatchIssueEvent.ParentChanged, new { from = oldKeys[issue.Id], to = (string?)null }, now));
                }
            }
        }

        if (events.Count == 0 && targetProject is null) return (false, null, null, null);

        foreach (var e in events) issue.Events.Add(e);
        issue.UpdatedAt = now;
        return (true, null, null, null);
    }

    /// <summary>
    /// Removes an issue, its comments, and its events. The accepted MVP gap,
    /// written down in the plan: a deleted issue takes its audit trail with it.
    ///
    /// Its children are outdented rather than deleted. Losing a parent is an
    /// outdent; deleting an epic should not quietly take eleven stories off the
    /// board.
    /// </summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> DeleteIssue(string key, CancellationToken ct)
    {
        var issue = await LoadAsync(key, ct);
        if (issue is null) return NotFound();

        var children = await db.Issues.Where(i => i.ParentId == issue.Id).ToListAsync(ct);
        foreach (var child in children) child.ParentId = null;

        db.Issues.Remove(issue);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- The drag ----

    /// <summary>
    /// Where a card landed. The client names the neighbours it can see and
    /// never a rank - the server computes the number, which is what keeps the
    /// board, a script, and Claude all moving a card the same way.
    /// </summary>
    /// <remarks>
    /// A move within a column writes no event. Tidying a column is board
    /// hygiene rather than work, and logging it would bury the status changes
    /// that matter under a hundred lines of dragging.
    ///
    /// <c>FromStatusId</c> is a precondition on what the caller last saw: a
    /// card that is anywhere else is a 409 and nothing is written. The board
    /// is up to thirty seconds behind and the loop moves cards all night, so an
    /// undo names the column it is taking the card out of.
    /// </remarks>
    [HttpPost("{key}/move")]
    public async Task<ActionResult<IssueDto>> MoveIssue(string key, IssueMoveRequest request, CancellationToken ct)
    {
        var issue = await LoadAsync(key, ct);
        if (issue is null) return NotFound();

        if (await OverrideRefusal(request.WipOverride, ct) is { } notAPerson) return notAPerson;

        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == request.StatusId, ct);
        if (status is null) return BadRequest($"there is no column {request.StatusId}");

        if (request.FromStatusId is { } expected && issue.StatusId != expected)
        {
            var current = await db.Statuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == issue.StatusId, ct);
            return Conflict($"{key} is in {current?.Name ?? "another column"} now - nothing moved");
        }

        var after = await NeighbourIdAsync(request.AfterKey, ct);
        var before = await NeighbourIdAsync(request.BeforeKey, ct);

        var changedColumn = issue.StatusId != status.Id;
        if (changedColumn)
        {
            var now = time.GetUtcNow();
            var gate = new WipGate(db, claims, now);
            var wip = await gate.AdmitAsync(issue, status.Id, issue.Type, issue.ParentId, request.WipOverride, ct);
            if (wip.Kind == WipVerdictKind.Refused) return Conflict(wip.Refusal);

            var actor = await caller.ActorNameAsync(ct);
            var from = await db.Statuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == issue.StatusId, ct);
            issue.Events.Add(Event(actor, EfHatchIssueEvent.StatusChanged, new { from = from?.Name, to = status.Name }, now));
            if (wip.Kind == WipVerdictKind.Overridden)
                foreach (var over in wip.Overrides)
                    issue.Events.Add(Event(actor, EfHatchIssueEvent.WipOverridden, WipOverriddenPayload(over, status.Name), now));
            issue.UpdatedAt = now;
            issue.StatusId = status.Id;

            // The board has no deferred column to drop onto, so this endpoint
            // is not how a ticket is usually shelved - but it is an endpoint,
            // and a note owed to whoever is waiting is not owed less because
            // the move came in through a different door. Same call, same rule.
            if (status.IsDeferred && from?.IsDeferred != true)
                await Deferrals.NoteAsync(db, [issue.Id], status, actor, now, ct);
        }

        // The rank is computed after the column is set, and both are saved in
        // one call - so a renumbered column and the card that caused it can
        // never land separately.
        issue.Rank = await ranks.PlaceAsync(status.Id, issue.Id, after, before, ct);
        await db.SaveChangesAsync(ct);

        return await ToDtoAsync(issue, ct);
    }

    // ---- Loading ----

    /// <summary>
    /// The issue a display key names, tracked. Any key that does not resolve -
    /// malformed, an unknown project, a number nobody minted - is the same 404,
    /// because there is nothing useful to tell apart between them.
    /// </summary>
    private async Task<EfHatchIssue?> LoadAsync(string key, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return null;

        return await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
    }

    /// <summary>A neighbour named by the client, or null when it named none - or one that has since gone.</summary>
    private async Task<long?> NeighbourIdAsync(string? key, CancellationToken ct)
    {
        if (key is null || !IssueKey.TryParse(key, out var projectKey, out var number)) return null;

        return await db.Issues.WithKey(projectKey, number).Select(i => (long?)i.Id).FirstOrDefaultAsync(ct);
    }

    // ---- Parenting ----

    /// <summary>
    /// The parent a key names, checked against every rule that makes a
    /// hierarchy a hierarchy: it exists, it is in the same project, the type
    /// pairing is legal, and it is not below the issue being parented.
    /// </summary>
    /// <param name="parentKey">Null for "no opinion", empty for "no parent".</param>
    /// <param name="selfId">The issue being parented, when it already exists - what the cycle walk is looking for.</param>
    private async Task<(EfHatchIssue? Issue, string? Error)> ResolveParentAsync(
        string? parentKey, int projectId, string type, long? selfId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parentKey)) return (null, null);
        if (!IssueKey.TryParse(parentKey, out var projectKeyPart, out var number))
            return (null, $"\"{parentKey}\" is not an issue key");

        var parent = await db.Issues.Include(i => i.Project)
            .WithKey(projectKeyPart, number).FirstOrDefaultAsync(ct);
        if (parent is null) return (null, $"there is no {parentKey}");

        if (parent.ProjectId != projectId)
            return (null, $"{parentKey} is in another project - an issue and its parent share one");

        if (parent.Id == selfId) return (null, "an issue cannot be its own parent");

        if (EfHatchIssue.LegalParentTypes.TryGetValue(type, out var legal) && !legal.Contains(parent.Type))
            return (null,
                $"{Article(type)} {type} hangs under {string.Join(" or ", legal.Select(t => $"{Article(t)} {t}"))}, "
                + $"not {Article(parent.Type)} {parent.Type}");

        // Walk up from the proposed parent. Reaching the issue being parented
        // means the link would close a loop - which is not merely untidy: the
        // detail page follows parents, and a loop is a page that never finishes
        // rendering.
        if (selfId is { } self)
        {
            var seen = new HashSet<long>();
            var ancestor = parent.ParentId;
            while (ancestor is { } id && seen.Add(id))
            {
                if (id == self) return (null, $"{parentKey} is already below this issue");
                ancestor = await db.Issues.Where(i => i.Id == id).Select(i => i.ParentId).FirstOrDefaultAsync(ct);
            }
        }

        return (parent, null);
    }

    // ---- The one narrowing ----

    /// <summary>
    /// A person, not a key. Overriding the WIP limit is the operator's call to
    /// make, the same way the limit itself is - see <see cref="WipController.NotAPerson"/>.
    /// Checked in the action rather than left to <c>RoleGate</c>, for
    /// <see cref="IssueClaimController.NotAPerson"/>'s reason: this route has to
    /// go on accepting a key, and <c>RoleGate</c> is dormant wherever the wall is
    /// off, where a keyless runner is a program too. Refuses whatever the load,
    /// and even where no limit is set, so an agent can never learn from the
    /// answer when the flag would have mattered.
    /// </summary>
    private async Task<ObjectResult?> OverrideRefusal(bool wipOverride, CancellationToken ct) =>
        wipOverride && await caller.IsProgramAsync(ct)
            ? new ObjectResult("overriding the WIP limit is a person's call, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;

    /// <summary>
    /// A person, not a key. Moving an issue to another project is the
    /// operator's call, the same way overriding the WIP limit is - see
    /// <see cref="OverrideRefusal"/>. Fires on the raw presence of
    /// <c>projectId</c>, before the target project is even looked up, the
    /// same way <see cref="OverrideRefusal"/> fires on the raw
    /// <c>wipOverride: true</c>.
    /// </summary>
    private async Task<ObjectResult?> ProjectMoveRefusal(int? projectId, CancellationToken ct) =>
        projectId is not null && await caller.IsProgramAsync(ct)
            ? new ObjectResult("moving an issue to another project is a person's call, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;

    // ---- Mapping ----

    /// <summary>
    /// "an epic", "a story". These sentences are read on screen by the person
    /// who just tried the thing, and "a epic" reads as a bug in everything
    /// around it.
    /// </summary>
    private static string Article(string noun) =>
        noun.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(noun[0])) ? "an" : "a";

    private static EfHatchIssueEvent Event(string actor, string kind, object? payload, DateTimeOffset at) => new()
    {
        Actor = actor,
        Kind = kind,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload),
        At = at,
    };

    /// <summary>
    /// One <c>wip_overridden</c> event's payload for one limit stepped over -
    /// <c>epic</c> present only when <paramref name="over"/> names one, so the
    /// section's own event never carries a null <c>epic</c> where it could
    /// simply carry none.
    /// </summary>
    private static object WipOverriddenPayload(WipOverride over, string to) =>
        over.Epic is { } epic
            ? new { limit = over.Limit, load = over.Load, to, epic }
            : new { limit = over.Limit, load = over.Load, to };

    private async Task<string> KeyOfAsync(EfHatchIssue issue, CancellationToken ct) =>
        IssueKey.Format(
            issue.Project?.Key ?? await db.Projects.Where(p => p.Id == issue.ProjectId).Select(p => p.Key).SingleAsync(ct),
            issue.Number);

    private Task<string?> KeyOfAsync(long issueId, CancellationToken ct) =>
        IssueProjection.KeyOfAsync(db, issueId, ct);

    private Task<IssueDto> ToDtoAsync(EfHatchIssue issue, CancellationToken ct) =>
        IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);

    /// <summary>
    /// The bottom of each column, for a request that appends to one more than
    /// once.
    ///
    /// <see cref="RankService.BottomAsync"/> asks the database where the bottom
    /// is, and a bulk edit saves nothing until it has finished - so fifty issues
    /// sent to the same column would every one of them be told the same number.
    /// Ties are survivable (the board breaks them by id, and the next drop into
    /// that column renumbers it) but they are not what was meant, and the order
    /// the operator's list was in is lost. So the first append per column asks,
    /// and the rest count on from it.
    /// </summary>
    private sealed class ColumnBottoms(RankService ranks)
    {
        private readonly Dictionary<int, long> handedOut = [];

        public async Task<long> NextAsync(int statusId, CancellationToken ct)
        {
            var rank = handedOut.TryGetValue(statusId, out var previous)
                ? previous + RankService.Gap
                : await ranks.BottomAsync(statusId, ct);

            handedOut[statusId] = rank;
            return rank;
        }
    }

    /// <summary>
    /// A patch with its dates already read, and with the difference between
    /// "no opinion" and "clear this" made into a flag rather than left as a
    /// null that means two things.
    /// </summary>
    /// <param name="SetReady">Whether the body mentioned <c>readyAt</c> at all. False leaves the date alone; true with a null <paramref name="ReadyAt"/> clears it.</param>
    /// <param name="SetPullRequest">The same flag for <c>pullRequestUrl</c>, read the same way.</param>
    private sealed record IssueEdit(
        string? Title,
        string? Description,
        string? Type,
        int? StatusId,
        string? ParentKey,
        bool SetReady,
        IssueMoment? ReadyAt,
        bool SetDue,
        IssueMoment? DueAt,
        bool SetPullRequest,
        string? PullRequestUrl,
        int? ProjectId,
        bool? MoveDescendants)
    {
        /// <summary>Whether this edit names nothing at all - the request a bulk edit refuses rather than reports as a hundred no-ops.</summary>
        public bool IsEmpty =>
            Title is null && Description is null && Type is null && StatusId is null
            && ParentKey is null && !SetReady && !SetDue && !SetPullRequest && ProjectId is null;
    }

    /// <summary>
    /// Turns a patch body into an <see cref="IssueEdit"/>, or says what is
    /// wrong with it. Both edit paths go through here, so a date that is not a
    /// date is refused in the same words whether one issue or fifty were named.
    /// </summary>
    private static bool ReadEdit(IssuePatchRequest request, out IssueEdit edit, out string? error)
    {
        edit = null!;

        if (!ReadMoment(request.ReadyAt, "readyAt", out var readyAt, out error)) return false;
        if (!ReadMoment(request.DueAt, "dueAt", out var dueAt, out error)) return false;
        if (!ReadPullRequestUrl(request.PullRequestUrl, out var pullRequestUrl, out error)) return false;

        edit = new IssueEdit(
            request.Title?.Trim(),
            request.Description,
            request.Type,
            request.StatusId,
            request.ParentKey,
            request.ReadyAt is not null,
            readyAt,
            request.DueAt is not null,
            dueAt,
            request.PullRequestUrl is not null,
            pullRequestUrl,
            request.ProjectId,
            request.MoveDescendants);

        return true;
    }

    /// <summary>
    /// Reads the pull request URL off a request body. Absent and empty both
    /// arrive here as "no URL", which the patch path - having already checked
    /// the field was present - reads as the clear.
    ///
    /// Absolute <c>http</c> or <c>https</c> is the whole rule, and it is a rule
    /// about the link opening rather than about whose forge it points at. A
    /// relative path and a bare <c>github.com/o/r/pull/1</c> are both refused,
    /// because a field whose only job is to be clicked should not hold
    /// something that does not go anywhere; a self-hosted forge on a private
    /// address is accepted, because Hatch has no opinion about where an
    /// operator reviews code (docs/ethos.md).
    /// </summary>
    /// <summary>What a refused pull request URL is told to be instead, in the words the refusal uses.</summary>
    private const string ExpectedPullRequestUrl =
        "an absolute http or https address, like https://example.com/owner/repo/pull/12";

    private static bool ReadPullRequestUrl(string? text, out string? url, out string? error)
    {
        url = null;
        error = null;

        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return true;

        if (trimmed.Length > EfHatchIssue.MaxPullRequestUrlLength)
        {
            error = $"a pull request url is at most {EfHatchIssue.MaxPullRequestUrlLength} characters";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            error = $"\"{trimmed}\" is not a pull request url - give {ExpectedPullRequestUrl}";
            return false;
        }

        url = trimmed;
        return true;
    }

    /// <summary>
    /// Reads one of the two dates off a request body. Absent and empty both
    /// arrive here as "no moment" - which the create path reads as "never had
    /// one" and the patch path, having already checked the field was present,
    /// reads as the clear.
    ///
    /// Formal validity is the only thing checked. A date in the past is a
    /// fact, not a mistake, and a ready date after a due date is a mix-up worth
    /// seeing on the card rather than one worth refusing the whole edit for.
    /// </summary>
    private static bool ReadMoment(string? text, string field, out IssueMoment? moment, out string? error)
    {
        moment = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text)) return true;

        if (!IssueMoment.TryParse(text, out var parsed))
        {
            error = $"\"{text}\" is not {field} - give {IssueMoment.Expected}";
            return false;
        }

        moment = parsed;
        return true;
    }

    /// <summary>
    /// The field rules, shared by create and patch. <paramref name="required"/>
    /// is what separates them: a create needs a title and a type, a patch may
    /// mention neither.
    /// </summary>
    private static string? Invalid(string? title, string? description, string? type, bool required = true)
    {
        if (required && string.IsNullOrEmpty(title)) return "an issue needs a title";
        if (title is { Length: > EfHatchIssue.MaxTitleLength })
            return $"a title is at most {EfHatchIssue.MaxTitleLength} characters";
        if (description is { Length: > EfHatchIssue.MaxDescriptionLength })
            return $"a description is at most {EfHatchIssue.MaxDescriptionLength} characters";
        if (required || type is not null)
            if (!EfHatchIssue.IsValidType(type))
                return $"an issue is one of {string.Join(", ", EfHatchIssue.Types)} - not \"{type}\"";
        return null;
    }
}

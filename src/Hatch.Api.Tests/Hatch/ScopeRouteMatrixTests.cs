using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Every Hatch route, asked as someone who may not see a project, on the real
/// host with the wall up. A project outside the caller's scope reads as one
/// that does not exist: 404, or an empty answer. This is the test that says so
/// for each route, so a route added next year that forgets the filter is a red
/// build rather than a leak nobody noticed.
/// </summary>
/// <remarks>
/// <para><b>Two lists, and a new route fails until it is on one.</b>
/// <see cref="ProjectBearing"/> holds a probe for each route that names or
/// returns something of a project. <see cref="ProjectFree"/> holds the routes
/// that do not, each with the reason. An action in neither, in both, or named
/// but gone fails <see cref="EveryHatchActionIsOnExactlyOneList"/>, which needs
/// no database. The shape is <c>AdminSurfaceTests</c>'s: the lists are the
/// audit, and the code is held to them.</para>
///
/// <para><b>Two kinds of probe.</b> A <i>lookup</i> names a project or an issue,
/// so for a caller who cannot see it the answer must be the answer for an id
/// nothing has: the same status, and never a 2xx and never a 403 - a 403 says
/// the thing is there. A <i>list</i> answers rows, so its body must hold none
/// of the hidden project's text and not its key. Each lookup also has a visible
/// twin, the same request at a project the caller can see, whose status must
/// differ from the nonexistent one: without it a probe whose path or body is
/// wrong would be a 404 everywhere and pass for the wrong reason.</para>
///
/// <para><b>Four callers.</b> The matrix runs for a person scoped to nothing,
/// a person scoped to project A, and a key owned by each. Each runs on its own
/// host and database: a twin can change A, and a delete would break the next
/// caller's twin. A caller scoped to nothing cannot see A either, so it has no
/// visible twin: A is asked of it as a second hidden project, and the twin is
/// the other caller's. Two twins are not asked to differ, each with the reason
/// at its entry (<c>NoTwin</c>).</para>
///
/// <para><b>Where the criteria are narrowed.</b> The role gate refuses a key
/// from a route that takes none (<c>AcceptKey</c> is false) with 403
/// <c>key_not_accepted</c>, before scope is read, and the same for all three
/// targets. For a key such a probe expects that 403 for each target, and is
/// exempt from "never 403" and from the twin rule. The same holds for the one
/// route only a key may call, for a person, and for a probe whose body is a
/// person's alone (a project move in a bulk edit).</para>
///
/// <para>Needs Postgres, and is skipped out loud without it. Run it with
/// <c>make test-api-db</c>.</para>
/// </remarks>
[Collection(global::Hatch.Api.Tests.Jobs.QuartzSchedulerCollection.Name)]
public class ScopeRouteMatrixTests
{
    /// <summary>
    /// One host and one database per caller, so most of a minute goes on the
    /// migrate, the seed and the first requests' JIT. Under the two minutes of
    /// inactivity that abort a run, so a stalled phase fails this case alone.
    /// </summary>
    private const int CaseBudgetSeconds = 90;

    /// <summary>Belt and braces over <see cref="Deadline"/>, a little past its own budget.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Namespace = "Hatch.Api.Modules.Hatch";

    /// <summary>The second issue of project A, which every body that names another issue names.</summary>
    private const string OtherIssue = "AAA-2";

    // ---- The reasons a route names no project ----

    private const string ColumnOfTheHouse = "a column belongs to the house; no project owns one";
    private const string WipIsTheHouses = "the WIP limit is the house's, and its counts read every project on purpose";
    private const string LocalModelsAreTheHouses = "the local-models table is the house's, board-wide; no project owns a row";
    private const string ScopeWriting = "what each person may see; an admin's, never a project's";
    private const string HouseSettings = "house settings and the agent credentials; no project";
    private const string HouseBackup = "the house backup; no project";
    private const string RunnerBinary = "the runner binary; no project";
    private const string RunnerAdmin = "runner administration and heartbeats; the claims a runner holds are HA-2389's";
    private const string NamesTheCaller = "names the caller and nothing else";
    private const string Ha2388 = "HA-2388";

    /// <summary>
    /// The routes that name no project and return none of one's rows, each with
    /// why. Attachments and images wait on their own story, HA-2388.
    /// </summary>
    private static readonly (string Name, string Reason)[] ProjectFree =
    [
        // A run holds what was seen in every project and carries none of one's
        // rows: the routes answer empty and not found unless the caller's scope
        // is everything - pinned in GooseControllerTests.
        ("GooseController.ListRuns", "a run names no project; a bounded caller is answered empty"),
        ("GooseController.GetRun", "a run names no project; a bounded caller is answered not found"),
        ("AttachmentsController.DeleteAttachment", Ha2388),
        ("AttachmentsController.GetAttachment", Ha2388),
        ("AttachmentsController.GetAttachments", Ha2388),
        ("AttachmentsController.PostAttachment", Ha2388),
        ("BackupController.GetBackup", HouseBackup),
        ("BackupController.PostBackupRun", HouseBackup),
        ("ImagesController.GetImage", Ha2388),
        ("ImportController.Preview", "parses the files it is sent and reads no project"),
        ("ImportController.PreviewText", "parses the text it is sent and reads no project"),
        ("LocalModelsController.CreateLocalModel", LocalModelsAreTheHouses),
        ("LocalModelsController.DeleteLocalModel", LocalModelsAreTheHouses),
        ("LocalModelsController.GetLocalModels", LocalModelsAreTheHouses),
        ("MeController.GetMe", NamesTheCaller),
        ("MeHomeController.PutHome", "sets the caller's own home screen; it names no project and returns none"),
        ("ProjectsController.CreateProject", "names no project; it makes one"),
        ("RunnerController.Download", RunnerBinary),
        ("RunnerController.Get", RunnerBinary),
        ("RunnersController.CreateRunner", RunnerAdmin),
        ("RunnersController.DeleteRunnerOrder", RunnerAdmin),
        ("RunnersController.DeleteRunnerSessionOrder", RunnerAdmin),
        ("RunnersController.DeleteRunnerSessionPause", RunnerAdmin),
        ("RunnersController.EndSession", RunnerAdmin),
        ("RunnersController.GetRunner", RunnerAdmin),
        ("RunnersController.GetRunners", RunnerAdmin),
        ("RunnersController.Heartbeat", RunnerAdmin),
        ("RunnersController.PatchRunner", RunnerAdmin),
        ("RunnersController.PatchRunnerSession", RunnerAdmin),
        ("RunnersController.PauseAll", RunnerAdmin),
        ("RunnersController.PostRunnerOrder", RunnerAdmin),
        ("RunnersController.PostRunnerSessionOrder", RunnerAdmin),
        ("RunnersController.PutRefusedModels", RunnerAdmin),
        ("RunnersController.RevokeRunner", RunnerAdmin),
        ("RunnersController.RotateRunner", RunnerAdmin),
        ("RunnersController.SessionHeartbeat", RunnerAdmin),
        ("RunnersController.SetRunnerOwner", RunnerAdmin),
        ("RunnersController.StopAll", RunnerAdmin),
        ("RunnersController.SuggestRunnerName", RunnerAdmin),
        ("ScopesController.GetScope", ScopeWriting),
        ("ScopesController.GetScopes", ScopeWriting),
        ("ScopesController.PutScope", ScopeWriting),
        ("SettingsController.GetClaudeToken", HouseSettings),
        ("SettingsController.GetCredential", HouseSettings),
        ("SettingsController.GetHatchSettings", HouseSettings),
        ("SettingsController.PutHatchSettings", HouseSettings),
        ("StatusesController.CreateStatus", ColumnOfTheHouse),
        ("StatusesController.DeleteStatus", ColumnOfTheHouse),
        ("StatusesController.GetStatuses", ColumnOfTheHouse),
        ("StatusesController.PatchStatus", ColumnOfTheHouse),
        ("StatusesController.PutAgentFiles", ColumnOfTheHouse),
        ("StatusesController.PutExpressSkips", ColumnOfTheHouse),
        ("StatusesController.PutImplementation", ColumnOfTheHouse),
        ("StatusesController.PutMergesPullRequest", ColumnOfTheHouse),
        ("StatusesController.PutParentPulls", ColumnOfTheHouse),
        ("StatusesController.PutProtected", ColumnOfTheHouse),
        ("StatusesController.PutRunnerTags", ColumnOfTheHouse),
        ("WipController.GetWip", WipIsTheHouses),
        ("WipController.PutWip", WipIsTheHouses),
    ];

    private enum Kind { Lookup, List }

    private enum Who { Hidden, Visible, Missing }

    /// <summary>What a request is about, built three ways from the seed: B's, A's, and one nothing has.</summary>
    private sealed record Target(
        string ProjectKey, int ProjectId, long DreamId, int PlaybookId, int[] ProposalIds, long TrunkBuildId, long FindingId,
        string Remote, int StatusId, int OtherStatusId, Guid PersonId)
    {
        public string Key => $"{ProjectKey}-1";
    }

    private sealed record Req(HttpMethod Method, string Path, string? Json = null, byte[]? Raw = null);

    /// <summary>
    /// What to send, and what is special about it. A lookup has one builder; a
    /// list may have several (unfiltered, then filtered at the hidden project).
    /// </summary>
    private sealed record Probe(Kind Kind, Func<Target, Req>[] Builds)
    {
        /// <summary>Deletes, and anything else that would break a later twin, run last.</summary>
        public bool Destructive { get; init; }

        /// <summary>
        /// Sent after every other twin, deletes included. It takes rows the other
        /// twins read with it: an issue's edge, omission and proposal, a project's
        /// everything.
        /// </summary>
        public bool Last { get; init; }

        /// <summary>The route lists issue text, so a caller who sees A must read A's marker in the first build.</summary>
        public bool ListsText { get; init; }

        /// <summary>The visible twin cannot be told from the nonexistent one by status; the reason is the comment at the entry.</summary>
        public bool NoTwin { get; init; }

        /// <summary>A key is refused a body it sends here before scope is read, as for a person-only route.</summary>
        public bool RefusesKey { get; init; }

        /// <summary>A person is refused here before scope is read: the route is a key's alone.</summary>
        public bool RefusesPerson { get; init; }

        /// <summary>The write is held to the answer an unbound remote gets: the same status and the same body shape.</summary>
        public bool LikeUnbound { get; init; }
    }

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private static Req Send(HttpMethod method, string path, object? body = null) =>
        new(method, path, body is null ? null : JsonSerializer.Serialize(body, Json));

    private static Req Get(string path) => new(HttpMethod.Get, path);
    private static Req Post(string path, object? body = null) => Send(HttpMethod.Post, path, body);
    private static Req Put(string path, object? body = null) => Send(HttpMethod.Put, path, body);
    private static Req Patch(string path, object? body = null) => Send(HttpMethod.Patch, path, body);
    private static Req Delete(string path) => new(HttpMethod.Delete, path);

    /// <summary>The JSON property paths of a body, values ignored, sorted; a body that is not JSON has none.</summary>
    private static List<string> BodyShape(string body)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(body);
            Walk(doc.RootElement, "$", paths);
        }
        catch (JsonException)
        {
        }
        return [.. paths];

        static void Walk(JsonElement element, string path, SortedSet<string> paths)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                        Walk(property.Value, $"{path}.{property.Name}", paths);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Walk(item, $"{path}[]", paths);
                    break;
                default:
                    paths.Add(path);
                    break;
            }
        }
    }

    private static Probe Lookup(Func<Target, Req> build) => new(Kind.Lookup, [build]);

    private static Probe Rows(bool listsText, params Func<Target, Req>[] builds) => new(Kind.List, builds) { ListsText = listsText };

    /// <summary>The first eight bytes of a PNG, which is all the logo upload reads to tell what it was sent.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Every route that names or returns something of a project, by
    /// <c>Controller.Action</c>. Written once; the code is held to it by
    /// <see cref="EveryHatchActionIsOnExactlyOneList"/>.
    /// </summary>
    private static readonly (string Name, Probe Probe)[] ProjectBearing =
    [
        // ---- Reads of the whole house, filtered by the caller's scope ----
        ("ActivityController.GetActivity", Rows(false, t => Get("/api/activity"), t => Get($"/api/activity?projectId={t.ProjectId}"))),
        ("AssigneeController.GetAssignees", Rows(false, t => Get("/api/assignees"))),
        ("AttentionController.GetAttention", Rows(false, t => Get("/api/attention"))),
        ("AmbitionsController.GetAmbitions", Rows(false, t => Get("/api/ambitions"), t => Get($"/api/ambitions?projectId={t.ProjectId}"))),
        ("BallotController.GetBallot", Rows(false, t => Get("/api/ballot"), t => Get($"/api/ballot?projectId={t.ProjectId}"))),
        ("BoardController.GetBoard", Rows(true, t => Get("/api/board"))),
        ("ChangelogController.GetChangelog", Rows(false, t => Get("/api/changelog"), t => Get($"/api/changelog?projectId={t.ProjectId}"))),
        ("GooseController.ListFindings", Rows(false, t => Get("/api/goose/findings"))),
        ("PlanController.GetPlan", Rows(false, t => Get("/api/plan"), t => Get($"/api/plan?projectId={t.ProjectId}"))),
        ("QuestionsController.GetQuestions", Rows(false, t => Get("/api/questions?open=false"))),
        ("SearchController.GetSearch", Rows(false, t => Get("/api/search"), t => Get($"/api/search?projectId={t.ProjectId}"))),
        ("UtilizationController.Get", Rows(false, t => Get("/api/utilization"))),
        ("WorkLogController.GetHistory", Rows(false, t => Get("/api/work-log/history"), t => Get($"/api/work-log/history?projectId={t.ProjectId}"))),
        ("WorkLogController.GetSessions", Rows(false, t => Get("/api/work-log/sessions"), t => Get($"/api/work-log/sessions?projectId={t.ProjectId}"))),
        ("WorkLogController.GetByPlaybook", Rows(false, t => Get("/api/work-log/by-playbook"), t => Get($"/api/work-log/by-playbook?projectId={t.ProjectId}"))),
        ("PlaybooksController.GetPlaybooks", Rows(false, t => Get("/api/playbooks"))),
        ("PlaybookProposalsController.GetProposals", Rows(false, t => Get("/api/playbook-proposals?state=all"))),
        ("PlaybookFileController.GetPlaybookFiles", Rows(false, t => Get("/api/playbook-files"))),
        ("DependencyFileController.GetDependencyFiles", Rows(false, t => Get("/api/dependency-files"))),
        ("TrunkBuildController.GetTrunkBuilds", Rows(false, t => Get("/api/trunk-builds"))),
        ("ProjectsController.GetProjects", Rows(false, t => Get("/api/projects"))),
        ("IssuesController.SearchIssues", Rows(true, t => Get("/api/issues"), t => Get($"/api/issues?projectId={t.ProjectId}"))),
        ("DreamsController.GetDreams", Rows(true, t => Get("/api/dreams"), t => Get($"/api/dreams?projectId={t.ProjectId}"))),
        ("ReportsController.GetTimeline", Rows(false, t => Get("/api/reports/timeline"), t => Get($"/api/reports/timeline?projectId={t.ProjectId}"))),
        ("ReportsController.GetFlow", Rows(false, t => Get("/api/reports/flow"), t => Get($"/api/reports/flow?projectId={t.ProjectId}"))),

        // By remote. A remote that only a hidden project binds is read as one
        // nobody binds, so these are listed with the remote of the hidden project.
        ("ChangelogController.GetChangelogFile", Rows(false, t => Get($"/api/changelog/file?remote={t.Remote}"))),
        ("ChangelogFileController.GetChangelogFiles", Rows(false, t => Get("/api/changelog-files"), t => Get($"/api/changelog-files?remote={t.Remote}"))),
        ("ChangelogFileController.PutChangelogFile", Rows(false, t => Put("/api/changelog-files", new { remote = t.Remote, trunk = "main", sha = "abc", runner = "r" }))),

        // A hidden repository answers what an unbound remote answers.
        ("TrunkBuildController.PutTrunkBuild", Rows(false, t => Put("/api/trunk-builds", new { remote = t.Remote, trunk = "main", sha = "abc", verdict = "passed", runner = "r" })) with { LikeUnbound = true }),
        ("PlaybookFileController.PutPlaybookFile", Rows(false, t => Put("/api/playbook-files", new { remote = t.Remote, trunk = "main", sha = "abc", runner = "r" })) with { LikeUnbound = true }),
        ("DependencyFileController.PutDependencyFile", Rows(false, t => Put("/api/dependency-files", new { remote = t.Remote, trunk = "main", sha = "abc", runner = "r" })) with { LikeUnbound = true }),

        // The work routes are listed as rows only. Their claim, hop and merge
        // behaviour is HA-2387's; what is held here is that a hidden issue's
        // text and key never come back from them.
        ("WorkController.GetNextWork", Rows(false, t => Get("/api/work/next"))),
        ("WorkController.GetQueue", Rows(false, t => Get("/api/work/queue"))),
        ("WorkController.GetReview", Rows(false, t => Get("/api/work/review"))),
        ("WorkController.GetWork", Rows(false, t => Get($"/api/work/{t.Key}"))),
        ("WorkController.HopWork", Rows(false, t => Post($"/api/work/{t.Key}/hop"))),
        ("WorkController.VerdictWork", Rows(false, t => Post($"/api/work/{t.Key}/verdict?heldToken={Guid.NewGuid()}"))),
        ("WorkController.MergedWork", Rows(false, t => Post($"/api/work/{t.Key}/merged", new { url = "pull-request", runner = "r" }))),
        ("WorkController.PushbackWork", Rows(false, t => Post($"/api/work/{t.Key}/pushback", new { url = "pull-request", runner = "r" }))),
        ("WorkController.LandedWork", Rows(false, t => Post($"/api/work/{t.Key}/landed"))),
        ("WorkController.ClosedWork", Rows(false, t => Post($"/api/work/{t.Key}/closed", new { url = "pull-request", runner = "r" }))),

        // ---- An issue, by key ----
        ("IssuesController.GetIssue", Lookup(t => Get($"/api/issues/{t.Key}"))),
        ("IssuesController.PatchIssue", Lookup(t => Patch($"/api/issues/{t.Key}", new { description = "patched" }))),
        ("IssuesController.MoveIssue", Lookup(t => Post($"/api/issues/{t.Key}/move", new { statusId = t.StatusId }))),
        ("IssuesController.DeleteIssue", Lookup(t => Delete($"/api/issues/{t.Key}")) with { Destructive = true, Last = true }),
        ("IssueThreadController.GetComments", Lookup(t => Get($"/api/issues/{t.Key}/comments"))),
        ("IssueThreadController.AddComment", Lookup(t => Post($"/api/issues/{t.Key}/comments", new { body = "a comment" }))),
        ("IssueThreadController.GetEvents", Lookup(t => Get($"/api/issues/{t.Key}/events"))),
        ("QuestionOverturnController.Overturn", Lookup(t => Post($"/api/issues/{t.Key}/questions/1/overturn", new { answer = "no" }))),
        ("QuestionsController.GetIssueQuestions", Lookup(t => Get($"/api/issues/{t.Key}/questions?open=false"))),
        ("PlanController.GetIssuePlan", Lookup(t => Get($"/api/plan/{t.Key}"))),
        ("IssueWorkLogController.GetWorkLog", Lookup(t => Get($"/api/issues/{t.Key}/work-log"))),

        // Only a key writes a work log entry: a person is refused before the lookup.
        ("IssueWorkLogController.PostEntry", Lookup(t => Post($"/api/issues/{t.Key}/work-log", new
        {
            sessionId = "session", startedAt = DateTimeOffset.UnixEpoch, endedAt = DateTimeOffset.UnixEpoch, durationMs = 0,
            title = "t", summary = "s", isError = false, turns = 1, costUsd = (decimal?)null, models = Array.Empty<object>(),
        })) with { RefusesPerson = true }),

        ("IssueDreamsController.GetIssueDreams", Lookup(t => Get($"/api/issues/{t.Key}/dreams"))),
        ("IssueDreamsController.PutIssueDreams", Lookup(t => Put($"/api/issues/{t.Key}/dreams", new { dreamIds = Array.Empty<long>() }))),
        ("IssueServesController.GetServes", Lookup(t => Get($"/api/issues/{t.Key}/serves"))),
        ("IssueServesController.PutServes", Lookup(t => Put($"/api/issues/{t.Key}/serves", new { capabilityIds = Array.Empty<long>() }))),
        ("IssueCapabilitiesController.GetCapabilities", Lookup(t => Get($"/api/issues/{t.Key}/capabilities"))),
        ("IssueCapabilitiesController.PostCapability", Lookup(t => Post($"/api/issues/{t.Key}/capabilities", new { statement = "s", howToTell = "h" }))),
        ("IssueCapabilitiesController.PatchCapability", Lookup(t => Patch($"/api/issues/{t.Key}/capabilities/1", new { statement = "s" }))),
        ("IssueCapabilitiesController.PutOrder", Lookup(t => Put($"/api/issues/{t.Key}/capabilities/order", new { ids = Array.Empty<long>() }))),
        ("IssueCapabilitiesController.DeleteCapability", Lookup(t => Delete($"/api/issues/{t.Key}/capabilities/1")) with { Destructive = true }),
        ("IssueCapabilitiesController.GetVerdicts", Lookup(t => Get($"/api/issues/{t.Key}/capabilities/1/verdicts"))),
        ("IssueCapabilitiesController.PostVerdict", Lookup(t => Post($"/api/issues/{t.Key}/capabilities/1/verdicts", new { verdict = "holds", lookedAt = "x", sha = "abc" }))),
        ("IssuePassesController.GetPasses", Lookup(t => Get($"/api/issues/{t.Key}/passes"))),
        ("IssueChangesController.GetChanges", Lookup(t => Get($"/api/issues/{t.Key}/changes"))),
        ("IssueChangesController.PostOutcome", Lookup(t => Post($"/api/issues/{t.Key}/changes/1/outcomes", new { epicKey = OtherIssue, outcome = "carries_on", why = "x" }))),
        ("IssueDependenciesController.AddDependency", Lookup(t => Post($"/api/issues/{t.Key}/dependencies", new { dependsOnKey = OtherIssue }))),
        ("IssueDependenciesController.RemoveDependency", Lookup(t => Delete($"/api/issues/{t.Key}/dependencies/{OtherIssue}")) with { Destructive = true }),
        ("IssueRunnerOmissionsController.PutOmittedRunner", Lookup(t => Put($"/api/issues/{t.Key}/omitted-runners/r", new { reason = "x" }))),
        ("IssueRunnerOmissionsController.DeleteOmittedRunner", Lookup(t => Delete($"/api/issues/{t.Key}/omitted-runners/r")) with { Destructive = true }),
        ("IssueRunnerTagsController.PutIssueRunnerTags", Lookup(t => Put($"/api/issues/{t.Key}/runner-tags", new { tags = Array.Empty<string>() }))),
        ("IssueMessagesController.Deliver", Lookup(t => Post($"/api/issues/{t.Key}/messages/deliver", new { }))),
        ("IssueRemandController.Remand", Lookup(t => Post($"/api/issues/{t.Key}/remand", new { statusId = t.StatusId }))),
        ("AssigneeController.PutIssueAssignee", Lookup(t => Put($"/api/issues/{t.Key}/assignee", new { kind = (string?)null, id = (Guid?)null }))),

        // Flags and levels on an issue. The value is a real one: a priority or a
        // level that is not is refused before the lookup, for every target.
        ("IssueExpediteController.PutIssuePriority", Lookup(t => Put($"/api/issues/{t.Key}/priority", new { priority = "normal" }))),
        ("IssueExpediteController.PutIssueExpedite", Lookup(t => Put($"/api/issues/{t.Key}/expedite", new { expedited = false }))),
        ("IssuePreApprovalController.PutIssuePreApproval", Lookup(t => Put($"/api/issues/{t.Key}/pre-approval", new { level = "none" }))),
        ("IssueLocalModelController.PutIssueLocalModel", Lookup(t => Put($"/api/issues/{t.Key}/local-model", new { level = "none" }))),
        ("QuestionLevelController.PutQuestionLevel", Lookup(t => Put($"/api/issues/{t.Key}/questions/1/level", new { level = "hold" }))),
        ("QuestionPromoteController.Promote", Lookup(t => Post($"/api/issues/{t.Key}/questions/1/promote", new { principle = "a principle" }))),
        ("IssueHumanWorkController.PutIssueHumanWork", Lookup(t => Put($"/api/issues/{t.Key}/human-work", new { value = "inherit" }))),
        ("IssuePreApprovalController.PutIssueExpress", Lookup(t => Put($"/api/issues/{t.Key}/express", new { express = false }))),
        ("IssueOnePrController.PutIssueOnePr", Lookup(t => Put($"/api/issues/{t.Key}/one-pr", new { value = "inherit" }))),
        ("IssueNoCodeController.PutIssueNoCode", Lookup(t => Put($"/api/issues/{t.Key}/no-code", new { reason = "" }))),
        ("IssueDangerousController.PutIssueDangerous", Lookup(t => Put($"/api/issues/{t.Key}/dangerous", new { reason = "" }))),
        ("IssueReleaseNoteController.PutIssueReleaseNote", Lookup(t => Put($"/api/issues/{t.Key}/release-note", new { note = "" }))),
        ("IssuePlaybookController.PatchIssuePlaybook", Lookup(t => Patch($"/api/issues/{t.Key}/playbook", new { model = (string?)null, effort = (string?)null }))),
        ("IssueWipLimitController.PatchIssueWipLimit", Lookup(t => Patch($"/api/issues/{t.Key}/wip", new { limit = (string?)null }))),
        ("IssueBudgetController.PatchIssueBudget", Lookup(t => Patch($"/api/issues/{t.Key}/budget", new { amount = (string?)null }))),
        ("IssueBudgetController.GetIssueBudget", Lookup(t => Get($"/api/issues/{t.Key}/budget"))),
        // No role at all, so open to a key: a dispatcher is who leaves this mark.
        ("IssueStallController.PutStall", Lookup(t => Put($"/api/issues/{t.Key}/stall", new { why = "x" }))),
        ("IssueStallController.PutHold", Lookup(t => Put($"/api/issues/{t.Key}/hold", new { held = false }))),
        ("IssueContributionController.PutIssueContribution", Lookup(t => Put($"/api/issues/{t.Key}/contribution", new { authorLogin = "someone", authorAssociation = "NONE" }))),

        // The claim, and what a runner records about an issue's branch.
        ("IssueClaimController.TakeClaim", Lookup(t => Post($"/api/issues/{t.Key}/claim", new { runner = "r" }))),
        ("IssueClaimController.Heartbeat", Lookup(t => Post($"/api/issues/{t.Key}/claim/heartbeat", new { token = Guid.NewGuid() }))),
        ("IssueClaimController.ReleaseClaim", Lookup(t => Delete($"/api/issues/{t.Key}/claim?token={Guid.NewGuid()}"))),
        ("BroughtCurrentController.PutBroughtCurrent", Lookup(t => Put($"/api/issues/{t.Key}/brought-current", new { trunk = "main", trunkSha = "abc", runner = "r" }))),
        ("BuildCheckController.PutBuildCheck", Lookup(t => Put($"/api/issues/{t.Key}/build-check", new { remote = t.Remote, branch = "x", sha = "abc", verdict = "pass", runner = "r" }))),
        ("MergeCheckController.PutMergeCheck", Lookup(t => Put($"/api/issues/{t.Key}/merge-check", new { remote = t.Remote, trunk = "main", trunkSha = "abc", verdict = "clean", branch = "x", branchSha = "abc", runner = "r" }))),
        ("LandingController.PutLanding", Lookup(t => Put($"/api/issues/{t.Key}/landing", new { remote = t.Remote, sha = "abc123" }))),
        ("PullRequestStateController.PutPullRequestState", Lookup(t => Put($"/api/issues/{t.Key}/pull-request-state", new { url = "pull-request", headSha = "abc", mergeState = "clean", reviewDecision = "none", runner = "r" }))),
        ("VerdictsController.PostVerdict", Lookup(t => Post($"/api/issues/{t.Key}/verdicts", new { verdict = "pass", findings = "f" }))),

        // ---- A name in the body or the query ----
        // A bulk edit that moves an issue to a project names it in the body: 400
        // "there is no project N". The project move is a person's, so a key is refused first.
        ("IssuesController.BulkEdit", Lookup(t => Post("/api/issues/bulk", new { keys = new[] { OtherIssue }, projectId = t.ProjectId })) with { RefusesKey = true }),
        ("IssuesController.CreateIssue", Lookup(t => Post("/api/issues", new { projectId = t.ProjectId, type = "story", title = "a new issue" }))),
        ("ImportController.Import", Lookup(t => Post("/api/import", new
        {
            projectId = t.ProjectId,
            docs = new[] { new { filename = "plan.md", title = "Imported", description = "d", state = 0, stories = Array.Empty<object>() } },
        }))),
        ("DreamsController.AddDream", Lookup(t => Post("/api/dreams", new { body = "a dream", projectId = t.ProjectId }))),
        ("DreamWeaveController.PostWeave", Lookup(t => Post("/api/dreams/weave", new { projectId = t.ProjectId, mode = "common" }))),
        ("PlaybookProposalsController.SubmitProposal", Lookup(t => Post("/api/playbook-proposals", new
        {
            reason = "why", issueKey = t.Key, changes = new[] { new { playbookId = t.PlaybookId, prompt = "p" } },
        }))),

        // A report that validates its project filter answers 400 "there is no
        // project N", for a hidden project as for one nothing has.
        ("ReportsController.GetCumulativeFlow", Lookup(t => Get($"/api/reports/cumulative-flow?project={t.ProjectKey}"))),
        ("ReportsController.GetChangelogReport", Lookup(t => Get($"/api/reports/changelog?projectId={t.ProjectId}"))),
        ("ReportsController.GetActivityReport", Lookup(t => Get($"/api/reports/activity?actor=someone&projectId={t.ProjectId}"))),
        ("ReportsController.GetConsumption", Lookup(t => Get($"/api/reports/consumption?projectId={t.ProjectId}"))),
        ("ReportsController.GetConsumptionCsv", Lookup(t => Get($"/api/reports/consumption.csv?projectId={t.ProjectId}"))),

        // ---- A dream, a playbook, a proposal and a trunk build, by id ----
        ("DreamsController.EditDream", Lookup(t => Patch($"/api/dreams/{t.DreamId}", new { body = "edited" }))),
        ("DreamsController.DeleteDream", Lookup(t => Delete($"/api/dreams/{t.DreamId}")) with { Destructive = true }),
        ("PlaybooksController.PatchPlaybook", Lookup(t => Patch($"/api/playbooks/{t.PlaybookId}", new { }))),
        ("PlaybooksController.DeletePlaybook", Lookup(t => Delete($"/api/playbooks/{t.PlaybookId}")) with { Destructive = true }),

        // A playbook that names a project: 400 "there is no project N". Whether
        // the visible twin succeeds turns on the board's reachability rules, so
        // its status is not asked to differ.
        ("PlaybooksController.CreatePlaybook", Lookup(t => Post("/api/playbooks", new
        {
            fromStatusId = t.StatusId, toStatusId = t.OtherStatusId, types = new[] { "task" }, prompt = "p", projectId = t.ProjectId.ToString(),
        })) with { NoTwin = true }),
        ("PlaybookProposalsController.GetProposal", Lookup(t => Get($"/api/playbook-proposals/{t.ProposalIds[0]}"))),

        // Deciding proposals takes the exact pending set. For a caller who sees
        // A that is A's three; for a hidden project's it is refused for the set,
        // the same as for ids nothing has. Approving applies a patch whose own
        // outcome is the playbook rules', so its twin is not asked to differ.
        ("PlaybookProposalsController.RejectAll", Lookup(t => Post("/api/playbook-proposals/reject-all", new { proposalIds = t.ProposalIds })) with { Destructive = true }),
        ("PlaybookProposalsController.ApproveAll", Lookup(t => Post("/api/playbook-proposals/approve-all", new { proposalIds = t.ProposalIds })) with { Destructive = true, NoTwin = true }),
        ("TrunkBuildBugController.PostBug", Lookup(t => Post($"/api/trunk-builds/{t.TrunkBuildId}/bug", new { }))),
        ("GooseFindingBugController.PostBug", Lookup(t => Post($"/api/goose/findings/{t.FindingId}/bug", new { }))),

        // ---- A project, by id ----
        ("ProjectsController.PatchProject", Lookup(t => Patch($"/api/projects/{t.ProjectId}", new { name = "A" }))),
        ("ProjectsController.DeleteProject", Lookup(t => Delete($"/api/projects/{t.ProjectId}")) with { Destructive = true, Last = true }),
        ("ProjectsController.GetRepositories", Lookup(t => Get($"/api/projects/{t.ProjectId}/repositories"))),
        ("ProjectsController.PutRepositories", Lookup(t => Put($"/api/projects/{t.ProjectId}/repositories", new[] { new { remote = t.Remote } }))),
        ("ProjectsController.GetLogo", Lookup(t => Get($"/api/projects/{t.ProjectId}/logo"))),
        ("ProjectsController.PutLogo", Lookup(t => new Req(HttpMethod.Put, $"/api/projects/{t.ProjectId}/logo", Raw: PngSignature))),
        ("ProjectsController.DeleteLogo", Lookup(t => Delete($"/api/projects/{t.ProjectId}/logo")) with { Destructive = true }),
        ("ProjectsController.GetMembers", Lookup(t => Get($"/api/projects/{t.ProjectId}/members"))),
        ("ProjectsController.PutMember", Lookup(t => Put($"/api/projects/{t.ProjectId}/members/{t.PersonId}", new { role = "owner" }))),
        ("ProjectsController.DeleteMember", Lookup(t => Delete($"/api/projects/{t.ProjectId}/members/{t.PersonId}")) with { Destructive = true }),
        ("ProjectsController.ClaimProject", Lookup(t => Post($"/api/projects/{t.ProjectId}/claim"))),
        ("ProjectsController.PostReflect", Lookup(t => Post($"/api/projects/{t.ProjectId}/reflect"))),
        ("ProjectsController.PutLanding", Lookup(t => Put($"/api/projects/{t.ProjectId}/landing", new { landing = (string?)null }))),
        ("ProjectsController.PutPlaybooksSource", Lookup(t => Put($"/api/projects/{t.ProjectId}/playbooks-source", new { repoSourced = false }))),
        ("ProjectsController.PutReflection", Lookup(t => Put($"/api/projects/{t.ProjectId}/reflection", new { cadenceDays = (int?)null }))),
        ("ProjectsController.PutRunnerTags", Lookup(t => Put($"/api/projects/{t.ProjectId}/runner-tags", new { tags = Array.Empty<string>() }))),
        ("ProjectsController.PutBindsTracker", Lookup(t => Put($"/api/projects/{t.ProjectId}/binds-tracker", new { bindsTracker = false }))),
        ("ProjectsController.PutStoriesOnEpics", Lookup(t => Put($"/api/projects/{t.ProjectId}/stories-on-epics", new { storiesOnEpics = false }))),
        ("ProjectsController.PutCheckApprovesMerge", Lookup(t => Put($"/api/projects/{t.ProjectId}/check-approves-merge", new { checkApprovesMerge = false }))),
        ("ProjectsController.PutRunnersWorkContributions", Lookup(t => Put($"/api/projects/{t.ProjectId}/runners-work-contributions", new { runnersWorkContributions = false }))),
        ("ProjectsController.PutFilesContributions", Lookup(t => Put($"/api/projects/{t.ProjectId}/files-contributions", new { filesContributions = false }))),
        ("ProjectsController.PutNoCodeCloses", Lookup(t => Put($"/api/projects/{t.ProjectId}/no-code-closes", new { noCodeCloses = false }))),
        ("ProjectsController.PutOnePrPolicy", Lookup(t => Put($"/api/projects/{t.ProjectId}/one-pr-policy", new { policy = "optional" }))),
        ("ProjectsController.PutPriority", Lookup(t => Put($"/api/projects/{t.ProjectId}/priority", new { priority = "normal" }))),
    ];

    // ---- The lists are held to the code ----

    /// <summary>
    /// Every action in the Hatch module, as <c>Controller.Action</c>, with
    /// whether a key may call it. The attribute is read as
    /// <c>AdminSurfaceTests</c> reads it: on the action, else on the controller.
    /// An action with none asks for no role at all, and the wall lets any
    /// person or key it has authenticated through - so it is a route a scoped
    /// caller reaches, and it is held to the matrix like the rest.
    /// </summary>
    private static IReadOnlyDictionary<string, bool> HatchActions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (Name: $"{t.Name}.{m.Name}", Method: m)))
            .Where(a => a.Method.DeclaringType!.Namespace == Namespace)
            .ToDictionary(a => a.Name, a => RequiredRole(a.Method)?.AcceptKey ?? true);

    private static RequireRoleAttribute? RequiredRole(MethodInfo action) =>
        action.GetCustomAttribute<RequireRoleAttribute>()
        ?? action.DeclaringType!.GetCustomAttribute<RequireRoleAttribute>();

    /// <summary>
    /// A new route fails the build until it is listed. The failure names it and
    /// says which list asks the question: does it name a project, or return
    /// something of one?
    /// </summary>
    [Fact]
    public void EveryHatchActionIsOnExactlyOneList()
    {
        var actual = HatchActions().Keys.ToHashSet();
        var bearing = ProjectBearing.Select(p => p.Name).ToList();
        var free = ProjectFree.Select(p => p.Name).ToList();
        var problems = new List<string>();

        foreach (var name in bearing.Concat(free).GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).Order())
            problems.Add($"{name} is listed more than once - a route is on one list, once");

        foreach (var name in actual.Except(bearing).Except(free).Order())
            problems.Add($"{name} is on neither list - does it name a project or return one's rows? Add a probe to ProjectBearing, or a reason to ProjectFree");

        foreach (var name in bearing.Intersect(free).Order())
            problems.Add($"{name} is on both lists - it either names a project or it does not");

        foreach (var name in bearing.Concat(free).Distinct().Except(actual).Order())
            problems.Add($"{name} is listed and is not a Hatch action - it was renamed or removed");

        foreach (var (name, reason) in ProjectFree)
            if (string.IsNullOrWhiteSpace(reason)) problems.Add($"{name} is project-free and says no reason");

        foreach (var (name, probe) in ProjectBearing)
        {
            if (probe.Builds.Length == 0) problems.Add($"{name} has no request to send");
            if (probe.Kind == Kind.Lookup && probe.Builds.Length != 1) problems.Add($"{name} is a lookup and a lookup has one request");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // ---- The matrix ----

    [SkippableTheory(Timeout = CaseTimeoutMs)]
    [InlineData("person", "none")]
    [InlineData("person", "some")]
    [InlineData("key", "none")]
    [InlineData("key", "some")]
    public async Task EveryProjectBearingRoute_ReadsAsAbsent_ToACallerWhoCannotSeeTheProject(string kind, string scope)
    {
        Skip.IfNot(
            HatchDatabase.Available,
            $"{HatchDatabase.Variable} is unset - the wall, the scope and the filters run on the real host over Postgres. Run `make test-api-db`.");

        var caller = $"{kind} {scope}";
        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), () => string.Join(Environment.NewLine, logs.Messages));

        await using var host = await WalledHost.StartAsync(deadline, logs);

        var owner = scope == PersonScopeMode.None ? host.None : host.Some;
        var isKey = kind == "key";
        using var client = isKey
            ? await host.AsKeyAsync(await host.MintKeyAsync($"matrix-{scope}", owner))
            : await host.AsPersonAsync(owner);

        var seesA = scope == PersonScopeMode.Some;
        var acceptsKey = HatchActions();
        var targets = TargetsFor(host);
        var failures = new List<string>();

        async Task<(HttpStatusCode Status, string Body)> SendAsync(string phase, Req req)
        {
            using var message = new HttpRequestMessage(req.Method, req.Path);
            if (req.Json is not null) message.Content = new StringContent(req.Json, Encoding.UTF8, "application/json");
            if (req.Raw is not null) message.Content = new ByteArrayContent(req.Raw);

            using var response = await deadline.BoundAsync(phase, () => client.SendAsync(message));
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        bool Refused(string name, Probe probe) =>
            probe.RefusesPerson ? !isKey : isKey && (!acceptsKey[name] || probe.RefusesKey);

        string Say(string name, Req req, HttpStatusCode status) => $"{caller}: {name} {req.Method} {req.Path} answered {(int)status}";

        var lookups = ProjectBearing.Where(p => p.Probe.Kind == Kind.Lookup).ToList();
        var lists = ProjectBearing.Where(p => p.Probe.Kind == Kind.List).ToList();
        var missing = new Dictionary<string, HttpStatusCode>();

        // Nonexistent and hidden first: a probe on A may change A, and nothing
        // here may be allowed to change what a later one is asked of.
        foreach (var (name, probe) in lookups)
        {
            var build = probe.Builds[0];
            var gone = build(targets[Who.Missing]);
            var (goneStatus, _) = await SendAsync($"{name} (nonexistent)", gone);
            missing[name] = goneStatus;

            // A caller scoped to nothing cannot see A either, so A is hidden
            // from them too and must read the same as B.
            var hiddenTargets = seesA ? new[] { Who.Hidden } : new[] { Who.Hidden, Who.Visible };
            foreach (var who in hiddenTargets)
            {
                var req = build(targets[who]);
                var (status, _) = await SendAsync($"{name} ({who})", req);

                if (Refused(name, probe))
                {
                    if (status != HttpStatusCode.Forbidden || goneStatus != HttpStatusCode.Forbidden)
                        failures.Add($"{Say(name, req, status)}, and {(int)goneStatus} for an id nothing has - this caller is refused before scope is read, so both should be 403");
                    continue;
                }

                if (status != goneStatus)
                    failures.Add($"{Say(name, req, status)} for the {who} project, but {(int)goneStatus} for one nothing has - they must read the same");
                else if ((int)status is >= 200 and < 300)
                    failures.Add($"{Say(name, req, status)} for the {who} project - a project the caller cannot see answered as though it were there");
                else if (status == HttpStatusCode.Forbidden)
                    failures.Add($"{Say(name, req, status)} for the {who} project - a 403 says the project exists");
            }
        }

        // Lists: nothing of B in the body, and for a caller scoped to nothing,
        // nothing of A either.
        foreach (var (name, probe) in lists)
        {
            for (var i = 0; i < probe.Builds.Length; i++)
            {
                // The unbound remote first: a write is held to what it answers.
                (HttpStatusCode Status, string Body)? unbound = null;
                if (probe.LikeUnbound)
                    unbound = await SendAsync($"{name} (unbound)", probe.Builds[i](targets[Who.Missing]));

                var req = probe.Builds[i](targets[Who.Hidden]);
                var (status, body) = await SendAsync($"{name} (list {i})", req);

                if (unbound is { } gone)
                {
                    if ((int)gone.Status >= 500) failures.Add($"{caller}: {name} answered {(int)gone.Status} for a remote nothing binds - a server error");
                    if (status != gone.Status)
                        failures.Add($"{Say(name, req, status)} for the hidden project's remote, but {(int)gone.Status} for one nothing binds - they must read the same");
                    else
                    {
                        var hiddenShape = BodyShape(body);
                        var unboundShape = BodyShape(gone.Body);
                        if (!hiddenShape.SequenceEqual(unboundShape))
                            failures.Add($"{Say(name, req, status)} for the hidden project's remote, with a body shaped [{string.Join(", ", hiddenShape)}] where an unbound remote's is [{string.Join(", ", unboundShape)}]");
                    }
                }

                if ((int)status >= 500) failures.Add($"{Say(name, req, status)} - a list for a scoped caller is a server error");
                if (body.Contains(WalledHost.HiddenMarker, StringComparison.Ordinal))
                    failures.Add($"{Say(name, req, status)} and its body holds {WalledHost.HiddenMarker}");
                if (body.Contains("BBB", StringComparison.Ordinal))
                    failures.Add($"{Say(name, req, status)} and its body holds the hidden project's key");
                if (!seesA && body.Contains(WalledHost.VisibleMarker, StringComparison.Ordinal))
                    failures.Add($"{Say(name, req, status)} and its body holds {WalledHost.VisibleMarker}, which this caller's scope does not hold");

                if (i == 0 && probe.ListsText && seesA && !Refused(name, probe) && !body.Contains(WalledHost.VisibleMarker, StringComparison.Ordinal))
                    failures.Add($"{Say(name, req, status)} and its body does not hold {WalledHost.VisibleMarker} - the route lists issue text and this caller sees project A, so the list is empty or the seed is not where it reads");
            }
        }

        // The visible twins, for a caller who can see A: the same request at A
        // must not read as a project nothing has. Deletes last.
        if (seesA)
        {
            foreach (var (name, probe) in lookups.OrderBy(p => p.Probe.Last).ThenBy(p => p.Probe.Destructive))
            {
                var req = probe.Builds[0](targets[Who.Visible]);
                var (status, _) = await SendAsync($"{name} (visible)", req);

                if (Refused(name, probe))
                {
                    if (status != HttpStatusCode.Forbidden)
                        failures.Add($"{Say(name, req, status)} for the visible project - this caller is refused before scope is read, so it should be 403");
                    continue;
                }

                if (!probe.NoTwin && status == missing[name])
                    failures.Add($"{Say(name, req, status)} for the visible project, the same as for one nothing has - the probe's path or body does not reach the route, so it proves nothing");
            }

            // The two the ticket names outright. A write into B is a 404 and into
            // A is a 201, for a person; and a stall on B's issue is a 404 for either.
            var stall = Put($"/api/issues/{targets[Who.Hidden].Key}/stall", new { why = "x" });
            var (stallStatus, _) = await SendAsync("stalling the hidden issue", stall);
            if (stallStatus != HttpStatusCode.NotFound)
                failures.Add($"{Say("IssueStallController.PutStall", stall, stallStatus)} for the hidden issue, expected 404");

            if (!isKey)
            {
                var intoHidden = Post("/api/issues", new { projectId = host.Seed.ProjectB, type = "story", title = "into the hidden project" });
                var (hiddenStatus, _) = await SendAsync("creating an issue in the hidden project", intoHidden);
                if (hiddenStatus != HttpStatusCode.NotFound)
                    failures.Add($"{Say("IssuesController.CreateIssue", intoHidden, hiddenStatus)} into the hidden project, expected 404");

                var intoVisible = Post("/api/issues", new { projectId = host.Seed.ProjectA, type = "story", title = "into the visible project" });
                var (visibleStatus, _) = await SendAsync("creating an issue in the visible project", intoVisible);
                if (visibleStatus != HttpStatusCode.Created)
                    failures.Add($"{Say("IssuesController.CreateIssue", intoVisible, visibleStatus)} into the visible project, expected 201");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The two controls: an unbounded caller reads B's issue, as a person and as
    /// a key that has no owner. Without them every 404 above could be a seed
    /// nobody can reach, and "unowned means unbounded" would be a claim and not
    /// a fact.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AnUnboundedCaller_ReadsTheHiddenProjectsIssue()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset. Run `make test-api-db`.");

        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), () => string.Join(Environment.NewLine, logs.Messages));

        await using var host = await WalledHost.StartAsync(deadline, logs);
        var hidden = TargetsFor(host)[Who.Hidden];

        using var person = await host.AsPersonAsync(host.All);
        using var unowned = await host.AsKeyAsync(await host.MintKeyAsync("matrix-unowned", owner: null));

        foreach (var (label, client) in new[] { ("a person scoped to all", person), ("a key with no owner", unowned) })
        {
            using var response = await deadline.BoundAsync(label, () => client.GetAsync($"/api/issues/{hidden.Key}"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{label}: GET /api/issues/{hidden.Key} answered {(int)response.StatusCode}");
            Assert.Contains(WalledHost.HiddenMarker, body);
        }
    }

    /// <summary>B's, A's and one nothing has, built from the seed.</summary>
    private static Dictionary<Who, Target> TargetsFor(WalledHost host)
    {
        var ids = host.Ids;
        var seed = host.Seed;

        return new Dictionary<Who, Target>
        {
            [Who.Hidden] = new("BBB", seed.ProjectB, seed.DreamB, ids.PlaybookB, ids.ProposalsB, ids.TrunkBuildB, ids.FindingB, ScopeSeed.CanonicalB, ids.Todo, ids.Done, host.Some.Id),
            [Who.Visible] = new("AAA", seed.ProjectA, seed.DreamA, ids.PlaybookA, ids.ProposalsA, ids.TrunkBuildA, ids.FindingA, ScopeSeed.CanonicalA, ids.Todo, ids.Done, host.Some.Id),
            [Who.Missing] = new("ZZZ", 999_999, 999_999, 999_999, [999_999], 999_999, 999_999, "host/zzz", ids.Todo, ids.Done, host.Some.Id),
        };
    }
}

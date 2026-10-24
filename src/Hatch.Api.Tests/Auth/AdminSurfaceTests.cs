using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// The audit, made executable. Which role each verb asks for is a
/// judgement about this house rather than a rule a compiler can derive, so it
/// is written down once - here - and the test is that the code agrees with it.
///
/// It exists because the failure mode is silence. A new endpoint added to
/// DevicesController next year is unguarded by default, which is the correct
/// default (see RequireRoleAttribute on why this is opt-in) and also the
/// reason nobody would notice. This turns "I did not think about it" into a
/// red build with the action's name in it, and the fix is to think about it and
/// then edit one of the two lists below.
///
/// The shape of the judgement, for whoever is editing those lists:
///
/// - **Shaping the house is guarded. Operating it is not.** Creating a zone,
///   importing a device, wiring a panel - guarded. Turning a lamp on, nudging a
///   thermostat, triggering a routine, playing music - open, and it has to
///   stay open, because the dashboard runs on a hallway tablet nobody signs in
///   to and the whole promise is that the family never has to.
/// - **Reads stay open, with two exceptions.** Both exceptions are inventories
///   of credentials rather than facts about the house: the session list
///   (AuthController.ListGrants, PeopleController.GetSessions) and the
///   connection settings (SettingsController, DevicesController's camera
///   connection).
/// - **The family modules are not in this file at all.** Quill, Gather,
///   Storage and Game are the household's own apps; their authorization is
///   ownership, expressed as a WHERE clause in the module
///   (docs/auth-architecture.md, "A person is an authorization input"), and a
///   global role has nothing to say about them.
/// - **Two levels, and the level is not a judgement per action.** Everything
///   under Modules/Hatch asks for User, with `AcceptScope = "hatch"` except the
///   person-only routes named in <c>PersonOnly</c>; everything else that is
///   guarded asks for Admin, except the People reads, which ask for User so
///   that a Pending person - who is let into nothing - sees no names either.
///   Two tests below hold the code to that, so a new controller cannot land
///   at the wrong level unnoticed.
/// - **Hatch is the exception, and it is not really one.** It lives under
///   Modules/ for the schema and the migration history, but it is not a family
///   app - it is the operator's own tooling wearing a module's clothes, like
///   the admin app is the operator's own screen. Every one of its verbs is
///   guarded, reads included, and its bundle 404s for non-admins the same way
///   (AdminAppMiddleware). Scoped API keys widen that (docs/hatch.md, "One
///   gate, two lanes"): a second credential through the same gate rather than
///   a second gate.
/// </summary>
public class AdminSurfaceTests
{
    /// <summary>
    /// Every action that asks for a role, as "Controller.Action".
    /// Adding one here without adding the attribute fails, and the reverse
    /// fails too - the list is the audit, not a subset of it.
    /// </summary>
    private static readonly string[] Guarded =
    [
        // Sessions: minting a credential, revoking one, and the inventory of
        // every credential in the household. Redeem, me and sign-out stay open
        // - they are how a device becomes anyone at all.
        "AuthController.ListGrants",
        "AuthController.RevokeGrant",
        "AuthController.LinkGrantPerson",
        "AuthController.CreateInvite",

        // API keys, and the one entry in this file with a second reason. They
        // are credentials, so they belong beside the sessions above - and they
        // carry no AcceptScope, so a key cannot mint a key. Allowing that would
        // make the scope system decorative, since any key could issue itself a
        // second one carrying whatever it liked.
        "ApiKeysController.ListKeys",
        "ApiKeysController.ListScopes",
        "ApiKeysController.CreateKey",
        "ApiKeysController.RevokeKey",
        "ApiKeysController.SetOwner",

        // People. The writes above all, because Role is set here: an
        // unguarded PUT would let any enrolled device promote itself, which
        // makes the whole boundary a formality. The reads ask for User only -
        // the family apps render a name and a photo - and sit at the end.
        "PeopleController.Create",
        "PeopleController.Update",
        "PeopleController.Delete",
        "PeopleController.PutPhoto",
        "PeopleController.DeletePhoto",
        "PeopleController.GetSessions",
        "PeopleController.GetAll",
        "PeopleController.Get",
        "PeopleController.GetPhoto",

        // The domain model: zones, devices, channels, panels, routines. Every
        // GET on all five is open.
        "ZonesController.Create",
        "ZonesController.Update",
        "ZonesController.Delete",
        "ZonesController.PutComfort",
        "DevicesController.Create",
        "DevicesController.Update",
        "DevicesController.Delete",
        "DevicesController.AddChannel",
        "DevicesController.UpdateChannel",
        "DevicesController.DeleteChannel",
        "DevicesController.RefreshOptions",
        "DevicesController.Backfill",
        "PanelsController.Create",
        "PanelsController.Update",
        "PanelsController.Delete",
        "RoutinesController.Create",
        "RoutinesController.Update",
        "RoutinesController.Delete",

        // Camera connection: a host, a port, a path and a username - a route
        // straight to the camera that goes around Hatch entirely. Watching the
        // feed is a different question, and CameraController stays open.
        "DevicesController.GetCameraConnection",
        "DevicesController.UpsertCameraConnection",
        "DevicesController.DeleteCameraConnection",

        // Integrations. The OAuth callback is deliberately absent: nobody
        // arrives there by choosing to, and the single-use state row is a
        // stronger claim than a session.
        "CalendarOAuthController.Start",
        "CalendarsController.RefreshCalendars",
        "CalendarsController.Sync",
        "CalendarsController.UpdateCalendar",
        "CalendarsController.DeleteAccount",
        "HomeAssistantController.FetchFromHomeAssistant",
        "DiscoveryController.GetUnmapped",
        "PhotosController.RefreshAlbums",
        "PhotosController.UpdateAlbum",

        // Hatch, whole. Reads included, because the board is a list of what
        // the operator is doing and every card title on it - not a fact about
        // the house that a hallway tablet has any business rendering.
        "BoardController.GetBoard",
        "ProjectsController.GetProjects",
        "ProjectsController.CreateProject",
        "ProjectsController.PatchProject",
        "ProjectsController.DeleteProject",
        "ProjectsController.GetRepositories",
        "ProjectsController.GetLogo",
        "ProjectsController.GetMembers",
        // A pasted image: read by a key named in a brief, posted by a person.
        "ImagesController.GetImage",
        "ImagesController.PostImage",
        "StatusesController.GetStatuses",
        "StatusesController.CreateStatus",
        "StatusesController.PatchStatus",
        "StatusesController.DeleteStatus",

        // Which columns are in the WIP section, and how much of one slice of
        // the board may sit across them at once, cut the same way as the
        // runner bound below: reading is Hatch-scoped - an agent stalled
        // behind a full section is entitled to know why - and the write names
        // no scope, because a key that could raise the limit or empty the
        // section could pull more of its own work in overnight. See
        // WipController.
        "WipController.GetWip",
        "WipController.PutWip",
        "IssuesController.GetIssue",
        "IssuesController.SearchIssues",
        "IssuesController.CreateIssue",
        "IssuesController.BulkEdit",
        "IssuesController.PatchIssue",
        "IssuesController.DeleteIssue",
        "IssuesController.MoveIssue",
        "IssueThreadController.GetComments",
        "IssueThreadController.AddComment",
        "IssueThreadController.GetEvents",

        // The other end of a message written by AddComment: a session, or the
        // runner spawning one, marking what it has put in front of the model.
        // Hatch-scoped for the reason AddComment is - the key a spawned agent
        // inherits is the one that reads and delivers.
        "IssueMessagesController.Deliver",

        // Hatch-scoped like the rest of the module, including the answering.
        // A key is what `hatch.sh answer` types with, and a key is also what a
        // spawned agent inherits - the server cannot tell those apart, so it
        // does not pretend to. What keeps an agent from answering itself out of
        // a block is that the dispatch is refused while a question is open, and
        // the dispatch is a command the operator types. See
        // IssueThreadController.AddComment.
        "QuestionsController.GetQuestions",
        "QuestionsController.GetIssueQuestions",

        // The same two facts as GetQuestions and GetBoard, read together for
        // the nav strip: what is up for review, and what is waiting on an
        // answer. Hatch-scoped like both of the reads it stands in for, and it
        // widens neither - a key that could not read the board could not read
        // this, and it says strictly less than the board does.
        "AttentionController.GetAttention",
        "ImportController.Preview",
        "ImportController.PreviewText",
        "ImportController.Import",
        "WorkController.GetNextWork",
        "WorkController.GetWork",

        // The loop's own write, on the loop's own say-so: it carries an
        // express issue past a column marked ExpressSkips with no session. It
        // inherits the class's Hatch scope rather than a person-only route,
        // because a key is exactly who calls a hop - see
        // IssueExpressController and StatusesController.PutExpressSkips for
        // the person-only edge that decides whether a hop exists at all.
        "WorkController.HopWork",

        // The runner's own write, same as the hop above: applies the verdict a
        // check session recorded under its own claim token, with no session
        // dispatched. Hatch-scoped like the hop, for the same reason - a key is
        // exactly who calls this once a check has run.
        "WorkController.VerdictWork",

        // The one path that may move an issue into a terminal column without
        // the operator pressing it there themselves - but only because a
        // merged pull request already is that decision, made on the forge and
        // reported back. It refuses everything a hop refuses (wrong column, a
        // claim) plus two of its own (no pull request recorded, or one that
        // does not match), so a key can carry a ticket no further than the
        // operator already agreed it may go.
        "WorkController.MergedWork",

        // The review column's other direction: a merge column judged
        // "pushback" by MergeWork itself, carried back for a person - or the
        // next session - to fix. Hatch-scoped like MergedWork beside it: a
        // runner's poll is exactly who reads this verdict.
        "WorkController.PushbackWork",

        // MergedWork's trunk-landing counterpart: a landing judged "land" by
        // LandingWork itself, carried to the terminal column after review.
        // Hatch-scoped like its neighbours: a runner's poll is exactly who
        // calls it, and it refuses everything a hop refuses plus the judge.
        "WorkController.LandedWork",

        // The merge column's own counterpart to MergedWork above: a pull
        // request read as closed rather than merged, for the same board
        // row and the same reason a key may report it - a runner's poll
        // reads the forge, not a person. It moves nothing and takes no
        // claim (see the action's own remark), so there is nothing here
        // for a claimed issue to be refused on.
        "WorkController.ClosedWork",

        // The same walk as GetNextWork, reported instead of acted on. A read,
        // and one a key already holds every part of: it says nothing about the
        // board that `next` and `/issues` do not already say, only in one
        // answer instead of a hundred.
        "WorkController.GetQueue",

        // The review column's branches and what the board holds about each, for
        // a runner's poll. A read, and one that says strictly less than the
        // issues do: the verdicts already ride IssueDto.
        "WorkController.GetReview",

        // The read a meter is drawn from, Hatch-scoped like the rest of the
        // module: it says how far along a subtree is, which is exactly what a
        // key holder asking "what is left under this epic" is entitled to.
        "PlanController.GetIssuePlan",
        "PlanController.GetPlan",

        // When the work happened, read off the status history: the epics active
        // in a range and the stretches beneath them. Hatch-scoped and open to a
        // person like WorkLogController's reads - it says what the trail
        // already says, folded into one answer.
        "ReportsController.GetTimeline",

        // How many issues sat in each board column over time, read off the
        // status history; Hatch-scoped and open to a person, like GetTimeline.
        "ReportsController.GetCumulativeFlow",

        // How work moved through the board over a range - counts and
        // durations per column, read off the status history, the questions and
        // the work log; Hatch-scoped and open to a person, like GetTimeline.
        "ReportsController.GetFlow",

        // My own Claude headroom, off my own runners' heartbeats. Hatch-scoped
        // like the rest of the module and guarded for the same reason the
        // board is: what it says is how much room is left to work tonight,
        // which is a fact about the caller rather than about the house - and
        // it already narrows to the caller's own runners, the same as `--mine`.
        "UtilizationController.Get",

        // The build on a repository's trunk, Hatch-scoped for the reason
        // BuildCheckController is: a key may read and write it, the same as a
        // branch's build check - it is a fact about a sha that any runner reads
        // the same way. Filing the bug it can surface is Hatch-scoped too, now
        // that the choice a key makes there is gated per project rather than
        // cut outright - see TrunkBuildBugController's own header for why.
        "TrunkBuildController.GetTrunkBuilds",
        "TrunkBuildController.PutTrunkBuild",
        "TrunkBuildBugController.PostBug",

        // What .hatch/playbooks.yaml parsed to on a repository's trunk,
        // Hatch-scoped and gated the same as TrunkBuildController: a key
        // reads it the same way a runner's poll does, and a runner's poll
        // writes it the same way. See PlaybookFile.
        "PlaybookFileController.GetPlaybookFiles",
        "PlaybookFileController.PutPlaybookFile",

        // Who is at the browser, so the nav strip can say so and know the
        // role. Guarded like the rest of the module, and Hatch-scoped for
        // consistency: a key reads as nobody and gets a 204.
        "MeController.GetMe",

        // What each ticket cost, one row per agent session. Hatch-scoped like
        // the rest of the module - and the write is cut a third way, tighter
        // than either of the two below: it refuses a person outright, in the
        // action, because the only honest writer of a meter reading is the
        // dispatcher that read the meter. That check cannot live in the
        // attribute, which is dormant wherever the wall is off. See
        // IssueWorkLogController.NotAKey.
        "IssueWorkLogController.GetWorkLog",
        "IssueWorkLogController.PostEntry",

        // The same log read across issues, for the leaderboard. Hatch-scoped
        // like the module's other reads and, deliberately unlike the write above
        // it, open to a person: reading what the nights cost is the whole point
        // of the page.
        "WorkLogController.GetByPlaybook",
        "WorkLogController.GetHistory",
        "WorkLogController.GetSessions",

        // The event trail across every issue rather than about one - the
        // module's other cross-issue read, Hatch-scoped and open to a person
        // the same way WorkLogController's reads are: it says nothing about an
        // issue its own thread would refuse to.
        "ActivityController.GetActivity",

        // The search across every issue, its comments and its work log -
        // another cross-issue read, Hatch-scoped and open to a person like
        // IssuesController.SearchIssues, whose cards it returns.
        "SearchController.GetSearch",

        // What landed in the newest night, across every issue rather than
        // about one - Hatch-scoped and open to a person the same way
        // WorkLogController's and ActivityController's reads are. See
        // OvernightController.
        "OvernightController.GetOvernight",

        // Marking the night as seen is a person's own mark, never a key's -
        // see OvernightController.PostSeen.
        "OvernightController.PostSeen",

        // What shipped, a window at a time - Hatch-scoped and open to a
        // person the same way OvernightController's read is. See
        // ChangelogController.
        "ChangelogController.GetChangelog",

        // Playbooks are guarded twice over. Reading one is Hatch-scoped like
        // the rest; writing one names no scope at all, so an API key is
        // refused - a playbook chooses the next agent's instructions, its
        // model and its budget, and an agent that could edit one could widen
        // its own. See PlaybooksController.
        "PlaybooksController.GetPlaybooks",
        "PlaybooksController.CreatePlaybook",
        "PlaybooksController.PatchPlaybook",
        "PlaybooksController.DeletePlaybook",

        // The two verbs that say one issue waits on another, and deliberately
        // *unlike* the playbook and the override below: an edge is a statement
        // about the work, not about an agent's budget, so a planning session
        // that has just filed five stories can chain them itself. Hatch-scoped
        // like the rest of the module. See IssueDependenciesController.
        "IssueDependenciesController.AddDependency",
        "IssueDependenciesController.RemoveDependency",

        // The lease on an issue, and the one place in the module where the
        // scope is accepted *because* the caller is a machine: a mutex only an
        // operator could operate would mutex nothing. All three carry the
        // ordinary guard.
        //
        // The one narrowing is not expressible here. A DELETE with no token is
        // the operator prising a ticket off whoever holds it, and that lane
        // alone refuses an API key - checked in the action, because RoleGate
        // is dormant wherever the wall is off. See
        // IssueClaimController.NotAPerson.
        "IssueClaimController.TakeClaim",
        "IssueClaimController.Heartbeat",
        "IssueClaimController.ReleaseClaim",

        // Grace: the session a dispatcher spawned asking its own live claim
        // not to be called quiet while a slow command runs. A key's because
        // the caller is the session and holds no token. It cannot revive a
        // claim, is capped by Hatch:StallGraceMaxMinutes, and touches the
        // quiet rule alone - the heartbeat TTL is still judged. See
        // IssueClaimController.Grace.
        "IssueClaimController.Grace",

        // A runner's verdict on whether an issue's branch merges with the
        // trunk, and Hatch-scoped for the reason the claim is: the caller is a
        // machine, and a verdict only an operator could enter would be one
        // nobody ever entered. It is a fact about two refs that every runner
        // computes the same way, not a statement about an agent's budget; a
        // wrong one costs a stall, which is a question. See MergeCheckController.
        "MergeCheckController.PutMergeCheck",

        // What a repository holds for an issue's branch, cut the same way as
        // the verdict above: a fact about a sha any caller computes the same
        // way, not a statement about an agent's budget. See LandingController.
        "LandingController.PutLanding",

        // The same for the build on the branch's tip: a fact about a sha that
        // every runner reads the same way, and a wrong one costs a stall,
        // which is a question. See BuildCheckController.
        "BuildCheckController.PutBuildCheck",

        // What the forge says of an issue's recorded pull request, cut the same
        // way: a fact any runner reads the same way from the forge, and a wrong
        // one stalls nothing, since nothing acts on it yet. See
        // PullRequestStateController.
        "PullRequestStateController.PutPullRequestState",

        // The merge train saying it just brought a branch current, cut the same
        // way: a runner reports an update it performed, and a wrong one only
        // moves which row the train's head is on. See BroughtCurrentController.
        "BroughtCurrentController.PutBroughtCurrent",

        // What a check session judged about an issue - pass or fail - and
        // Hatch-scoped for the same reason the two verdicts above are: the
        // caller is whatever ran the check, and a verdict only an operator
        // could enter would be one an agent session could never record for
        // itself. See VerdictsController.
        "VerdictsController.PostVerdict",

        // A playbook's power routed through a different table, and cut the
        // same way: an issue's model and effort override every playbook that
        // could speak for it, so an agent that could set one could raise its
        // own budget. Reading it is open - it rides IssueDto, which is
        // Hatch-scoped - and only the write is here. See
        // IssuePlaybookController.
        "IssuePlaybookController.PatchIssuePlaybook",

        // How many of an epic's stories may be in progress at once, cut the
        // same way as the playbook override just above: a key that could
        // raise its own epic's ceiling could pull more of its own stories
        // into progress at once. Reading rides IssueDto, open like the rest.
        // See IssueWipLimitController.
        "IssueWipLimitController.PatchIssueWipLimit",

        // The runners, and the same split drawn a third time. The heartbeat and
        // the read are a dispatcher's - a loop that could not say it was alive
        // would leave a control surface that could only ever be empty - and the
        // PATCH beside them is the operator's, because every field it writes is
        // a bound on how much a runner may spend and how long it may run. An
        // agent that could raise its own --max-spend could raise its own
        // budget. See RunnersController.
        "RunnersController.GetRunnerNames",
        "RunnersController.GetRunners",
        "RunnersController.Heartbeat",
        "RunnersController.PatchRunner",
        "RunnersController.PostRunnerOrder",
        "RunnersController.DeleteRunnerOrder",

        // A related edge, cut the same way and for a different reason: under
        // the loop's "people only" rule an assignee is a dispatch gate, so a
        // key that could write one could clear a person's name off a ticket and
        // hand itself work that was reserved. The directory read beside it is
        // Hatch-scoped - it is a list of names, and an agent has to be able to
        // say whose ticket it is declining to take. See AssigneeController.
        "AssigneeController.GetAssignees",
        "AssigneeController.PutIssueAssignee",

        // The same edge read from the other side. An assignee holds a ticket
        // off the night shift; priority puts one at the front of it, so a key
        // that could set one could put its own ticket ahead of everything a
        // person filed, every night, with nothing looking wrong on the board.
        // Reading it is open - it rides IssueDto and IssueCardDto, both
        // Hatch-scoped - and only the write is here. See
        // IssueExpediteController, which cuts both its routes the same way.
        "IssueExpediteController.PutIssuePriority",
        "IssueExpediteController.PutIssueExpedite",

        // The night train's own edge, now three levels instead of a flag: none,
        // express or preapproved (HA-470). Express still decides which gates the
        // loop may pass unattended; preapproved is the approve press given in
        // advance - a key that could set either could carry its own ticket
        // through the night unattended, or approve it in advance. Setting express
        // is gated the way a move into the column that feeds implementation is
        // (IProjectAccess.CanMoveIntoAsync); setting preapproved, on either side
        // of the change, needs an approver (IProjectAccess.CanApproveAsync).
        // Reading it is open, the same as expedite. See IssuePreApprovalController.
        "IssuePreApprovalController.PutIssueExpress",

        // The level by name, same two gates as above - HA-470.
        "IssuePreApprovalController.PutIssuePreApproval",

        // A story's own say over whether its tasks share one branch and one
        // pull request. Unlike its neighbours it is open to a key - it decides
        // how a story is branched, not what the loop reaches for or passes - so
        // it is here and not in PersonOnly. The project's default, which changes
        // how every story is worked, is an owner's: ProjectsController.PutLanding.
        "IssueOnePrController.PutIssueOnePr",

        // Which runners may implement an issue's own work - a statement about
        // the ticket, not a power over the board, open to a key for the same
        // reason one-pr is. IssueRunnerTagsController.
        "IssueRunnerTagsController.PutIssueRunnerTags",

        // A release note, open to a key only under the issue's live claim: the
        // claim check is in the action, not the attribute, so a key can write the
        // note of the ticket it is working and no other. IssueReleaseNoteController.
        "IssueReleaseNoteController.PutIssueReleaseNote",

        // Holding and resuming a ticket - an agent that could hold its own
        // ticket could park it off the board for the night. Stalling is the
        // other write IssueStallController makes, and it is open to a key on
        // purpose: a dispatcher is exactly who leaves that mark, so it carries
        // no attribute at all and never appears here.
        "IssueStallController.PutHold",

        // The column half of the same edge: which columns an express issue is
        // carried past with no session. Reading it is open - it rides
        // StatusDto, which is Hatch-scoped - and only the write is here. See
        // StatusesController.PutExpressSkips.
        "StatusesController.PutExpressSkips",

        // The pull half of the same kind of edge: whether a child standing in
        // a column is carried on to the next one with no session while its
        // parent stands in the implementation column. Reading it is open, the
        // same as ExpressSkips - only the write is here. See
        // StatusesController.PutParentPulls.
        "StatusesController.PutParentPulls",

        // Where an agent's own issues are born. Reading it is open, the same
        // as ExpressSkips and ParentPulls - only the write is here. This one
        // decides a gate the loop must not be able to move for itself: the
        // column its own filing lands in. See StatusesController.PutAgentFiles.
        "StatusesController.PutAgentFiles",

        // The column where code gets written. Reading it is open, the same as
        // ExpressSkips, ParentPulls and AgentFiles - only the write is here.
        // It decides which move a dependency gates, which the loop must not be
        // able to move for itself. See StatusesController.PutImplementation.
        "StatusesController.PutImplementation",

        // Who may move work into this column - an owner or approver of the
        // issue's project, never a key, never a hop, never a dispatch
        // (HA-293's gate). Reading it is open, the same as the flags above -
        // only the write is here. See StatusesController.PutProtected.
        "StatusesController.PutProtected",

        // What the loop does once it's there: whether the runner's poll
        // merges an issue's pull request once it stands here, current and
        // green, with no session (HA-289's epic). Reading it is open, the
        // same as the flags above - only the write is here. See
        // StatusesController.PutMergesPullRequest.
        "StatusesController.PutMergesPullRequest",

        // A column's own runner tags - the operator's to set, same as
        // Protected and MergesPullRequest, since a column has no owner to
        // check instead. Reading it is open, the same as the flags above -
        // only the write is here. See StatusesController.PutRunnerTags.
        "StatusesController.PutRunnerTags",

        // The ordered list of remotes a project is bound to, cut the same
        // way: a runner that could bind one could point every runner on the
        // board at a repository nobody chose. The read beside it
        // (GetRepositories, above with the rest of ProjectsController) is
        // Hatch-scoped - it is exactly what a dispatch needs to know where to
        // check out. See ProjectsController.
        "ProjectsController.PutRepositories",
        "ProjectsController.PutLogo",
        "ProjectsController.DeleteLogo",
        "ProjectsController.PutMember",
        "ProjectsController.DeleteMember",
        "ProjectsController.ClaimProject",
        "ProjectsController.PutLanding",
        "ProjectsController.PostReflect",
        "ProjectsController.PutPlaybooksSource",
        "ProjectsController.PutReflection",
        "ProjectsController.PutRunnerTags",
        "ProjectsController.PutBindsTracker",
        "ProjectsController.PutClaimLimits",

        // The whole controller, reads included. See
        // Hatch.Api.Controllers.SettingsController.
        "SettingsController.GetAll",
        "SettingsController.Get",
        "SettingsController.Upsert",
        "SettingsController.Delete",

        // Hatch's own two settings - the Claude subscription token and what to
        // call whoever is sitting at this machine. Cut tighter than the module
        // around it: plain [RequireAdmin], no Hatch scope, so a key is refused
        // the read as well as the write. A key that could write here could set
        // the name every event in the house is signed with, or swap the
        // credential the account's headroom is read through. See
        // Hatch.Api.Modules.Hatch.SettingsController - a different class that
        // shares the four names above it, which is why its actions are named
        // for what they answer rather than Get and Put.
        "SettingsController.GetHatchSettings",
        "SettingsController.PutHatchSettings",

        // The one route in that class cut the other way, and the only endpoint
        // in Hatch that hands a live secret back out. It is here because it is
        // still guarded - a household member who is not an administrator is
        // refused - but it carries AcceptScope = hatch, unlike its two
        // siblings, because its ordinary caller is a keyless container runner
        // that has to authenticate a claude CLI it starts itself. What keeps
        // that from being a widening is a second gate the attribute cannot
        // express: the action refuses everyone, key or person, wherever the
        // wall is up. See Hatch.Api.Modules.Hatch.SettingsController.
        // GetCredential answers the same for any supported agent, and
        // GetClaudeToken is the older route kept for an older container: both
        // are cut the same way.
        "SettingsController.GetCredential",
        "SettingsController.GetClaudeToken",

        // The runner the image hands out, cut the same way as the settings
        // above it: plain [RequireAdmin], no Hatch scope. Not because a binary
        // is a credential - it is the same program anybody may build from this
        // repository - but because the audience is a person at a browser
        // setting a machine up, and nobody's dispatcher has a reason to
        // download the program it is already running as. See RunnerController.
        "RunnerController.Get",
        "RunnerController.Download",
    ];

    /// <summary>
    /// Actions that stay open even though a passing glance would guard them,
    /// each with the reason. These are not exempt from the audit - they are its
    /// most considered entries, and the test asserts they are still open so
    /// that guarding one is a deliberate edit rather than a reflex.
    /// </summary>
    private static readonly string[] DeliberatelyOpen =
    [
        // Control, not configuration. The tablet in the hallway does all of
        // these and nobody has ever signed in to it.
        "DevicesController.SetPower",
        "DevicesController.SetSetpoint",
        "DevicesController.SetMode",
        "DevicesController.TriggerScene",
        "DevicesController.PlayMedia",
        "PanelsController.SetPower",
        "PanelsController.SetSetpoint",
        "RoutinesController.Trigger",
        "RoutinesController.TurnOff",

        // How a device becomes anyone at all. Gating these is the infinite
        // redirect loop the wall's own allow-list exists to avoid.
        "AuthController.Verify",
        "AuthController.Redeem",
        // Me and sign-out ask the wall for a credential and nothing more, on
        // purpose: a Pending person has to be able to read who they are and to
        // sign out, and both work off the grant alone.
        "AuthController.Me",
        "AuthController.SignOutDevice",

        // Where Google sends the browser back. Guarded by the single-use state
        // row, which is a stronger claim than "an admin is holding this tab".
        "CalendarOAuthController.Callback",
    ];

    [Fact]
    public void TheGuardedSurfaceIsExactlyWhatWasAudited()
    {
        var actual = ActionsWhere(guarded: true);

        // Set comparison rather than sequence: the lists above are grouped for
        // a reader, not sorted for a machine.
        Assert.Equal(Guarded.OrderBy(a => a), actual.OrderBy(a => a));
    }

    /// <summary>
    /// The routes under Modules/Hatch that name no scope, so a key is refused
    /// them: a playbook, an override, a runner bound, an assignee, a priority,
    /// the settings and the runner binary each choose what the next agent may
    /// do or spend. Each still asks for User - a person, not an Admin.
    /// </summary>
    private static readonly string[] PersonOnly =
    [
        "PlaybooksController.CreatePlaybook",
        "PlaybooksController.PatchPlaybook",
        "PlaybooksController.DeletePlaybook",
        "IssuePlaybookController.PatchIssuePlaybook",
        "IssueWipLimitController.PatchIssueWipLimit",
        "RunnersController.PatchRunner",
        "RunnersController.PostRunnerOrder",
        "RunnersController.DeleteRunnerOrder",

        // Marking the night as seen is a person's own mark, never a key's or
        // a keyless runner's. See OvernightController.PostSeen.
        "OvernightController.PostSeen",
        "WipController.PutWip",
        "AssigneeController.PutIssueAssignee",
        "IssueExpediteController.PutIssuePriority",
        "IssueExpediteController.PutIssueExpedite",

        // The night train's edge, now three levels instead of a flag - HA-470.
        "IssuePreApprovalController.PutIssueExpress",
        "IssuePreApprovalController.PutIssuePreApproval",
        "IssueStallController.PutHold",
        "StatusesController.PutExpressSkips",
        "StatusesController.PutParentPulls",

        // Where an agent's own issues are born, which the loop must not be
        // able to move for itself.
        "StatusesController.PutAgentFiles",

        // The column where code gets written, which the loop must not be able
        // to move for itself.
        "StatusesController.PutImplementation",

        // Who may move work into this column - HA-293's gate.
        "StatusesController.PutProtected",

        // What the loop does once it's there - HA-289's epic.
        "StatusesController.PutMergesPullRequest",

        // A column's own runner tags - the operator's to set, a column
        // having no owner to check instead.
        "StatusesController.PutRunnerTags",
        "ProjectsController.PutRepositories",
        "ProjectsController.PutLogo",
        "ProjectsController.DeleteLogo",
        "ProjectsController.PutMember",
        "ProjectsController.DeleteMember",
        "ProjectsController.ClaimProject",
        "ProjectsController.PutLanding",
        "ProjectsController.PostReflect",
        "ProjectsController.PutPlaybooksSource",
        "ProjectsController.PutReflection",
        "ProjectsController.PutRunnerTags",
        "ProjectsController.PutBindsTracker",
        "ProjectsController.PutClaimLimits",
        "ImagesController.PostImage",
        "SettingsController.GetHatchSettings",
        "SettingsController.PutHatchSettings",
        "RunnerController.Get",
        "RunnerController.Download",
    ];

    private static readonly string[] UserOutsideHatch =
    [
        "PeopleController.GetAll",
        "PeopleController.Get",
        "PeopleController.GetPhoto",
    ];

    [Fact]
    public void EveryGuardedActionAsksForTheLevelItWasAuditedAt()
    {
        foreach (var (name, method) in GuardedActions())
        {
            var attribute = RequiredRole(method)!;
            var expected = IsHatch(method) || UserOutsideHatch.Contains(name) ? PersonRole.User : PersonRole.Admin;

            Assert.True(expected == attribute.Minimum, $"{name} asks for {attribute.Minimum}, audited at {expected}");
        }
    }

    [Fact]
    public void HatchRoutesAcceptTheHatchScopeExactlyWhereTheyDidBefore()
    {
        foreach (var (name, method) in GuardedActions())
        {
            var scope = RequiredRole(method)!.AcceptScope;
            var expected = IsHatch(method) && !PersonOnly.Contains(name) ? ApiKeyScopes.Hatch : null;

            Assert.True(expected == scope, $"{name} accepts scope '{scope}', audited at '{expected}'");
        }
    }

    /// <summary>
    /// A method-level attribute does not replace a class-level one - MVC runs
    /// both - so "the method wins" would be a claim about something that does
    /// not happen. Nothing may carry both.
    /// </summary>
    [Fact]
    public void NoActionCarriesARoleOnBothTheMethodAndItsController()
    {
        foreach (var (name, method) in GuardedActions())
        {
            Assert.False(
                method.GetCustomAttribute<RequireRoleAttribute>() is not null
                && method.DeclaringType!.GetCustomAttribute<RequireRoleAttribute>() is not null,
                $"{name} carries a role on the method and the class");
        }
    }

    [Fact]
    public void TheConsideredExceptionsAreStillOpen()
    {
        var open = ActionsWhere(guarded: false).ToHashSet();

        foreach (var action in DeliberatelyOpen)
        {
            Assert.Contains(action, open);
        }
    }

    /// <summary>
    /// Guards the lists themselves: every name in them has to be a real action,
    /// or a rename turns an audited endpoint into an unaudited one and both
    /// tests above keep passing while the entry sits there meaning nothing.
    /// </summary>
    [Fact]
    public void EveryNameInBothListsStillNamesAnAction()
    {
        var all = ActionsWhere(guarded: true).Concat(ActionsWhere(guarded: false)).ToHashSet();

        foreach (var action in Guarded.Concat(DeliberatelyOpen))
        {
            Assert.Contains(action, all);
        }
    }

    private static IEnumerable<(string Name, MethodInfo Method)> AllActions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => ($"{t.Name}.{m.Name}", m)));

    private static IEnumerable<(string Name, MethodInfo Method)> GuardedActions() =>
        AllActions().Where(a => RequiredRole(a.Method) is not null);

    private static IEnumerable<string> ActionsWhere(bool guarded) =>
        AllActions().Where(a => (RequiredRole(a.Method) is not null) == guarded).Select(a => a.Name);

    private static bool IsHatch(MethodInfo action) =>
        action.DeclaringType!.Namespace == "Hatch.Api.Modules.Hatch";

    /// <summary>An attribute on the controller covers every action in it - which is how SettingsController is guarded whole.</summary>
    private static RequireRoleAttribute? RequiredRole(MethodInfo action) =>
        action.GetCustomAttribute<RequireRoleAttribute>()
        ?? action.DeclaringType!.GetCustomAttribute<RequireRoleAttribute>();
}

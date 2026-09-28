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
        "StatusesController.GetStatuses",
        "StatusesController.CreateStatus",
        "StatusesController.PatchStatus",
        "StatusesController.DeleteStatus",
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

        // The account's own Claude headroom, proxied so no browser ever holds
        // the subscription token. Hatch-scoped like the rest of the module and
        // guarded for the same reason the board is: what it says is how much
        // room is left to work tonight, which is a fact about the operator
        // rather than about the house.
        "UtilizationController.Get",

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
        "WorkLogController.GetHistory",
        "WorkLogController.GetSessions",

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

        // A runner's verdict on whether an issue's branch merges with the
        // trunk, and Hatch-scoped for the reason the claim is: the caller is a
        // machine, and a verdict only an operator could enter would be one
        // nobody ever entered. It is a fact about two refs that every runner
        // computes the same way, not a statement about an agent's budget; a
        // wrong one costs a stall, which is a question. See MergeCheckController.
        "MergeCheckController.PutMergeCheck",

        // A playbook's power routed through a different table, and cut the
        // same way: an issue's model and effort override every playbook that
        // could speak for it, so an agent that could set one could raise its
        // own budget. Reading it is open - it rides IssueDto, which is
        // Hatch-scoped - and only the write is here. See
        // IssuePlaybookController.
        "IssuePlaybookController.PatchIssuePlaybook",

        // The runners, and the same split drawn a third time. The heartbeat and
        // the read are a dispatcher's - a loop that could not say it was alive
        // would leave a control surface that could only ever be empty - and the
        // PATCH beside them is the operator's, because every field it writes is
        // a bound on how much a runner may spend and how long it may run. An
        // agent that could raise its own --max-spend could raise its own
        // budget. See RunnersController.
        "RunnersController.GetRunners",
        "RunnersController.Heartbeat",
        "RunnersController.PatchRunner",

        // A related edge, cut the same way and for a different reason: under
        // the loop's "people only" rule an assignee is a dispatch gate, so a
        // key that could write one could clear a person's name off a ticket and
        // hand itself work that was reserved. The directory read beside it is
        // Hatch-scoped - it is a list of names, and an agent has to be able to
        // say whose ticket it is declining to take. See AssigneeController.
        "AssigneeController.GetAssignees",
        "AssigneeController.PutIssueAssignee",

        // The same edge read from the other side. An assignee holds a ticket
        // off the night shift; expedite puts one at the front of it, so a key
        // that could set one could put its own ticket ahead of everything a
        // person filed, every night, with nothing looking wrong on the board.
        // Reading it is open - it rides IssueDto and IssueCardDto, both
        // Hatch-scoped - and only the write is here. See
        // IssueExpediteController.
        "IssueExpediteController.PutIssueExpedite",

        // The ordered list of remotes a project is bound to, cut the same
        // way: a runner that could bind one could point every runner on the
        // board at a repository nobody chose. The read beside it
        // (GetRepositories, above with the rest of ProjectsController) is
        // Hatch-scoped - it is exactly what a dispatch needs to know where to
        // check out. See ProjectsController.
        "ProjectsController.PutRepositories",

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
    /// them: a playbook, an override, a runner bound, an assignee, an expedite,
    /// the settings and the runner binary each choose what the next agent may
    /// do or spend. Each still asks for User - a person, not an Admin.
    /// </summary>
    private static readonly string[] PersonOnly =
    [
        "PlaybooksController.CreatePlaybook",
        "PlaybooksController.PatchPlaybook",
        "PlaybooksController.DeletePlaybook",
        "IssuePlaybookController.PatchIssuePlaybook",
        "RunnersController.PatchRunner",
        "AssigneeController.PutIssueAssignee",
        "IssueExpediteController.PutIssueExpedite",
        "ProjectsController.PutRepositories",
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

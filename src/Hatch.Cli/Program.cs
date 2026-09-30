using System.Runtime.InteropServices;
using System.Text;
using Hatch.Cli;

// The whole CLI, as one program.
//
// It began as the two commands that spawn an agent - `work` and `go-to-work` -
// because a claim is a lease with a clock on it and none of that could be tested
// in a shell script. AERIE-934 brought the other fourteen across for a different
// reason: an operator who is not in this repository has no scripts/hatch.sh, and
// a tracker that can only be reached from one clone is a tracker for one person.
// So this is `hatch`, installed once and run anywhere, and hatch.sh is a door
// into it. See docs/hatch.md, "Where the loop lives".

try
{
    // The renderer draws a few characters that are not ASCII, and a Windows
    // console left on its default code page turns them into question marks.
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // No console attached - a redirected log, or a service. Nothing to set.
}

var say = new Terminal();

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    say.Lines(Usage);
    return args.Length == 0 ? 1 : 0;
}

var command = args[0];
var rest = args[1..];

if (!Program.Commands.Contains(command))
{
    say.Complain($"hatch: no such command \"{command}\" - `hatch --help` lists them.");
    return 1;
}

var environment = Environment.GetEnvironmentVariables()
    .Cast<System.Collections.DictionaryEntry>()
    .ToDictionary(e => (string)e.Key, e => e.Value as string);

// Where the increment happens. hatch.sh names it, because it knows where it
// lives; a runner started by hand finds it by walking up from wherever it was
// started, which is what every other tool in a repository does. Not every
// runner has one: a loop with no checkout of its own is served entirely by
// --repo or HATCH_REPOS, discovered below once Settings has loaded.
var here = Directory.GetCurrentDirectory();
var root = Checkout.Find(environment.GetValueOrDefault("HATCH_ROOT"), here);

// This checkout's own settings, at higher precedence than the per-user file, so
// a repository that pins its own origin keeps it. Absent outside one - a loop
// with no standing checkout reads the exported layer and the per-user file
// only.
var checkoutEnv = root is null ? null : Path.Combine(root, "scripts", ".env");

// `config` is the one command that has to run before there is anything to
// require, so it reads the layers itself rather than through a load that would
// refuse for want of the origin it is there to ask for.
if (command == "config")
{
    var fold = Settings.Layers(checkoutEnv, environment);
    var configured = fold("HATCH_RUNNER").Value;
    var configRunnerName = await ResolveRunnerAsync(
        fold("HATCH_BASE").Value, fold("HATCH_KEY").Value, configured, root ?? here, CancellationToken.None);

    return await new ConfigCommand(say, new Input(), environment, checkoutEnv, configRunnerName, Root: root)
        .RunAsync(rest, CancellationToken.None);
}

if (!Settings.TryLoad(checkoutEnv, environment, out var settings, out var missing))
{
    // A hook that fails is a session that is told so. Nothing to ask, nothing
    // said: the message stays unread and a later step asks again.
    if (command == "inbox") return 0;

    say.Complain(missing);
    return 1;
}

// Before the checkouts are discovered, because that says things - a stray
// under HATCH_REPOS is a line on standard output - and a hook's standard output
// is a document the session parses. `inbox` prints JSON or nothing.
if (command == "inbox")
{
    var inboxRunnerName = await ResolveRunnerAsync(settings.Base, settings.Key, settings.Runner, root ?? here, CancellationToken.None);
    using var hooked = new HatchClient(settings, inboxRunnerName);
    return await new InboxCommand(new Board(hooked), say, Console.In, TimeProvider.System)
        .RunAsync(rest, CancellationToken.None);
}

// The standing checkout, if there is one, then every checkout HATCH_REPOS
// names - refused here, before any lock is taken and before anything is
// claimed, uniformly for all sixteen commands: a misconfigured HATCH_REPOS is
// exactly as broken for `hatch board` as for the loop. `work` and
// `go-to-work` may override this list with their own --repo, below.
if (!Checkouts.TryDiscover(root, settings.Repos, settings.Workspace, out var checkouts, out var strays, out var badRepo))
{
    say.Complain(badRepo);
    return 1;
}

foreach (var stray in strays)
    say.Line($"hatch: {stray} - not a checkout, and nothing under it is one either; left alone");

// The standing checkout's path when there is one, otherwise the first named
// checkout's - not "the checkout the process is standing in" any more.
var primaryRoot = checkouts.Count > 0 ? checkouts[0].Path : here;
var runnerName = await ResolveRunnerAsync(settings.Base, settings.Key, settings.Runner, primaryRoot, CancellationToken.None);

// A lambda rather than say.Complain directly: `say` is reassigned below to a
// LiveTerminal once the readout is known to apply, and a call made after that
// point should still say "waiting" through whichever terminal is current
// rather than the plain one this line started with.
using var client = new HatchClient(settings, runnerName, onWaiting: line => say.Complain(line));
var board = new Board(client);

// Every way out through one door. A signal that is not handled kills the
// process outright, and the increment that ends in an interrupt is exactly the
// one whose claim most needs letting go of - so both are caught, the token is
// cancelled, and the release happens on the way out of the `finally` the
// commands already have. A second one is left to the runtime: somebody pressing
// it twice means now.
using var cancelling = new CancellationTokenSource();
var caught = 0;

using var interrupt = Handle(PosixSignal.SIGINT);
using var terminate = Handle(PosixSignal.SIGTERM);

// The terminal a loop runs in closing - a window shut, a session hung up on -
// raises this on the platforms that have it, and .NET raises it for a closed
// console window on Windows too. Handled the same way SIGINT is: the claim
// most needs letting go of is the one held when nobody is watching any more.
using var hangup = Handle(PosixSignal.SIGHUP);

LiveTerminal? live = null;

try
{
    if (command is "work" or "go-to-work" or "do-my-work")
    {
        // The readout is absent on a redirected log, on TERM=dumb, and on
        // `hatch work -i` - the terminal there belongs to the interactive
        // session, not to this process. Constructed only this late, and inside
        // this try: everything before it can still return without a footer
        // there was never anything to erase.
        var readoutState = new ReadoutState();
        var attached = command == "work" && (rest.Contains("-i") || rest.Contains("--interactive"));
        if (!attached && !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("TERM") != "dumb" &&
            (!OperatingSystem.IsWindows() || WindowsConsole.TryEnableVirtualTerminalProcessing()))
        {
            say = live = new LiveTerminal(
                readoutState, TimeProvider.System, color: Environment.GetEnvironmentVariable("NO_COLOR") is null);
        }

        var runtime = new Runtime(
            Settings: settings,
            Board: board,
            Sessions: new ClaudeSessionRunner(settings.ClaudeBin),
            Say: say,
            Root: primaryRoot,
            RunnerName: runnerName,
            Where: Checkout.Where(Checkout.Host(), primaryRoot),
            TempDirectory: Path.GetTempPath(),
            Checkouts: checkouts)
        {
            // Set by the supervisor in scripts/hatch.sh or scripts/hatch.ps1 and
            // by nobody else, which is how a runner started by hand knows there
            // is nothing standing over it to build the new source and run it
            // again. See docs/hatch.md, "What it stops for".
            NightStatePath = environment.GetValueOrDefault("HATCH_NIGHT_STATE"),
            Readout = readoutState,
        }.WithGit();

        return command switch
        {
            "work" => await new WorkCommand(runtime).RunAsync(rest, cancelling.Token),
            "go-to-work" => await new GoToWorkCommand(runtime).RunAsync(rest, cancelling.Token),
            // Exactly go-to-work --mine, so the two can never drift apart in
            // which flags they accept. -h is asked of go-to-work's own usage
            // rather than answered by prepending a flag in front of it - Usage.Wanted
            // reads only the first argument, and --mine there would hide the ask.
            _ => await new GoToWorkCommand(runtime).RunAsync(
                Hatch.Cli.Usage.Wanted(rest) ? rest : ["--mine", .. rest], cancelling.Token),
        };
    }

    var cli = new Cli(board, say, new Input(), settings, runnerName)
    {
        Checkouts = checkouts,
        Clones = settings.Workspace is not null,
    };

    return command switch
    {
        "board" => await new BoardCommands(cli).BoardAsync(rest, cancelling.Token),
        "next" => await new BoardCommands(cli).NextAsync(rest, cancelling.Token),
        "queue" => await new BoardCommands(cli).QueueAsync(rest, cancelling.Token),
        "show" => await new IssueCommands(cli).ShowAsync(rest, cancelling.Token),
        "start" => await new IssueCommands(cli).StartAsync(rest, cancelling.Token),
        "move" => await new IssueCommands(cli).MoveAsync(rest, cancelling.Token),
        "comment" => await new IssueCommands(cli).CommentAsync(rest, cancelling.Token),
        "pr" => await new IssueCommands(cli).PrAsync(rest, cancelling.Token),
        "depends" => await new DependsCommand(cli).RunAsync(rest, cancelling.Token),
        "ask" => await new QuestionCommands(cli).AskAsync(rest, cancelling.Token),
        "questions" => await new QuestionCommands(cli).QuestionsAsync(rest, cancelling.Token),
        "answer" => await new QuestionCommands(cli).AnswerAsync(rest, cancelling.Token),
        "api" => await new ApiCommand(cli).RunAsync(rest, cancelling.Token),
        "runner-claude-token" => await new ClaudeTokenCommand(cli).RunAsync(rest, cancelling.Token),

        // Unreachable: the name was checked against the same table above.
        _ => 1,
    };
}
catch (OperationCanceledException)
{
    return 130;
}
catch (HatchException e)
{
    say.Complain(e.Message);
    return 1;
}
catch (Exception e)
{
    // Everything above this line has already let go of whatever claim it held
    // - every `work` and `go-to-work` path releases in its own `finally`, and
    // a pass in the loop catches its own exceptions before this is ever
    // reached. What is left to do is say what broke, in one line and with no
    // stack trace, and leave with the exit code a supervisor watches for.
    say.Complain($"hatch: {e.GetType().Name}: {e.Message}");
    return 1;
}
finally
{
    // The night is over, one way or another - Ctrl-C, the board asking this
    // runner to stop, a restart onto a newer build, or the run simply ending.
    // Erase the footer so whatever prints after this - the closing tally, the
    // shell's own prompt - scrolls normally with nothing pinned below it.
    live?.Stop();
}

PosixSignalRegistration? Handle(PosixSignal signal)
{
    try
    {
        return PosixSignalRegistration.Create(signal, context =>
        {
            if (Interlocked.Increment(ref caught) > 1) return;

            context.Cancel = true;
            say.Complain("");
            say.Complain("hatch: stopping - letting go of the ticket first");
            cancelling.Cancel();
        });
    }
    catch (Exception e) when (e is PlatformNotSupportedException or ArgumentOutOfRangeException)
    {
        // Not every signal exists on every platform. The ones that do are
        // caught; the ones that do not were never going to arrive.
        return null;
    }
}

/// <summary>
/// <see cref="Checkout.RunnerAsync"/>, with the throwaway client it needs to
/// ask the board what is already live - built and disposed here so every call
/// site above names a checkout the same way without repeating the plumbing.
/// </summary>
/// <remarks>
/// A second, real client is still built by the caller for everything after:
/// this one exists only long enough to read <c>GET /api/hatch/runners</c>, and
/// its own name never reaches the board for anything else.
/// </remarks>
async Task<string> ResolveRunnerAsync(string? origin, string? key, string? configured, string forRoot, CancellationToken ct)
{
    var host = Checkout.Host();

    using var lookupClient = string.IsNullOrWhiteSpace(origin)
        ? null
        : new HatchClient(new Settings { Base = origin.TrimEnd('/'), Key = key ?? "" }, Checkout.Where(host, forRoot));

    return await Checkout.RunnerAsync(
        configured, host, forRoot, lookupClient is null ? null : new Board(lookupClient), ct);
}

internal partial class Program
{
    /// <summary>
    /// The commands something other than a person runs, and no session is ever
    /// told about.
    /// </summary>
    /// <remarks>
    /// Real commands, in <see cref="Commands"/> and in <see cref="Usage"/> like
    /// every other - there is no hidden verb here. What this list is for is the
    /// block in src/Hatch.Web/apps/hatch/public/hatch-at-home.md that a friend pastes into their own
    /// repository's <c>CLAUDE.md</c>: that block is the contract an agent
    /// works to, and <c>runner-claude-token</c> is the container entrypoint's
    /// own plumbing (containers/hatch-runner/entrypoint.sh). Naming it there
    /// would be telling every session in the house about a command that prints
    /// a credential, for no work it could ever do with it. <c>inbox</c> is what a
    /// session's hooks call to be handed a message, and marks it read: a session
    /// that knew of it could mark its operator's messages read without reading
    /// them. DocsContractTests
    /// holds both halves of that: the rest are named in the block, and these
    /// are deliberately not.
    /// </remarks>
    public static readonly string[] Internal =
    [
        "runner-claude-token",
        "inbox",
    ];

    /// <summary>
    /// Every command, in one place - so a name that is not one of them is
    /// refused before anything is loaded, and so the dispatch below and this
    /// list cannot drift apart.
    /// </summary>
    public static readonly string[] Commands =
    [
        "config", "board", "next", "queue", "show", "start", "move", "comment", "pr",
        "depends", "ask", "questions", "answer", "api", "work", "go-to-work", "do-my-work",
        .. Internal,
    ];

    public static readonly string[] Usage =
    [
        "hatch - the house tracker, from a terminal",
        "",
        "  hatch config                 ask for the origin and the key, and write them",
        "  hatch config --show          what is set, and which layer it came from",
        "  hatch board                  the columns, and how many cards in each",
        "  hatch next                   top workable card of \"todo\"",
        "  hatch next \"in progress\"      ...or of any column",
        "  hatch queue                  every card a pass would look at, and why",
        "  hatch queue AER-1            ...under one epic",
        "  hatch queue --mine           ...only your own",
        "  hatch show AER-12            the brief, plus its comments",
        "  hatch start AER-12           move it to \"in progress\"",
        "  hatch move AER-12 todo       ...or to any non-terminal column",
        "  hatch comment AER-12 \"sha abc123 on branch aer-12-thing\"",
        "  hatch pr AER-12              where it is being reviewed",
        "  hatch pr AER-12 https://...  ...or say where, having opened one",
        "  hatch pr AER-12 --clear      ...or take it off the one it has",
        "  hatch depends AER-12         what it waits on, and what waits on it",
        "  hatch depends AER-12 AER-11  AER-12 waits on AER-11",
        "  hatch depends AER-12 --remove AER-11        ...no longer",
        "  hatch ask AER-12 \"how should retries be scoped?\" \\",
        "      --recommend \"Per-node: one budget per node, so a slow node cannot starve\" \\",
        "      --option \"Global: one budget for the drain, simpler to reason about\"",
        "  hatch questions              everything waiting on an answer",
        "  hatch questions AER-12       ...or just this ticket's",
        "  hatch answer                 answer them, one at a time, here",
        "  hatch api GET /api/hatch/issues?statusId=2",
        "  hatch api PATCH /api/hatch/issues/AER-12 '{\"dueAt\":\"2026-10-01\"}'",
        "",
        "The one the container runner's entrypoint calls, and nobody types:",
        "",
        "  hatch runner-claude-token    the Claude token this Hatch holds, decoded",
        "",
        "And the one a session's hooks call, and nobody types:",
        "",
        "  hatch inbox AER-12 --hook stop    what was said to the session on it, marked read",
        "",
        "The three that spawn an agent, and the only three that need a checkout or a",
        "workspace to clone into - the one you are standing in, one named with --repo",
        "or HATCH_REPOS, or --workspace or HATCH_WORKSPACE to clone what is missing:",
        "",
        "  hatch work                   one increment on the next thing due",
        "  hatch work AER-12            ...or on this one",
        "  hatch work --under AER-1     ...or on the next thing under one epic",
        "  hatch work --mine            ...or on the next of your own - AER-12 with this is refused",
        "  hatch work -i AER-12         ...in a session you sit in",
        "  hatch work --quiet           ...saying nothing until it is finished",
        "  hatch work --model opus --effort xhigh AER-12",
        "  hatch work --dry-run         print the prompt, spawn nothing",
        "  hatch work --repo /path/to/a/checkout    ...also serve that checkout, repeatable",
        "  hatch work --workspace /clones           ...clone what the board binds, into /clones",
        "  hatch go-to-work             increments, back to back, until told to stop",
        "  hatch go-to-work --once      ...one pass, and out",
        "  hatch go-to-work --under AER-1 --interval 300",
        "  hatch go-to-work --max-runs 5 --max-spend 20 --until 08:00",
        "  hatch go-to-work --stop-file /tmp/stop",
        "  hatch go-to-work --restart-after 60   ...coming back as a newer build that often",
        "  hatch go-to-work --restart-after 0    ...only when its own source changed",
        "  hatch go-to-work --no-restart         ...never coming back as a newer one",
        "  hatch go-to-work --repo /path/to/a/checkout    ...also serve that checkout, repeatable",
        "  hatch go-to-work --workspace /clones           ...clone what the board binds, into /clones",
        "  hatch go-to-work --mine      ...take only your own tickets, the whole night through",
        "  hatch do-my-work             ...exactly go-to-work --mine, every flag above included",
        "",
        "Every command takes -h for its own usage block.",
        "",
        "Settings, highest first: an exported variable, then scripts/.env in the",
        "checkout you are standing in, then the per-user file `config` writes.",
        "",
        "  HATCH_BASE         https://hatch.<your domain>",
        "  HATCH_KEY          hatch_ak_... Optional against a Hatch with its wall off,",
        "                     where calls name themselves with a runner header instead",
        "  HATCH_CLAUDE_BIN   the claude CLI, if it is not on PATH",
        "  HATCH_BASE_BRANCH  the trunk go-to-work resets to between increments",
        "  HATCH_RUNNER       what the board calls this runner (default: a character from",
        "                     the cast list, chosen once per checkout - see `hatch config`)",
        "  HATCH_ROOT         the checkout to work in (default: upwards from here)",
        "  HATCH_REPOS        checkouts a loop with no checkout of its own serves, joined on",
        "                     the platform's path separator (: on Unix, ; on Windows)",
        "  HATCH_WORKSPACE    a directory this runner owns entirely, where it clones every",
        "                     repository the board binds that it has no checkout of",
        "  HATCH_HEARTBEAT    seconds of silence before the renderer says what it is waiting on",
        "  HATCH_RETRY_SECONDS  seconds a call keeps retrying Hatch, 90 by default, 0 for one try",
        "  HATCH_NIGHT_STATE  where a night's totals are handed to the loop that restarts into",
        "",
        "A go-to-work whose own source changes under it asks to be restarted as the new",
        "build, and something outside it has to rebuild and run it again. A hatch started",
        "with nobody standing over it does not ask: it is the loop it started as.",
    ];
}

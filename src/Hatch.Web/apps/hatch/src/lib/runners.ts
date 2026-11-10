/* What a runner row says about itself, apart from the page that draws it - the
   way lib/claim.ts is apart from the badge.

   The server hands over four facts and no verdict: when the runner was last
   heard from, how long silence is allowed to last, what it holds, and what it
   was asked to do. Turning those into one word is this file's whole job, and it
   is here rather than in the page because the ordering between them is a
   judgement worth testing: a runner that has stopped answering is *gone*
   whatever it was asked to do, because "we told it to pause" says nothing about
   whether it is still there to have heard.

   Pure functions taking `now` rather than reading it, so a page judges every
   row against one instant. */

import { resetClockWords } from './attention';
import { agoPhrase } from './claim';
import { buildTimePhrase, isStamped, shortSha } from './revision';
import { runnerTagsDraft, runnerTagsRequest, sameTags } from './runnerTags';
import { compactTokens, parseTokenCount, thousands, TOKEN_FIELD_HINT } from './workLog';
import type { Runner, RunnerPatchRequest, RunnerSession } from '../types';

/** The seven states a row can be in, in the order they are decided. */
export type RunnerActivity = 'gone' | 'exhausted' | 'throttled' | 'paused' | 'stopping' | 'working' | 'idle';

/** The Max spend field's hint, naming the one thing the cap cannot do: bind an
    agent whose session reports no price at all. */
export const MAX_SPEND_HINT =
  'Dollars, on the same terms. It counts only sessions whose agent reports a price, so it cannot bind one that does not.';

/**
 * What this runner is doing, in one word.
 *
 * Silence beats everything, which is the one ordering decision here: a
 * runner that stopped heartbeating is `gone` whatever it last reported about
 * itself, because silence is a fact and everything else is a stale claim.
 * Below that, a fact the runner reports about its own account -
 * `exhaustedUntil` - beats a request a person made of it: an operator who
 * paused a runner still sees that it is out of usage, because pausing
 * it changes nothing about whether it has any usage to spend. That fact is
 * read the same way `gone`'s heartbeat is, though: a reset time only counts
 * while it is still ahead of `now` - once it has passed, it is no longer a
 * fact about the account, the same as a heartbeat old enough to be silence.
 * `throttledUntil` is read the same way, future-only, and ranks just below
 * `exhausted` and above `paused`: a runner over its own hourly token cap is
 * waiting on the clock whatever a person asked of it, but its account still
 * having usage left makes it a lesser fact than being out of usage.
 * Below that, what it was asked to do beats what it is holding - a loop
 * finishing its last increment is `stopping`, and that is the thing somebody
 * watching wants to see.
 */
export function runnerActivity(runner: Runner, now: Date): RunnerActivity {
  return factsActivity(runner, now);
}

/** The facts the rule above is decided from. A runner row and one of its
    sessions both carry them, which is why the badge on each is decided by the
    same function and cannot drift apart. */
interface ActivityFacts {
  lastSeenAt: string;
  goneAfterSeconds: number;
  exhaustedUntil: string | null;
  throttledUntil: string | null;
  state: Runner['state'];
  claimKey: string | null;
  maxTokensPerHour: number | null;
}

function factsActivity(facts: ActivityFacts, now: Date): RunnerActivity {
  const beat = Date.parse(facts.lastSeenAt);

  // An unparseable heartbeat is the one case where "look at this" is the honest
  // answer: it is a row whose age cannot be told, not a row that is fine.
  if (Number.isNaN(beat) || now.getTime() - beat > facts.goneAfterSeconds * 1000) return 'gone';

  if (facts.exhaustedUntil !== null && Date.parse(facts.exhaustedUntil) > now.getTime()) return 'exhausted';
  if (facts.throttledUntil !== null && Date.parse(facts.throttledUntil) > now.getTime()) return 'throttled';
  if (facts.state === 'paused') return 'paused';
  if (facts.state === 'stopping') return 'stopping';

  return facts.claimKey ? 'working' : 'idle';
}

function factsWords(facts: ActivityFacts, now: Date): string {
  const activity = factsActivity(facts, now);
  switch (activity) {
    case 'gone':
      return 'Gone';
    case 'exhausted': {
      const words = resetClockWords(new Date(facts.exhaustedUntil as string), now);
      return `Out of usage until ${words ?? 'an unknown time'}`;
    }
    case 'throttled': {
      const words = resetClockWords(new Date(facts.throttledUntil as string), now);
      const over = facts.maxTokensPerHour === null ? 'its hourly token cap' : `${compactTokens(facts.maxTokensPerHour)} tokens/hour`;
      return `Over ${over} until ${words ?? 'an unknown time'}`;
    }
    case 'paused':
      return 'Paused';
    case 'stopping':
      return 'Stopping';
    case 'working':
      return `Working ${facts.claimKey}`;
    default:
      return 'Idle';
  }
}

function factsParts(facts: ActivityFacts, now: Date): { words: string; claimKey: string | null } {
  return factsActivity(facts, now) === 'working'
    ? { words: 'Working', claimKey: facts.claimKey }
    : { words: factsWords(facts, now), claimKey: null };
}

/** The same, as the words on the row. */
export function activityWords(runner: Runner, now: Date): string {
  return factsWords(runner, now);
}

/** `activityWords` split into the badge's words and the key beside it, for a
    reader that draws the key as a link rather than as part of the sentence -
    only a working row carries one, the same split `runnerStrip` already
    makes. */
export function activityParts(runner: Runner, now: Date): { words: string; claimKey: string | null } {
  return factsParts(runner, now);
}

/** A session's facts judged against its runner's own horizon and cap: the
    session carries its own heartbeat, claim and exhaustion, the runner carries
    how long silence may last. */
const sessionFacts = (session: RunnerSession, runner: Runner): ActivityFacts => ({
  lastSeenAt: session.lastSeenAt,
  goneAfterSeconds: runner.goneAfterSeconds,
  exhaustedUntil: session.exhaustedUntil,
  throttledUntil: session.throttledUntil,
  state: session.state,
  claimKey: session.claimKey,
  maxTokensPerHour: runner.maxTokensPerHour,
});

/** What one session is doing, decided by the same rule as its runner's row. */
export function sessionActivity(session: RunnerSession, runner: Runner, now: Date): RunnerActivity {
  return factsActivity(sessionFacts(session, runner), now);
}

/** A session's badge words and the key beside it, as `activityParts` for a row. */
export function sessionParts(session: RunnerSession, runner: Runner, now: Date): { words: string; claimKey: string | null } {
  return factsParts(sessionFacts(session, runner), now);
}

/**
 * The sessions worth a line under their runner, in the server's order.
 *
 * Live means no end and not past the runner's gone horizon - the same rule
 * `hatch runners` indents by. The horizon is checked here as well as the end
 * because the in-memory provider skips the lazy `endedAt` write the server
 * makes on this very read.
 */
export function liveSessions(runner: Runner, now: Date): RunnerSession[] {
  return (runner.sessions ?? []).filter(
    (session) => session.endedAt === null && sessionActivity(session, runner, now) !== 'gone',
  );
}

/** Live sessions, ordered for the blocks under a runner's name: `startedAt`
    ascending so a block never reorders as it beats, ties broken by `where`
    with a null last - a copy, so the server's own order on `runner.sessions`
    is never disturbed. */
export function sessionsByStart(sessions: readonly RunnerSession[]): RunnerSession[] {
  return [...sessions].sort((a, b) => {
    const started = Date.parse(a.startedAt) - Date.parse(b.startedAt);
    if (started !== 0) return started;
    if (a.where === b.where) return 0;
    if (a.where === null) return 1;
    if (b.where === null) return -1;
    return a.where < b.where ? -1 : 1;
  });
}

/** The sessions not worth a live block: ended, or open but past the
    runner's gone horizon - `liveSessions`' complement, newest `startedAt`
    first, capped at 20, the server's own retention. */
export function recentSessions(runner: Runner, now: Date): RunnerSession[] {
  const live = new Set(liveSessions(runner, now).map((session) => session.id));
  return (runner.sessions ?? [])
    .filter((session) => !live.has(session.id))
    .sort((a, b) => Date.parse(b.startedAt) - Date.parse(a.startedAt))
    .slice(0, 20);
}

/** What each activity is worth saying loudly. Working is the one worth a colour
    - it is the row that is spending money right now - and gone is the one worth
    a warning. */
export const ACTIVITY_TONES: Record<RunnerActivity, 'muted' | 'success' | 'danger' | 'primary'> = {
  working: 'success',
  stopping: 'primary',
  paused: 'primary',
  exhausted: 'danger',
  throttled: 'danger',
  gone: 'danger',
  idle: 'muted',
};

/** One row of the strip the queue modal draws: every per-runner decision the
    strip needs, already made, so the component only draws. */
export interface RunnerStripRow {
  name: string;
  where: string | null;
  activity: RunnerActivity;
  words: string;
  tone: 'muted' | 'success' | 'danger' | 'primary';
  claimKey: string | null;
  line: string | null;
  ago: string;
  gone: boolean;
}

/**
 * The strip's rows, in the order the runners were given - the server's order,
 * most recently heard from first, is the order, so this does not sort.
 *
 * `claimKey` and `line` belong to a working row only: the strip draws the key
 * beside the badge as a link, so a ghost, an idle runner and a paused one
 * carry neither, and `words` says plainly `Working` rather than
 * `activityWords`' `Working AER-12`, which would say the key twice.
 */
export function runnerStrip(runners: readonly Runner[], now: Date): RunnerStripRow[] {
  return runners.map((runner) => {
    const activity = runnerActivity(runner, now);
    const { words, claimKey } = activityParts(runner, now);

    return {
      name: runner.name,
      where: runner.where,
      activity,
      words,
      tone: ACTIVITY_TONES[activity],
      claimKey,
      line: activity === 'working' ? runner.line : null,
      ago: agoPhrase(runner.lastSeenAt, now),
      gone: activity === 'gone',
    };
  });
}

/** The runners that share one machine, with what that machine has spent. */
export interface RunnerHostGroup {
  /** The machine, or null for a runner that never said - always alone. */
  host: string | null;
  runners: Runner[];
  totalRuns: number;
  totalSpent: number;
}

/**
 * The runners gathered by the machine they run from, each group at the place
 * its first member held - the server's order, so this does not sort.
 *
 * A runner with no host is a group of one and is never gathered with another:
 * an absent host is not evidence of a shared machine. A member whose `runs` or
 * `spent` is null has not reported yet and counts as nothing, the same reading
 * the field itself carries.
 */
export function groupByHost(runners: readonly Runner[]): RunnerHostGroup[] {
  const byHost = new Map<string, Runner[]>();
  for (const runner of runners) {
    if (!runner.host) continue;
    const members = byHost.get(runner.host);
    if (members) members.push(runner);
    else byHost.set(runner.host, [runner]);
  }

  const emitted = new Set<string>();
  const groups: RunnerHostGroup[] = [];
  const group = (host: string | null, members: Runner[]): RunnerHostGroup => ({
    host,
    runners: members,
    totalRuns: members.reduce((sum, m) => sum + (m.runs ?? 0), 0),
    totalSpent: members.reduce((sum, m) => sum + (m.spent ?? 0), 0),
  });

  for (const runner of runners) {
    if (!runner.host) {
      groups.push(group(null, [runner]));
    } else if (!emitted.has(runner.host)) {
      emitted.add(runner.host);
      groups.push(group(runner.host, byHost.get(runner.host) as Runner[]));
    }
  }

  return groups;
}

/** Whether this row was replaced by another name from the same checkout. */
export const isRetired = (runner: Runner) => Boolean(runner.retiredAt);

/** The states the list's glyph can draw - `RunnerActivity` plus `none`, for a
    runner that has never had a session at all. */
export type RunnerGlyphState = RunnerActivity | 'none';

/** How the glyph states rank against each other, highest first: gone beats
    everything, and a runner with no session at all outranks nothing. */
const RANK: Record<RunnerGlyphState, number> = {
  gone: 6,
  exhausted: 5,
  throttled: 4,
  paused: 3,
  stopping: 3,
  working: 2,
  idle: 1,
  none: 0,
};

/**
 * The glyph a runner's row draws: the highest-ranked state among its live
 * sessions, or `gone`/`none` where it has none live - `gone` when it has ever
 * had a session at all, `none` when it never has.
 */
export function runnerGlyphState(runner: Runner, now: Date): RunnerGlyphState {
  const live = liveSessions(runner, now);
  if (live.length === 0) return (runner.sessions ?? []).length > 0 ? 'gone' : 'none';

  return live
    .map((session) => sessionActivity(session, runner, now))
    .reduce((best, activity) => (RANK[activity] > RANK[best] ? activity : best));
}

/**
 * Which live session the row's second line speaks for: among the sessions at
 * the winning rank (paused and stopping tie), the one most recently heard
 * from - the earlier in server order on a tie. Null where no session is live.
 */
export function speakingSession(runner: Runner, now: Date): RunnerSession | null {
  const live = liveSessions(runner, now);
  if (live.length === 0) return null;

  const winningRank = Math.max(...live.map((session) => RANK[sessionActivity(session, runner, now)]));
  const contenders = live.filter((session) => RANK[sessionActivity(session, runner, now)] === winningRank);
  return contenders.reduce((best, session) => (Date.parse(session.lastSeenAt) > Date.parse(best.lastSeenAt) ? session : best));
}

/** The newest `lastSeenAt` over every session a runner has ever carried -
    live, ended or history - or null with none at all. */
export function runnerLastSeen(runner: Runner): string | null {
  const sessions = runner.sessions ?? [];
  if (sessions.length === 0) return null;

  return sessions.reduce((latest, session) => (Date.parse(session.lastSeenAt) > Date.parse(latest.lastSeenAt) ? session : latest))
    .lastSeenAt;
}

/** The row's second line, worded by its glyph state. */
export function rowSecondLine(runner: Runner, now: Date): string {
  const state = runnerGlyphState(runner, now);
  switch (state) {
    case 'working': {
      const speaking = speakingSession(runner, now) as RunnerSession;
      const others = liveSessions(runner, now).filter(
        (session) => session !== speaking && sessionActivity(session, runner, now) === 'working',
      ).length;
      const base = speaking.line ? `${speaking.claimKey} - ${speaking.line}` : (speaking.claimKey as string);
      return others > 0 ? `${base} +${others} more` : base;
    }
    case 'idle':
      return `Idle - seen ${agoPhrase(runnerLastSeen(runner), now)}`;
    case 'paused':
    case 'stopping':
    case 'throttled':
    case 'exhausted':
      return sessionParts(speakingSession(runner, now) as RunnerSession, runner, now).words;
    case 'gone':
      return `Gone - last seen ${agoPhrase(runnerLastSeen(runner), now)}`;
    default:
      return 'Not started';
  }
}

/** The row's third line: the owner, then the one distinct host among its
    live sessions - omitted with none, named with one, counted with more. */
export function rowThirdLine(runner: Runner, now: Date): string {
  const owner = runner.for ?? 'no owner';
  const hosts = Array.from(
    new Set(
      liveSessions(runner, now)
        .map((session) => session.host)
        .filter((host): host is string => host !== null),
    ),
  );

  if (hosts.length === 0) return owner;
  if (hosts.length === 1) return `${owner} · ${hosts[0]}`;
  return `${owner} · ${hosts.length} machines`;
}

/**
 * The list's rows, split and ordered: retired and keyless rows dropped, the
 * rest split on whether they are revoked, and each half ordered the same
 * way - a live session first, then most recently seen, then name. Revoked
 * rows keep the same live-first rule rather than a special case.
 */
export function sortRunners(runners: readonly Runner[], now: Date): { shown: Runner[]; revoked: Runner[] } {
  const eligible = runners.filter((runner) => !isRetired(runner) && runner.apiKeyId !== null && runner.apiKeyId !== undefined);
  const revoked = eligible.filter((runner) => Boolean(runner.revokedAt));
  const rest = eligible.filter((runner) => !Boolean(runner.revokedAt));

  const order = (a: Runner, b: Runner): number => {
    const aLive = liveSessions(a, now).length > 0;
    const bLive = liveSessions(b, now).length > 0;
    if (aLive !== bLive) return aLive ? -1 : 1;

    const aTime = runnerLastSeen(a);
    const bTime = runnerLastSeen(b);
    const aMs = aTime === null ? null : Date.parse(aTime);
    const bMs = bTime === null ? null : Date.parse(bTime);
    if (aMs !== bMs) {
      if (aMs === null) return 1;
      if (bMs === null) return -1;
      return bMs - aMs;
    }

    return a.name < b.name ? -1 : a.name > b.name ? 1 : 0;
  };

  return { shown: rest.sort(order), revoked: revoked.sort(order) };
}

/** Mirrors EfApiKey.MaxNameLength. */
export const MAX_NAME_LENGTH = 120;

export const lastUsedLabel = (iso: string | null): string => (iso === null ? 'never' : new Date(iso).toLocaleString());

/**
 * Whether this row has controls at all.
 *
 * A `once` runner is one increment that has already read whatever it was going
 * to read; a Pause on it would be a button nothing will ever look at, which is
 * worse than no button. A row somebody else owns has controls only for its
 * owner or an admin. A retired row has been replaced by another name from the
 * same checkout, and nothing answers to it any more: it has none either.
 */
export const isControllable = (runner: Runner) => runner.kind === 'loop' && runner.canControl && !isRetired(runner);

/** Which of the detail's three variants a caller sees - "Who sees what" in docs/runners-page-design.md. */
export type DetailVariant = 'admin' | 'owner' | 'viewer';

/** Admin sees everything; an owner who is not admin sees their own controls; everyone else is read-only. */
export const detailVariant = (isAdmin: boolean, runner: Runner): DetailVariant =>
  isAdmin ? 'admin' : runner.canControl ? 'owner' : 'viewer';

/** A revoked runner has no actions for anyone. */
export const runnerActionable = (runner: Runner) => !runner.revokedAt;

/** The repositories a runner serves, as one phrase - or null when it has
    named none. */
export function repositoriesLabel(runner: Runner): string | null {
  if (runner.repositories.length === 0) return null;
  const repos = runner.repositories.join(', ');
  return runner.clones ? `${repos}, clones what it lacks` : repos;
}

/** A session's own repositories, as `repositoriesLabel`'s mirror over its
    `remotes` - or null where it has named none. */
export function sessionRepositoriesLabel(session: RunnerSession): string | null {
  const remotes = session.remotes ?? [];
  if (remotes.length === 0) return null;
  const repos = remotes.join(', ');
  return session.clones ? `${repos}, clones what it lacks` : repos;
}

/** A build's own facts, wide enough for a runner or one of its sessions -
    both carry `revision`/`builtAt`, a session's optional rather than
    required. */
interface BuildFacts {
  revision?: string | null;
  builtAt?: string | null;
}

/** A runner's or a session's own build, as the row says it: a short sha and
    how long ago, `'dev build'` for an unstamped one, or null for an older
    CLI that has never reported one at all - the one case with nothing to
    say, mirrored on BuildStamp's own `revision === null` rule. */
export function runnerBuildLabel(facts: BuildFacts, now: Date): string | null {
  const revision = facts.revision ?? null;
  if (revision === null) return null;
  if (!isStamped(revision)) return 'dev build';
  const phrase = buildTimePhrase(revision, facts.builtAt ?? null, now);
  return phrase ? `${shortSha(revision)} · ${phrase.relative}` : shortSha(revision);
}

/** A runner's tags, joined - or null where it has named none. */
export function tagsLabel(runner: Runner): string | null {
  return runner.tags.length === 0 ? null : runner.tags.join(', ');
}

/** A runner's project bound, as the summary says it - or null where it has none. The `projects ` prefix is deliberate: a bare key beside an issue key reads as nothing. The bound does not change the merge train. */
export function projectsLabel(runner: Runner): string | null {
  return runner.projects.length === 0 ? null : `projects ${runner.projects.join(', ')}`;
}

/** Whether this runner refuses dangerous work, as the summary says it - or null where it does not, drawn the same as `clones`. */
export function dangerousLabel(runner: Runner): string | null {
  return runner.refusesDangerous ? 'Refuses dangerous work' : null;
}

/** A runner's models, joined - or null where it has reported none. The `models ` prefix is deliberate, the same reason as `projectsLabel`: a bare list beside an agent name reads as nothing. */
export function modelsLabel(runner: Runner): string | null {
  return runner.models.length === 0 ? null : `models ${runner.models.join(', ')}`;
}

/** A runner's dependencies, joined - or null where it has reported none. The `has ` prefix is deliberate, the same reason as `modelsLabel`: a bare list beside an agent name reads as nothing. Each entry is `id` or `id@version`; the `@` becomes a space, since the version is a fact about the id, not part of it. */
export function hasLabel(runner: Runner): string | null {
  if (runner.has.length === 0) return null;
  const entries = runner.has.map((entry) => {
    const at = entry.indexOf('@');
    return at === -1 ? entry : `${entry.slice(0, at)} ${entry.slice(at + 1)}`;
  });
  return `has ${entries.join(', ')}`;
}

/** Whether this runner was started with do-my-work or --mine, as the word for the row - or null, drawn the same as `clones`. */
export function mineLabel(runner: Runner): string | null {
  return runner.mine ? 'own tickets only' : null;
}

/** The trains this runner drives, as one phrase - or the sentence saying it
    drives none, the one way this label is louder than repositoriesLabel and
    mineLabel: AC5 asks the row to say it is driving none, not say nothing. */
export function drivesLabel(runner: Runner): string {
  return runner.drives.length === 0 ? 'driving nothing' : `driving ${runner.drives.join(', ')}`;
}

/** A held order as the row says it: what was asked, by whom, and how long ago. */
export function orderSentence(order: NonNullable<Runner['order']>, now: Date): string {
  return `ordered to ${order.kind}${order.key ? ' ' + order.key : ''} by ${order.by ?? 'somebody'}, ${agoPhrase(order.at, now)}`;
}

/** Whether the text is shaped like an issue key. Only gates the button - the
    server does the real parse. */
export const isIssueKeyShape = (text: string): boolean => /^[A-Za-z][A-Za-z0-9]*-\d+$/.test(text.trim());

/** The share of a token cap at which its line turns to a warning. Its own
    bands, not `rampColor`'s: a cap is a ceiling a run is stopped at, so the
    interesting question is how close it is, not how busy the machine is. */
export const TOKEN_WARN_PERCENT = 80;

/** How loudly a token line says how much of its cap is spent. */
export function budgetTone(percent: number): 'success' | 'warn' | 'danger' {
  if (percent >= 100) return 'danger';
  return percent >= TOKEN_WARN_PERCENT ? 'warn' : 'success';
}

/** One line of a runner's token budget, already worded and measured. */
export interface TokenBudgetLine {
  text: string;
  /** How much of the cap is spent, 0-100, or null where there is no cap. */
  percent: number | null;
  tone: 'success' | 'warn' | 'danger' | null;
}

/** One window's line: the figure against its cap (an unreported figure counts
    as 0), or the figure alone where the cap is unset - or nothing, where the
    cap is unset and no run reported a figure. */
function budgetLine(used: number | null, cap: number | null, window: string, isDefault: boolean): TokenBudgetLine | null {
  if (cap === null) {
    return used === null ? null : { text: `${compactTokens(used)} ${window}, no cap`, percent: null, tone: null };
  }
  const spent = used ?? 0;
  const percent = cap > 0 ? Math.min(100, (spent / cap) * 100) : 100;
  const text = `${compactTokens(spent)} / ${compactTokens(cap)} ${window}${isDefault ? ' (default)' : ''}`;
  return { text, percent, tone: budgetTone(percent) };
}

/**
 * The token budget a row says: the night's line, then the hour's.
 *
 * Neither cap set is no budget at all, and says nothing even where a total was
 * reported - the cell words that as `No token budget`. Otherwise each window
 * speaks for itself: against its cap where it has one, and as a bare figure
 * where it has none but a run reported one. The percent comes from the raw
 * numbers, never a rounded `compactTokens` figure, so 79.9 stays a success.
 */
export function tokenBudget(runner: Runner): TokenBudgetLine[] {
  if (runner.maxTokens === null && runner.maxTokensPerHour === null) return [];

  const night = budgetLine(runner.tokensUsed, runner.maxTokens, 'tokens', runner.maxTokensSource === 'default');
  const hour = budgetLine(runner.tokensLastHour, runner.maxTokensPerHour, 'per hour', runner.maxTokensPerHourSource === 'default');
  return [night, hour].filter((line): line is TokenBudgetLine => line !== null);
}

/** The same, for one session: its own `maxTokens`/`maxTokensPerHour` against
    its own usage. Never `(default)` - a session's cap is always its own,
    never inherited the way a runner's can be. */
export function sessionTokenBudget(session: RunnerSession): TokenBudgetLine[] {
  const maxTokens = session.maxTokens ?? null;
  const maxTokensPerHour = session.maxTokensPerHour ?? null;
  if (maxTokens === null && maxTokensPerHour === null) return [];

  const night = budgetLine(session.tokensUsed ?? null, maxTokens, 'tokens', false);
  const hour = budgetLine(session.tokensLastHour ?? null, maxTokensPerHour, 'per hour', false);
  return [night, hour].filter((line): line is TokenBudgetLine => line !== null);
}

/** The bounds as the form holds them: strings, because that is what inputs are
    and what the wire takes. */
export interface RunnerBounds {
  under: string;
  maxRuns: string;
  maxSpend: string;
  /** Grouped with commas once the box is left (`2,000,000`), or as typed. */
  maxTokens: string;
  maxTokensPerHour: string;
  /** The wire form, or `''` - what MomentField hands back. */
  untilAt: string;
  tags: string;
}

/** What a row's bounds look like in the form that edits them. */
export function boundsOf(runner: Runner): RunnerBounds {
  return {
    under: runner.under ?? '',
    maxRuns: runner.maxRuns === null ? '' : String(runner.maxRuns),
    maxSpend: runner.maxSpend === null ? '' : String(runner.maxSpend),
    maxTokens: runner.maxTokensSource === 'runner' && runner.maxTokens !== null ? thousands(runner.maxTokens) : '',
    maxTokensPerHour:
      runner.maxTokensPerHourSource === 'runner' && runner.maxTokensPerHour !== null ? thousands(runner.maxTokensPerHour) : '',
    untilAt: runner.untilAt ?? '',
    tags: runnerTagsDraft(runner.tags),
  };
}

/** A token box as the wire takes it: trimmed, and with the commas of the
    on-blur grouping taken out - but only when they are exactly a grouping, so
    `2,000,000` still parses and `2,5` is left for the grammar to refuse. */
export function tokensWire(text: string): string {
  const trimmed = text.trim();
  return /^\d{1,3}(,\d{3})+$/.test(trimmed) ? trimmed.replaceAll(',', '') : trimmed;
}

/** Whether a token box holds something other than the saved one. Compared as
    counts where both parse, so `2M` against a saved `2,000,000` is no change. */
function tokensDiffer(draft: string, saved: string): boolean {
  const a = parseTokenCount(tokensWire(draft));
  const b = parseTokenCount(tokensWire(saved));
  return a !== null && b !== null ? a !== b : draft.trim() !== saved.trim();
}

/** Whether the form is holding anything the row is not. */
export function boundsChanged(draft: RunnerBounds, runner: Runner): boolean {
  const saved = boundsOf(runner);
  return (
    (['under', 'maxRuns', 'maxSpend', 'untilAt'] as const).some((f) => draft[f].trim() !== saved[f].trim()) ||
    (['maxTokens', 'maxTokensPerHour'] as const).some((f) => tokensDiffer(draft[f], saved[f])) ||
    !sameTags(runnerTagsRequest(draft.tags), runner.tags)
  );
}

/**
 * Why the form cannot be saved yet, or null.
 *
 * The one rule worth stating on screen rather than discovering as a refusal:
 * `until` is an hour and not a day. The server says the same thing, and this is
 * only what stops somebody typing a date, pressing Save and being told off for
 * a control that offered them the empty time box in the first place.
 */
export function boundsProblem(draft: Pick<RunnerBounds, 'untilAt' | 'maxTokens' | 'maxTokensPerHour'>): string | null {
  if (draft.untilAt.trim() !== '' && !draft.untilAt.includes('T')) {
    return 'An until is an hour, not just a day - set a time beside the date.';
  }
  if (!tokensParse(draft.maxTokens)) return 'Max tokens this run takes a token count, like 500k or 2M.';
  if (!tokensParse(draft.maxTokensPerHour)) return 'Max tokens per hour takes a token count, like 500k or 2M.';
  return null;
}

/** Whether a token box is empty or holds a count - the only two it can be saved in. */
const tokensParse = (text: string): boolean => {
  const wire = tokensWire(text);
  return wire === '' || parseTokenCount(wire) !== null;
};

/**
 * The form as a request: every field, trimmed, including the empty ones.
 *
 * Sending every field rather than only what changed is the point of the tri-state:
 * `''` is how a cap comes off, and a field left out would mean "leave it", so a
 * cleared box that was not sent would silently keep the cap it was clearing.
 */
export function boundsRequest(draft: RunnerBounds): RunnerPatchRequest {
  return {
    under: draft.under.trim(),
    maxRuns: draft.maxRuns.trim(),
    maxSpend: draft.maxSpend.trim(),
    maxTokens: tokensWire(draft.maxTokens),
    maxTokensPerHour: tokensWire(draft.maxTokensPerHour),
    untilAt: draft.untilAt.trim(),
    tags: runnerTagsRequest(draft.tags),
  };
}

/** The runner-level settings card's fields: what the runner may work. Strings,
    as `RunnerBounds` is. */
export interface RunnerSettings {
  tags: string;
  under: string;
}

/** What a row's tags and under look like in the form that edits them. */
export function settingsOf(runner: Runner): RunnerSettings {
  return { tags: runnerTagsDraft(runner.tags), under: runner.under ?? '' };
}

/** Whether the settings form is holding anything the row is not. */
export function settingsChanged(draft: RunnerSettings, runner: Runner): boolean {
  return (
    draft.under.trim() !== (runner.under ?? '').trim() || !sameTags(runnerTagsRequest(draft.tags), runner.tags)
  );
}

/** The settings form as a request: exactly `tags` and `under`, so saving it
    leaves every ceiling alone. */
export function settingsRequest(draft: RunnerSettings): RunnerPatchRequest {
  return { tags: runnerTagsRequest(draft.tags), under: draft.under.trim() };
}

/** The bounds card's five ceilings, as `RunnerBounds` holds them. */
export type RunnerCeilings = Omit<RunnerBounds, 'under' | 'tags'>;

/** What a row's ceilings look like in the form that edits them. */
export function ceilingsOf(runner: Runner): RunnerCeilings {
  const { maxRuns, maxSpend, maxTokens, maxTokensPerHour, untilAt } = boundsOf(runner);
  return { maxRuns, maxSpend, maxTokens, maxTokensPerHour, untilAt };
}

/** Whether the bounds form is holding anything the row is not - its own five
    fields only, so tags and under never make it dirty. */
export function ceilingsChanged(draft: RunnerCeilings, runner: Runner): boolean {
  return boundsChanged({ ...boundsOf(runner), ...draft }, runner);
}

/** The bounds form as a request: exactly the five ceilings, every one sent
    for the reason `boundsRequest` gives, and never `tags` or `under`. */
export function ceilingsRequest(draft: RunnerCeilings): RunnerPatchRequest {
  const { maxRuns, maxSpend, maxTokens, maxTokensPerHour, untilAt } = boundsRequest({
    ...draft,
    under: '',
    tags: '',
  });
  return { maxRuns, maxSpend, maxTokens, maxTokensPerHour, untilAt };
}

/** What a runner's live sessions have spent between them. A session that has
    not reported a figure (an older CLI) counts as none. */
export function sessionTotals(sessions: RunnerSession[]): {
  runs: number;
  spent: number;
  tokens: number;
  tokensLastHour: number;
} {
  const sum = (pick: (session: RunnerSession) => number | null | undefined) =>
    sessions.reduce((total, session) => total + (pick(session) ?? 0), 0);

  return {
    runs: sum((s) => s.runs),
    spent: sum((s) => s.spent),
    tokens: sum((s) => s.tokensUsed),
    tokensLastHour: sum((s) => s.tokensLastHour),
  };
}

/** The projects its sessions declare with `--project`, once each, sorted. */
export function declaredProjects(sessions: RunnerSession[]): string[] {
  return [...new Set(sessions.flatMap((session) => session.projects ?? []))].sort();
}

/** Whether each session runs dangerous tickets, as its loop declared with
    `--dangerous`. A session that does not say (an older CLI) is left out
    rather than drawn as a No it never gave. */
export function declaredDangerous(sessions: RunnerSession[]): { where: string | null; dangerous: boolean }[] {
  return sessions.flatMap((session) =>
    session.refusesDangerous === null || session.refusesDangerous === undefined
      ? []
      : [{ where: session.where, dangerous: session.refusesDangerous === false }],
  );
}

/** A token field's hint: the shared wording, unless the field is inheriting
    the site default, in which case it names the figure the empty box
    inherits - the same words `budgetLine` uses for the same window, so the
    hint and the budget line never disagree. */
export function tokenFieldHint(runner: Runner, field: 'maxTokens' | 'maxTokensPerHour'): string {
  const source = field === 'maxTokens' ? runner.maxTokensSource : runner.maxTokensPerHourSource;
  if (source !== 'default') return TOKEN_FIELD_HINT;
  const cap = runner[field];
  const window = field === 'maxTokens' ? 'tokens' : 'per hour';
  return `Empty inherits the default: ${compactTokens(cap as number)} ${window}.`;
}

/** Which token box a server refusal is about, so the form can mark it - or
    null for a refusal about anything else. `--max-tokens-per-hour` is asked
    first because it contains `--max-tokens`. */
export function tokenFieldOf(refusal: string | null): 'maxTokens' | 'maxTokensPerHour' | null {
  if (refusal === null) return null;
  if (refusal.includes('--max-tokens-per-hour')) return 'maxTokensPerHour';
  return refusal.includes('--max-tokens') ? 'maxTokens' : null;
}

/** A token box as it reads once left: grouped with commas (`2M` becomes
    `2,000,000`) where it holds a count, and exactly as typed where it is empty
    or not one - the problem line says what is wrong with it, and rewriting it
    would take the evidence away. */
export function groupedTokens(text: string): string {
  const count = parseTokenCount(tokensWire(text));
  return count === null ? text : thousands(count);
}

/** The last press a runner's row was refused, and which row it was. `routed`
    is true for a bounds save, the one press the form can show a refusal
    under; any other press has no form open to show it in. */
export interface Refusal {
  runner: string;
  text: string;
  routed: boolean;
}

/** The refusal the top of the page draws: all of them, except a bounds save's
    that names a token flag, which the form draws under its field instead. */
export function topRefusal(failure: Refusal | null): string | null {
  if (failure === null) return null;
  return failure.routed && tokenFieldOf(failure.text) !== null ? null : failure.text;
}

/** The refusal one runner's form draws under a token field, or null where
    the refusal was another runner's, another flag's, or not a bounds save's. */
export function formRefusal(
  failure: Refusal | null,
  runner: string,
): { text: string; field: 'maxTokens' | 'maxTokensPerHour' } | null {
  if (failure === null || !failure.routed || failure.runner !== runner) return null;
  const field = tokenFieldOf(failure.text);
  return field === null ? null : { text: failure.text, field };
}

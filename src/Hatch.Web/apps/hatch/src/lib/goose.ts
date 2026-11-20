/* What the goose's page says, and in what order.

   Everything decidable lives here rather than in the components, as
   attention.ts does: how long a sweep took, how a run's row reads, how many
   findings were open when it last ran, how long a finding lasted, and which
   runner reported each session on a bug. Each is a pure function, pinned in
   goose.test.ts. */

import { gooseStoppedWords, waitedWords } from './attention';
import type {
  AttentionGoose,
  GooseFinding,
  GooseFindingEvidence,
  GooseRun,
  Issue,
  IssueEvent,
  Me,
  Status,
  WorkLogEntry,
} from '../types';

const count = (n: number, noun: string): string => `${n} ${noun}${n === 1 ? '' : 's'}`;

/** `a moment ago`, `12 minutes ago`: `waitedWords` said as a time that has passed. */
export function agoWords(at: string, now: Date): string {
  const waited = waitedWords(at, now);
  return waited === 'just asked' || waited === 'waiting' ? 'a moment ago' : `${waited} ago`;
}

/**
 * The line at the top of the page: whether the goose is running and when it
 * last swept. A stopped goose says so in `gooseStoppedWords`' own sentence,
 * the one the attention panel uses. `null` until the server has answered, so
 * nothing is claimed about a goose nobody has asked after.
 */
export function gooseStateWords(goose: AttentionGoose | null | undefined, now: Date): string | null {
  if (!goose) return null;
  if (goose.stopped) return gooseStoppedWords(goose, now);
  if (goose.lastSweepAt === null) return 'The goose is running. It has not swept yet.';
  return `The goose is running. It last swept ${agoWords(goose.lastSweepAt, now)}.`;
}

/**
 * How long a sweep took: `340 ms`, `1.2 s`, `14 s`, `2 min 5 s`.
 *
 * Its own rather than `durationPhrase` in workLog.ts, which reads anything
 * under a second as `0s` - and a sweep is usually well under one.
 */
export function sweepWords(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return '0 ms';
  if (ms < 1_000) return `${Math.round(ms)} ms`;
  if (ms < 10_000) return `${(Math.round(ms / 100) / 10).toFixed(1)} s`;

  const seconds = Math.round(ms / 1_000);
  if (seconds < 60) return `${seconds} s`;

  return `${Math.floor(seconds / 60)} min ${seconds % 60} s`;
}

/** The pieces of a run's row, each a short phrase the component lays out. */
export interface RunRowWords {
  /** When it first and last ran, `first ran 3 hours ago, last ran 2 minutes ago`. */
  when: string;
  sweeps: string;
  slowest: string;
  checks: string;
  open: string;
}

/** `1 sweep`, `12 sweeps`. */
export const sweepCount = (n: number): string => count(n, 'sweep');

/**
 * How a run's row reads. `open` is how many findings were open at the run's
 * last sweep, which `findingsOpenAt` counts. A server that predates the check
 * counts leaves them out, and the row says nothing of them rather than that
 * none ran.
 */
export function runRowWords(run: GooseRun, open: number, now: Date): RunRowWords {
  const sameInstant = run.firstRanAt === run.lastRanAt;

  let checks = '';
  if (run.checks !== undefined) {
    checks = run.checks === 0 ? 'no checks' : count(run.checks, 'check');
    if (run.checksFailed) checks += `, ${run.checksFailed} failed`;
  }

  return {
    when: sameInstant
      ? `ran ${agoWords(run.lastRanAt, now)}`
      : `first ran ${agoWords(run.firstRanAt, now)}, last ran ${agoWords(run.lastRanAt, now)}`,
    sweeps: sweepCount(run.sweeps),
    slowest: `slowest ${sweepWords(run.longestMs)}`,
    checks,
    open: open === 0 ? 'no findings open' : `${count(open, 'finding')} open`,
  };
}

/**
 * How many findings were open when a run last swept: opened by then, and not
 * closed until after. The last sweep because a run's entries are the latest
 * sweep's, so this is the count that sits beside them.
 */
export function findingsOpenAt(findings: GooseFinding[], run: GooseRun): number {
  const at = Date.parse(run.lastRanAt);
  if (Number.isNaN(at)) return 0;

  return findings.filter((f) => {
    if (Date.parse(f.openedAt) > at) return false;
    return f.closedAt === null || Date.parse(f.closedAt) > at;
  }).length;
}

/** Whether a finding has stopped being true. */
export const isClosed = (f: GooseFinding): boolean => f.closedAt !== null;

/** `4 minutes`, `2 hours`, `3 days`, and `under a minute` below the first of them. */
function spanWords(ms: number): string {
  const minutes = Math.floor(Math.max(0, ms) / 60_000);
  if (minutes < 1) return 'under a minute';
  if (minutes < 60) return count(minutes, 'minute');

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return count(hours, 'hour');

  return count(Math.floor(hours / 24), 'day');
}

/**
 * How long a closed finding was true, from when its condition began to when a
 * sweep stopped seeing it. `null` for an open one, or when a date cannot be read.
 */
export function findingLasted(f: GooseFinding): string | null {
  if (f.closedAt === null) return null;

  const began = Date.parse(f.beganAt);
  const closed = Date.parse(f.closedAt);
  if (Number.isNaN(began) || Number.isNaN(closed)) return null;

  return spanWords(closed - began);
}

/**
 * The time part of a finding's row. An open one says how long since it began:
 * `began 3 hours ago`. A closed one says how long it lasted and when it
 * closed: `lasted 3 hours, closed 2 days ago`.
 */
export function findingTimeWords(f: GooseFinding, now: Date): string {
  if (f.closedAt === null) return `began ${agoWords(f.beganAt, now)}`;

  const lasted = findingLasted(f);
  const closed = `closed ${agoWords(f.closedAt, now)}`;
  return lasted === null ? closed : `lasted ${lasted}, ${closed}`;
}

/**
 * The finding's evidence, one label and value to a line, in the order the
 * check gave it. A line with neither is dropped: nothing is gained by a row of
 * blanks.
 */
export function evidenceLines(f: GooseFinding): GooseFindingEvidence[] {
  return f.evidence
    .map((e) => ({ label: e.label.trim(), value: e.value.trim() }))
    .filter((e) => e.label !== '' || e.value !== '');
}

/** Open findings first, longest-standing first; the server's own order, kept for a list this page splits. */
export const openFindings = (findings: GooseFinding[]): GooseFinding[] => findings.filter((f) => !isClosed(f));

/** Closed findings, the one that closed last first. */
export const closedFindings = (findings: GooseFinding[]): GooseFinding[] =>
  findings
    .filter(isClosed)
    .sort((a, b) => Date.parse(b.closedAt as string) - Date.parse(a.closedAt as string) || b.id - a.id);

/** Runs, newest first by their last sweep. */
export const runsNewestFirst = (runs: GooseRun[]): GooseRun[] =>
  [...runs].sort((a, b) => Date.parse(b.lastRanAt) - Date.parse(a.lastRanAt) || b.id - a.id);

/** The column a bug stands in, by name, or null when the column is not among those read. */
export const bugColumnName = (statuses: Status[], issue: Pick<Issue, 'statusId'>): string | null =>
  statuses.find((s) => s.id === issue.statusId)?.name ?? null;

/** One session's report on a bug: what it said it did, when, and which runner. */
export interface SessionReport {
  id: number;
  title: string;
  /** Empty when the session never said. */
  summary: string;
  at: string;
  /** The runner that released the session, or null when no release names it. */
  runner: string | null;
}

/** What a session that never said what it did is shown as. */
export const UNDESCRIBED_TITLE = 'Did not say what it did';

/**
 * What each session on a bug reported, newest first.
 *
 * The runner is not on a work log row - `agent` there is the tool - but the
 * release that ended the session wrote a `claim_released` event naming both the
 * runner and the session, so the two are joined here. A session with no such
 * release, as with a runner from before the release named itself, shows no
 * runner rather than a guess.
 */
export function sessionReports(entries: WorkLogEntry[], events: IssueEvent[]): SessionReport[] {
  const runners = new Map<string, string>();
  for (const e of events) {
    if (e.kind !== 'claim_released' || !e.payload) continue;
    const { sessionId, runner } = e.payload;
    if (typeof sessionId === 'string' && typeof runner === 'string' && runner !== '' && !runners.has(sessionId)) {
      runners.set(sessionId, runner);
    }
  }

  return [...entries]
    .sort((a, b) => Date.parse(b.endedAt) - Date.parse(a.endedAt) || b.id - a.id)
    .map((e) => ({
      id: e.id,
      title: e.described && e.title ? e.title : UNDESCRIBED_TITLE,
      summary: e.described && e.summary ? e.summary : '',
      at: e.endedAt,
      runner: runners.get(e.sessionId) ?? null,
    }));
}

/** The three empty states, one sentence each. */
export const noRunsWords = (): string => 'The goose has not run yet.';
export const noOpenFindingsWords = (): string => 'Nothing is open. The goose sees nothing standing past its bound.';
export const noClosedFindingsWords = (): string => 'Nothing has closed in the days kept.';

/** Said where the runs would be, to a caller who reaches only some of the projects. */
export const runsCoverWholeBoardWords = (): string =>
  'Runs cover the whole board, so they are shown only to a caller who reaches all of it.';

/**
 * Whether this caller may read the runs. Absent scope is a server that
 * predates it, which reads as everything, as `Me.scope` says.
 */
export const runsCoverWholeBoard = (me: Pick<Me, 'scope'> | null | undefined): boolean =>
  !me?.scope || me.scope.mode === 'all';

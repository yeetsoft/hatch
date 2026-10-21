/* The work log's arithmetic and every sentence it says, apart from the
   component the way utilization.ts is apart from the battery.

   The hatch app has no DOM test setup and this story does not add one. The
   house pattern instead is that everything worth being wrong about lives here
   and is tested here, and the component is left thin enough to read.

   Three of these are only interesting at their edges, which is where the bugs
   are:

   - Tokens are the headline and dollars are secondary, because on a
     subscription the dollars are notional list price rather than money that
     left an account. But a session that cost a third of a cent must not read as
     free, so the money has enough places to say so.
   - "Undescribed" and "errored" are two different marks and a row can carry
     both. One predicate answers which, so the component never grows two
     conditions that can disagree.
   - Whether there is a work log at all is a question about the subtree, not
     about the entries: an epic whose stories cost money has a log worth drawing
     with no session of its own. */

import { shortSha } from './playbookFiles';
import type { Status, WorkLog, WorkLogEntry, WorkLogTotals } from '../types';

/**
 * The headline figure, short enough to sit in a row: `1.4M`, `912k`, `840`.
 *
 * One decimal place in the millions and none below, because the difference
 * between 1.4M and 1.5M is a decision and the difference between 912k and 913k
 * is not.
 */
export function compactTokens(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return '0';

  if (n >= 1_000_000) {
    const millions = Math.round(n / 100_000) / 10;
    return `${millions}M`;
  }

  if (n >= 1_000) return `${Math.round(n / 1_000)}k`;

  return `${Math.round(n)}`;
}

/** `compactTokens`, or a dash for a figure no run ever reported - an older
    runner's row, or one from a `--quiet` run. */
export const compactTokensOrDash = (n: number | null): string => (n === null ? '—' : compactTokens(n));

/**
 * The secondary figure: `$3.41`, `$0.0012`, `$0`.
 *
 * Four places under a cent rather than two, so a cheap session does not read as
 * free - a night of them adds up, and a column of `$0.00` would say the
 * opposite. Notional list price throughout; nothing here claims money moved.
 */
export function moneyPhrase(usd: number): string {
  if (!Number.isFinite(usd) || usd <= 0) return '$0';

  return usd >= 0.01 ? `$${usd.toFixed(2)}` : `$${usd.toFixed(4)}`;
}

/** Why a cost reads as a dash instead of a number - the title that goes on it. */
export const UNPRICED_TITLE = 'The agent reported no price for this session';

/** `moneyPhrase`, or a dash for a session whose agent reported no price at all -
    not the same as a real `$0`, which `moneyPhrase` still draws as `$0`. */
export const costPhrase = (usd: number | null): string => (usd === null ? '—' : moneyPhrase(usd));

/** The title that belongs beside `costPhrase`'s dash, explaining it - undefined
    for a priced row, which needs no explaining. */
export const costTitle = (usd: number | null): string | undefined => (usd === null ? UNPRICED_TITLE : undefined);

/**
 * How long it ran: `14m02s`, `1h02m`, `4s`.
 *
 * The minutes-and-seconds shape matches the `clock` the terminal prints at the
 * end of a run, so the line somebody watched and the row they read afterwards
 * are the same number said the same way.
 */
export function durationPhrase(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return '0s';

  const seconds = Math.floor(ms / 1_000);
  if (seconds < 60) return `${seconds}s`;

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m${pad(seconds % 60)}s`;

  return `${Math.floor(minutes / 60)}h${pad(minutes % 60)}m`;
}

const pad = (n: number): string => (n < 10 ? `0${n}` : `${n}`);

/**
 * The one string that gets copied, built here rather than assembled in JSX, so
 * what is on screen and what lands on the clipboard cannot drift apart.
 * Null for an agent this table does not recognise, including none at all - the
 * caller draws the bare session id instead.
 */
const RESUME_COMMANDS: Record<string, (sessionId: string) => string> = {
  claude: (id) => `claude --resume ${id}`,
  codex: (id) => `codex exec resume ${id}`,
  grok: (id) => `grok -r ${id}`,
  ollama: (id) => `codex exec resume --oss --local-provider ollama ${id}`,
};

export function resumeCommand(agent: string | null, sessionId: string): string | null {
  if (agent === null) return null;
  const build = RESUME_COMMANDS[agent];
  return build ? build(sessionId) : null;
}

/** What is wrong with an entry, if anything - one predicate rather than two
    conditions in a component. An errored run is the louder fact of the two, so
    it wins when a row is both.

    Takes the two fields it reads rather than a whole `WorkLogEntry`, so the
    leaderboard's narrower session row passes through the same predicate. A
    second copy of this in a second file is how a row on one page and the same
    row on another come to be marked differently. */
export function entryMark(entry: { isError: boolean; described: boolean }): 'error' | 'undescribed' | null {
  if (entry.isError) return 'error';
  return entry.described ? null : 'undescribed';
}

/** What an undescribed entry is called, instead of nothing. Narrowed for the
    reason `entryMark` is. */
export const entryTitle = (entry: { described: boolean; title: string | null }): string =>
  entry.described && entry.title ? entry.title : 'A session that did not say what it did';

/**
 * Whether to draw the section at all.
 *
 * Entries **or** a non-zero subtree total: an epic whose stories cost money has
 * a work log worth drawing even with no session of its own. An issue with
 * neither renders nothing at all rather than an empty section - a heading over
 * a blank space is a page saying "this feature exists and has failed you".
 */
export const showsWorkLog = (log: WorkLog | null): log is WorkLog =>
  log !== null && (log.entries.length > 0 || log.totals.sessions > 0);

/**
 * Whether the subtree's total is more than this issue's own, which is the one
 * case where drawing a single number would let one figure pass for the other.
 *
 * Asked of the session count and not of the tokens: an epic with a session of
 * its own and a story with a session of its own are two rows on two pages, and
 * that is true however cheap either was.
 */
export const hasDescendantSpend = (log: WorkLog): boolean => log.totals.sessions > log.own.sessions;

/** What the totals line says: `3 sessions · 1.4M tokens · $3.41`. Singular at
    one, because "1 sessions" is the kind of thing that makes a page look
    unfinished.

    Takes the totals rather than the whole log, so an issue's subtree and a
    leaderboard filter say their numbers the same way. `1.4M` on one page and
    `1,433,201` on another is one number in two vocabularies. */
export const totalsPhrase = (totals: WorkLogTotals): string =>
  [
    `${totals.sessions} ${totals.sessions === 1 ? 'session' : 'sessions'}`,
    `${compactTokens(totals.totalTokens)} tokens`,
    ...(totals.sessions - totals.unpricedSessions > 0 ? [moneyPhrase(totals.costUsd)] : []),
    ...(totals.unpricedSessions > 0 ? [`${totals.unpricedSessions} unpriced`] : []),
  ].join(' · ');

/** The same for what this issue spent on its own, said only when it differs -
    see `hasDescendantSpend`. */
export const ownPhrase = (log: WorkLog): string =>
  log.own.sessions === 0
    ? 'none of it on this issue itself'
    : `${compactTokens(log.own.totalTokens)} of it on this issue itself`;

/** What a row says about where its playbook came from - the boundary this
    story makes visible every time it is crossed. Null for a hop's row (no
    playbook at all) or one an older runner wrote (none of the four fields
    sent). */
export function playbookSourcePhrase(
  entry: Pick<WorkLogEntry, 'playbookSource' | 'playbookFileTrunk' | 'playbookFileSha'>,
): string | null {
  switch (entry.playbookSource) {
    case 'file':
      return entry.playbookFileTrunk && entry.playbookFileSha
        ? `playbooks from .hatch/playbooks.yaml on ${entry.playbookFileTrunk} at ${shortSha(entry.playbookFileSha)}`
        : null;
    case 'project':
      return "playbooks from this project's own table row";
    case 'board':
      return 'playbooks from the board-wide table';
    default:
      return null;
  }
}

/** What a row says about the column it moved between - the transition its
    playbook ran. Null only when the row carries no transition at all: an
    older runner's row, with both ids null.

    A status id the board no longer carries (a deleted column) still falls
    back to the raw numeric id rather than null or a thrown error - a deleted
    column is still a fact about the row, the same reason `PlaybookFileRowLine`
    falls back to a name string instead of hiding its row; a work-log row just
    has no stored name to fall back to, only the id. */
export function transitionPhrase(
  entry: Pick<WorkLogEntry, 'playbookFromStatusId' | 'playbookToStatusId'>,
  statuses: Status[],
): string | null {
  if (entry.playbookFromStatusId === null && entry.playbookToStatusId === null) return null;

  const name = (id: number | null): string | number => {
    if (id === null) return '';
    return statuses.find((s) => s.id === id)?.name ?? id;
  };

  return `${name(entry.playbookFromStatusId)} → ${name(entry.playbookToStatusId)}`;
}

/** What a row says about the model and effort its session ran at, e.g.
    `"opus/high"`. Null for the reason `transitionPhrase` is - an older
    runner's row. The CLI only ever sends the two together, so there is no
    partial case to decide. */
export function modelEffortPhrase(entry: Pick<WorkLogEntry, 'playbookModel' | 'playbookEffort'>): string | null {
  if (entry.playbookModel === null && entry.playbookEffort === null) return null;
  return `${entry.playbookModel}/${entry.playbookEffort}`;
}

/** The `<summary>` of a row's reads disclosure, e.g. `"Read 12 paths"`. Only
    called for a non-empty list - an entry that read nothing draws no
    disclosure at all. */
export const readsLabel = (reads: WorkLogEntry['reads']): string =>
  `Read ${reads.length} ${reads.length === 1 ? 'path' : 'paths'}`;

/** The command that shows the exact bytes a file-sourced row's playbook was
    read from - the full sha, not the short one, since this is what actually
    runs. Built here rather than in JSX for the same reason resumeCommand is:
    what is on screen and what lands on the clipboard must not drift apart. */
export const playbookFileShowCommand = (sha: string): string => `git show ${sha}:.hatch/playbooks.yaml`;

/** How many of the sessions in the total ended badly, or null when none did.
    Null rather than "0 errors": a log with nothing wrong should say nothing.
    Narrowed for the reason `totalsPhrase` is. */
export const errorPhrase = (totals: WorkLogTotals): string | null =>
  totals.errors === 0
    ? null
    : `${totals.errors} ${totals.errors === 1 ? 'session' : 'sessions'} ended with an error`;

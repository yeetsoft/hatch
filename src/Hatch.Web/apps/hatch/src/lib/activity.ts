/* The activity report's own words and wiring: who the page opens on, what the
   URL says about the actor, the picker's rows, and the headline, rundown and
   truncation sentences. Nothing here touches React or the DOM, so what can be
   wrong lives in a function with a test beside it.

   The window is lib/reportWindow.ts's and the project filter lib/timeline.ts's;
   the page imports them from there and nothing here copies them. */

import { compareAssignees } from './assignee';
import type { ActivityKindCount, ActivityReport, Assignee, AssigneeDirectory } from '../types';

const MINUTE_MS = 60_000;

/** The URL param naming whose events the report reads: the actor's name, which
    is what an event's `actor` holds. */
export const ACTOR = 'actor';

/** How many kinds the headline names before it folds the rest into a tail. */
const HEADLINE_KINDS = 3;

/**
 * Whose rundown the page reads, and where that came from.
 *
 * A non-blank `?actor=` wins and is returned **as given**, untrimmed - the
 * server matches the actor exactly - whether or not the directory knows them,
 * so a link to a revoked key's trail still reads. A blank or missing one falls
 * to the signed-in person, which the directory reports only when they are in it;
 * with neither, `actor` is null and the page says to choose.
 */
export function resolveActor(
  params: URLSearchParams,
  directory: Pick<AssigneeDirectory, 'me'>,
): { actor: string | null; fromUrl: boolean } {
  const raw = params.get(ACTOR);
  if (raw !== null && raw.trim() !== '') return { actor: raw, fromUrl: true };

  return { actor: directory.me?.name ?? null, fromUrl: false };
}

/** A copy of `params` with the actor set. Every other param is kept; the input
    is never mutated. */
export function withActor(params: URLSearchParams, name: string): URLSearchParams {
  const next = new URLSearchParams(params);
  next.set(ACTOR, name);

  return next;
}

/**
 * The picker's rows: people first and then keys, each A→Z and each by name,
 * the value being the name too since that is all an event records. A person
 * and a key sharing a name are one row - the actor string cannot tell them
 * apart. `current` is appended when nobody in the directory has it, so a link
 * to someone gone still draws something chosen.
 */
export function actorOptions(
  assignees: Assignee[],
  current: string | null,
): { value: string; text: string }[] {
  const seen = new Set<string>();
  const rows: { value: string; text: string }[] = [];

  for (const assignee of [...assignees].sort(compareAssignees)) {
    if (seen.has(assignee.name)) continue;
    seen.add(assignee.name);
    rows.push({ value: assignee.name, text: assignee.name });
  }

  if (current !== null && !seen.has(current)) rows.push({ value: current, text: current });

  return rows;
}

/* Kind → [one, many]. The common kinds in the words a person says; anything
   else falls back to the kind's own name spaced out, so a kind added later
   never draws blank. */
const KIND_WORDS: Record<string, [string, string]> = {
  commented: ['comment', 'comments'],
  status_changed: ['move', 'moves'],
  created: ['created', 'created'],
  retitled: ['retitle', 'retitles'],
  redescribed: ['description edit', 'description edits'],
  retyped: ['retype', 'retypes'],
  parent_changed: ['reparent', 'reparents'],
  assignee_changed: ['assignment', 'assignments'],
  priority_changed: ['priority change', 'priority changes'],
  dependency_added: ['dependency added', 'dependencies added'],
  dependency_removed: ['dependency removed', 'dependencies removed'],
  pull_request_changed: ['pull request change', 'pull request changes'],
  claim_taken: ['claim taken', 'claims taken'],
  claim_released: ['claim released', 'claims released'],
  messaged: ['message', 'messages'],
  asked: ['question', 'questions'],
  answered: ['answer', 'answers'],
};

/** One kind and its count in words: `1 comment`, `9 moves`, `6 created`. */
export function kindWords(kind: string, count: number): string {
  const [one, many] = KIND_WORDS[kind] ?? [kind.replaceAll('_', ' '), kind.replaceAll('_', ' ')];

  return `${count} ${count === 1 ? one : many}`;
}

/** Every kind in `kinds`, in the order given, joined: what was done to one
    issue. */
export const kindsWords = (kinds: ActivityKindCount[]): string =>
  kinds.map((k) => kindWords(k.kind, k.count)).join(', ');

/**
 * The headline: the total and the top three kinds, in the server's order (most
 * first, then by name). Further kinds are one tail, `and 4 others`, counting
 * their events - the number of events, not of kinds.
 *
 *   41 events: 12 comments, 9 moves, 6 created, and 14 others
 */
export function headlineWords(report: Pick<ActivityReport, 'total' | 'kinds'>): string {
  const events = `${report.total} ${report.total === 1 ? 'event' : 'events'}`;
  if (report.kinds.length === 0) return events;

  const top = report.kinds.slice(0, HEADLINE_KINDS).map((k) => kindWords(k.kind, k.count));
  const rest = report.kinds.slice(HEADLINE_KINDS).reduce((sum, k) => sum + k.count, 0);
  if (rest > 0) top.push(`and ${rest} ${rest === 1 ? 'other' : 'others'}`);

  return `${events}: ${top.join(', ')}`;
}

/** The empty range, as a sentence rather than an empty table. */
export const emptyWords = (actor: string): string => `${actor} did nothing in this window.`;

/** What the page says when there is nobody to read. */
export const chooseActorWords = (): string => 'Choose whose activity to read.';

/** The line under the trail when the listing is cut. */
export const truncatedWords = (report: Pick<ActivityReport, 'total' | 'events'>): string =>
  `Showing the newest ${report.events.length} of ${report.total} events; the rundown above covers all of them.`;

/**
 * An instant in the reader's own clock, as `4 Nov 2026, 14:05`. `offsetMinutes`
 * (minutes east of UTC) shifts it onto the reader's wall clock, which is then
 * formatted as UTC so the runtime's zone never moves it, as `bucketLabel` does.
 * The optional locale exists so tests can pin one.
 */
export function touchWords(iso: string, offsetMinutes: number, locale?: string): string {
  const shifted = new Date(Date.parse(iso) + offsetMinutes * MINUTE_MS);
  const date = shifted.toLocaleDateString(locale, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  });
  const time = shifted.toLocaleTimeString(locale, {
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'UTC',
  });

  return `${date}, ${time}`;
}

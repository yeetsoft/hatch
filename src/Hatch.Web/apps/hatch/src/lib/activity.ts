/* The activity report's own words and wiring: who the page opens on, what the
   URL says about the actor, the picker's rows, and the headline, rundown and
   truncation sentences. Nothing here touches React or the DOM, so what can be
   wrong lives in a function with a test beside it.

   The window is lib/reportWindow.ts's and the project filter lib/timeline.ts's;
   the page imports them from there and nothing here copies them. */

import { compareAssignees } from './assignee';
import type {
  ActivityEstimate,
  ActivityKindCount,
  ActivityReport,
  ActivitySession,
  Assignee,
  AssigneeDirectory,
} from '../types';

const MINUTE_MS = 60_000;

/** The URL param naming whose events the report reads: the actor's name, which
    is what an event's `actor` holds. */
export const ACTOR = 'actor';

/** The URL params naming the session-bracketing parameters, mirroring the
    API's own `gap`/`lead` query parameters and their defaults and bounds. */
export const GAP = 'gap';
export const LEAD = 'lead';
export const DEFAULT_GAP = 30;
export const DEFAULT_LEAD = 15;
const GAP_BOUNDS = { min: 1, max: 1440 };
const LEAD_BOUNDS = { min: 0, max: 240 };

const inBounds = (n: number, bounds: { min: number; max: number }): boolean =>
  Number.isInteger(n) && n >= bounds.min && n <= bounds.max;

function resolveMinutes(raw: string | null, fallback: number, bounds: { min: number; max: number }): number {
  if (raw === null) return fallback;
  const n = Number(raw);

  return inBounds(n, bounds) ? n : fallback;
}

/** The gap, off the URL: the integer in range, else `DEFAULT_GAP` - never
    refuses, as `resolveWindow` doesn't. */
export const resolveGap = (params: URLSearchParams): number =>
  resolveMinutes(params.get(GAP), DEFAULT_GAP, GAP_BOUNDS);

/** The lead, off the URL: the integer in range, else `DEFAULT_LEAD` - never
    refuses, as `resolveWindow` doesn't. */
export const resolveLead = (params: URLSearchParams): number =>
  resolveMinutes(params.get(LEAD), DEFAULT_LEAD, LEAD_BOUNDS);

/** Whether `n` is a gap the server would accept - what a committed input is
    checked against before it is sent. */
export const isValidGap = (n: number): boolean => inBounds(n, GAP_BOUNDS);

/** Whether `n` is a lead the server would accept - what a committed input is
    checked against before it is sent. */
export const isValidLead = (n: number): boolean => inBounds(n, LEAD_BOUNDS);

/** A copy of `params` with the gap set. Every other param is kept; the input
    is never mutated. */
export function withGap(params: URLSearchParams, gap: number): URLSearchParams {
  const next = new URLSearchParams(params);
  next.set(GAP, String(gap));

  return next;
}

/** A copy of `params` with the lead set. Every other param is kept; the input
    is never mutated. */
export function withLead(params: URLSearchParams, lead: number): URLSearchParams {
  const next = new URLSearchParams(params);
  next.set(LEAD, String(lead));

  return next;
}

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
export const emptyWords = (actor: string): string => `Nothing recorded for ${actor} in this window.`;

/** What the page says when there is nobody to read. */
export const chooseActorWords = (): string => 'Choose whose activity to read.';

/** The line under the trail when the listing is cut. */
export const truncatedWords = (report: Pick<ActivityReport, 'total' | 'events'>): string =>
  `Showing the newest ${report.events.length} of ${report.total} events; the rundown above covers all of them.`;

/** An instant shifted onto the reader's wall clock (minutes east of UTC),
    split into its date and time, each formatted as UTC so the runtime's own
    zone never moves it a second time. Shared by `touchWords` and
    `sessionWhenWords`. */
function shiftedParts(iso: string, offsetMinutes: number, locale?: string): { date: string; time: string } {
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

  return { date, time };
}

/**
 * An instant in the reader's own clock, as `4 Nov 2026, 14:05`. `offsetMinutes`
 * (minutes east of UTC) shifts it onto the reader's wall clock, which is then
 * formatted as UTC so the runtime's zone never moves it, as `bucketLabel` does.
 * The optional locale exists so tests can pin one.
 */
export function touchWords(iso: string, offsetMinutes: number, locale?: string): string {
  const { date, time } = shiftedParts(iso, offsetMinutes, locale);

  return `${date}, ${time}`;
}

/**
 * The totals in `estimate` as one headline sentence:
 * `3 sessions, about 2.5 hours in this window`. Singular below 2 sessions or
 * 1.05 hours.
 */
export function estimateHeadlineWords(estimate: Pick<ActivityEstimate, 'sessions' | 'minutes'>): string {
  const hours = estimate.minutes / 60;
  const sessionWord = estimate.sessions < 2 ? 'session' : 'sessions';
  const hourWord = hours < 1.05 ? 'hour' : 'hours';

  return `${estimate.sessions} ${sessionWord}, about ${hours.toFixed(1)} ${hourWord} in this window`;
}

/** The rule the headline's estimate was computed under, as one sentence, from
    `estimate.gapMinutes`/`estimate.leadMinutes` alone - never from the
    `gap`/`lead` inputs, which may differ from what the answer was computed
    with. */
export function estimateRuleWords(estimate: Pick<ActivityEstimate, 'gapMinutes' | 'leadMinutes'>): string {
  const gap = estimate.gapMinutes === 1 ? '1 minute' : `${estimate.gapMinutes} minutes`;
  const lead = estimate.leadMinutes === 1 ? '1 minute' : `${estimate.leadMinutes} minutes`;

  return (
    `A session ends after ${gap} of quiet; each is counted from its first event to its last, ` +
    `plus ${lead}.`
  );
}

/** A session's estimated length, rough and with no seconds: `45m`, `1h`,
    `1h30m`. A lone-event session's length is the lead alone. */
export function sessionLengthWords(minutes: number): string {
  const total = Math.round(minutes);
  if (total < 60) return `${total}m`;

  const hours = Math.floor(total / 60);
  const rest = total % 60;

  return rest === 0 ? `${hours}h` : `${hours}h${rest}m`;
}

/**
 * When a session started and ended, in the reader's own clock. The same
 * calendar day (after the offset shift) names the date once:
 * `4 Nov 2026, 09:00–10:30`; different days name both:
 * `4 Nov 2026, 23:30 – 5 Nov 2026, 00:15`.
 */
export function sessionWhenWords(
  session: Pick<ActivitySession, 'from' | 'to'>,
  offsetMinutes: number,
  locale?: string,
): string {
  const from = shiftedParts(session.from, offsetMinutes, locale);
  const to = shiftedParts(session.to, offsetMinutes, locale);

  if (from.date === to.date) return `${from.date}, ${from.time}–${to.time}`;

  return `${from.date}, ${from.time} – ${to.date}, ${to.time}`;
}

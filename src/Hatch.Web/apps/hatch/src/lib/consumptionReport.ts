/* The consumption report's own arithmetic: what the URL says about type,
   status and sort, how a run of milliseconds reads, what a row's dollars say,
   and every sentence the page speaks. Nothing here touches React or the DOM,
   so what can be wrong lives in a function with a test beside it.

   The window is lib/reportWindow.ts's and the project filter is lib/timeline.ts's
   (`PROJECT`, `reportProjects`, `withProjects`); the page imports each from
   there and nothing here copies either. */

import type { ConsumptionReport, ConsumptionSort, IssueType, WorkLogTotals } from '../types';
import { ISSUE_TYPES } from '../types';
import { dayLabel } from './leaderboard';
import { UNPRICED_TITLE, moneyPhrase, totalsPhrase } from './workLog';

/** How many rows the table asks for. The endpoint takes up to 500 and defaults
    to the same hundred; it is sent anyway so the page's cap is its own. */
export const TABLE_LIMIT = 100;

export const TYPE = 'type';
export const STATUS = 'status';
export const SORT = 'sort';

export type ConsumptionType = IssueType | 'all';

export const DEFAULT_TYPE: ConsumptionType = 'epic';
export const DEFAULT_SORT: ConsumptionSort = 'activity';

const TYPES: ConsumptionType[] = [...ISSUE_TYPES, 'all'];
const SORTS: ConsumptionSort[] = ['activity', 'tokens', 'cost', 'time'];

/** The type the URL names, or `epic`: trimmed and lower-cased first, and
    anything that is not one of the five types or `all` is a typo, not an error. */
export function resolveType(params: URLSearchParams): ConsumptionType {
  const raw = params.get(TYPE)?.trim().toLowerCase();

  return TYPES.find((type) => type === raw) ?? DEFAULT_TYPE;
}

/** A copy of `params` with the type set, or removed at `epic` - the default is
    not written to the URL. Every other param is kept; the input is never
    mutated. */
export function withType(params: URLSearchParams, type: ConsumptionType): URLSearchParams {
  const next = new URLSearchParams(params);
  if (type === DEFAULT_TYPE) next.delete(TYPE);
  else next.set(TYPE, type);

  return next;
}

/** The sort the URL names, or `activity`. */
export function resolveSort(params: URLSearchParams): ConsumptionSort {
  const raw = params.get(SORT)?.trim().toLowerCase();

  return SORTS.find((sort) => sort === raw) ?? DEFAULT_SORT;
}

/** A copy of `params` with the sort set, or removed at `activity`. */
export function withSort(params: URLSearchParams, sort: ConsumptionSort): URLSearchParams {
  const next = new URLSearchParams(params);
  if (sort === DEFAULT_SORT) next.delete(SORT);
  else next.set(SORT, sort);

  return next;
}

/** The column the URL names, by its numeric id, or null for every column. A
    column has no `key` the way a project does, and the id is what the API takes.
    Anything that is not a positive whole number is no column at all. */
export function resolveStatusId(params: URLSearchParams): number | null {
  const raw = params.get(STATUS)?.trim();
  if (!raw || !/^\d+$/.test(raw)) return null;

  const id = Number(raw);

  return Number.isSafeInteger(id) && id > 0 ? id : null;
}

/** A copy of `params` with the column set, or removed for `null` - every column
    is the default. */
export function withStatusId(params: URLSearchParams, id: number | null): URLSearchParams {
  const next = new URLSearchParams(params);
  if (id === null) next.delete(STATUS);
  else next.set(STATUS, String(id));

  return next;
}

const pad = (n: number): string => (n < 10 ? `0${n}` : `${n}`);

/**
 * How long the runners ran, said for a table cell: `48s`, `5m 03s`, `3h 12m`.
 *
 * `workLog.ts`'s `durationPhrase` says the same three shapes without the space,
 * as the terminal's clock does, and the Leaderboard keeps it; a report's column
 * of them reads better with one.
 */
export function humanDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return '0s';

  const seconds = Math.floor(ms / 1_000);
  if (seconds < 60) return `${seconds}s`;

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ${pad(seconds % 60)}s`;

  return `${Math.floor(minutes / 60)}h ${pad(minutes % 60)}m`;
}

/** What a row's dollars say: a dash with the leaderboard's title when no session
    of it was priced - never `$0`, which would be a price - the sum when all were,
    and the floor with the count beside it when some were not. */
export function rowCost(totals: WorkLogTotals): { text: string; title?: string } {
  if (totals.unpricedSessions >= totals.sessions) return { text: '—', title: UNPRICED_TITLE };

  const sum = moneyPhrase(totals.costUsd);
  if (totals.unpricedSessions === 0) return { text: sum };

  return { text: `${sum} (${totals.unpricedSessions} unpriced)` };
}

/** The line over the table, for every issue that matched and not only the rows
    drawn: `12 issues · 31 sessions · 1.4M tokens · $3.41 · 3h 12m`. */
export const totalsLine = (report: ConsumptionReport): string =>
  `${report.matched} issues · ${totalsPhrase(report.totals)} · ${humanDuration(report.totals.durationMs)}`;

/** The note above the table when it draws fewer rows than matched, or null when
    it draws them all - so a total larger than the rows reads as a cap rather
    than as an error. */
export function capWords(matched: number, shown: number): string | null {
  if (matched <= shown) return null;

  return `The first ${shown} of ${matched} issues. The totals cover all ${matched}.`;
}

/**
 * Which of the two empty states is true, or null when neither is - the
 * leaderboard's own distinction, told apart by `firstSessionAt` (which ignores
 * the window) rather than by a row count. `never` wins when both could speak.
 */
export function emptiness(
  report: ConsumptionReport,
  locale?: string,
): { kind: 'never' | 'range'; text: string } | null {
  if (report.firstSessionAt === null) {
    return { kind: 'never', text: 'Nothing has ever been logged under this filter.' };
  }

  if (report.totals.sessions > 0) return null;

  const last = report.lastSessionAt ?? report.firstSessionAt;

  return {
    kind: 'range',
    text:
      'No issue consumed anything in this window. ' +
      `The log runs from ${dayLabel(report.firstSessionAt, locale)} to ${dayLabel(last, locale)}.`,
  };
}

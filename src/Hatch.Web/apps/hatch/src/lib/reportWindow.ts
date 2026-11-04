/* A report's window - the two instants it reads between - and the arithmetic
   behind the controls that move it: stepping either way, back to today, and a
   length from a short list. Nothing here touches React or the DOM, so what can
   be wrong lives in a function with a test beside it.

   It reads `FROM`, `TO`, `RANGE` and `presetRange` from lib/leaderboard.ts and
   copies none of them. */

import { FROM, RANGE, TO, presetRange } from './leaderboard';

/** Ninety days: a quarter, which is wide enough to see a project's shape and
    narrow enough that the week ticks stay readable. */
export const DEFAULT_DAYS = 90;

const DAY_MS = 86_400_000;

/** A window as two ISO instants, the shape the URL and the API both take. Not
    `Window`, which is the DOM global. */
export type ReportWindow = { from: string; to: string };

export type WindowPreset = '1d' | '1w' | '2w' | '1m' | '3m' | '6m' | '1y' | 'all';

/** The `range` shorthand and the control's choices, narrowest first. This is
    not the leaderboard's 24h..30d vocabulary. 183 is not 182 or 184: it sits
    exactly on the week/month edge of `tickUnit`, so `6m` is the widest window
    still ticked by the week. `all` is a century - wider than any board has
    been running - so the server's own clamp turns it into the board's real
    span rather than this file knowing one. */
export const WINDOW_PRESETS: { key: WindowPreset; label: string; days: number }[] = [
  { key: '1d', label: '1 day', days: 1 },
  { key: '1w', label: '1 week', days: 7 },
  { key: '2w', label: '2 weeks', days: 14 },
  { key: '1m', label: '1 month', days: 30 },
  { key: '3m', label: '3 months', days: 90 },
  { key: '6m', label: '6 months', days: 183 },
  { key: '1y', label: '1 year', days: 365 },
  { key: 'all', label: 'All time', days: 36_525 },
];

/** The key if `raw` is one of the presets, else null - not a default, so the
    caller falls back to `DEFAULT_DAYS`. */
export function parsePreset(raw: string | null): WindowPreset | null {
  return WINDOW_PRESETS.find((preset) => preset.key === raw)?.key ?? null;
}

/**
 * The window, off the URL.
 *
 * **It never refuses.** A `from` and a `to` both present, both parseable and in
 * order are used, each normalised with `toISOString()`, and beat `range`.
 * Anything else - one alone, either unparseable, equal, inverted - falls back to
 * the window ending at the next local midnight strictly after `now`, because
 * half a window is not a window and a pasted link with a typo in it should open
 * the page. That window is as wide as `range` says when it names a preset and
 * `defaultDays` otherwise; an unrecognised `range` is ignored like any other
 * typo.
 *
 * @param offsetMinutes Minutes east of UTC, as `presetRange` takes it.
 * @param defaultDays The width when `range` names no preset; `DEFAULT_DAYS`
 *   unless a page wants another.
 */
export function resolveWindow(
  params: URLSearchParams,
  now: Date,
  offsetMinutes: number,
  defaultDays: number = DEFAULT_DAYS,
): ReportWindow {
  const from = params.get(FROM)?.trim();
  const to = params.get(TO)?.trim();

  if (from && to) {
    const start = new Date(from).getTime();
    const end = new Date(to).getTime();
    if (start < end) return { from: new Date(start).toISOString(), to: new Date(end).toISOString() };
  }

  const end = Date.parse(presetRange('today', now, offsetMinutes).to);
  const days =
    WINDOW_PRESETS.find((preset) => preset.key === parsePreset(params.get(RANGE)))?.days ?? defaultDays;

  return { from: new Date(end - days * DAY_MS).toISOString(), to: new Date(end).toISOString() };
}

/** The window moved half its own length earlier (-1) or later (+1). Both ends
    move by the same rounded amount, so the length is kept. */
export function shiftWindow(w: ReportWindow, direction: -1 | 1): ReportWindow {
  const from = Date.parse(w.from);
  const to = Date.parse(w.to);
  const step = direction * Math.round((to - from) / 2);

  return { from: new Date(from + step).toISOString(), to: new Date(to + step).toISOString() };
}

/** The window `days` long, ending where `w` ends. Millisecond arithmetic only,
    so across a DST seam the wall clock differs by an hour and nothing else. */
export function resizeWindow(w: ReportWindow, days: number): ReportWindow {
  const to = Date.parse(w.to);

  return { from: new Date(to - days * DAY_MS).toISOString(), to: w.to };
}

/** The window moved to end at the next local midnight after `now` - what
    `resolveWindow` ends at - with its length kept. */
export function windowEndingToday(w: ReportWindow, now: Date, offsetMinutes: number): ReportWindow {
  const end = Date.parse(presetRange('today', now, offsetMinutes).to);
  const length = Date.parse(w.to) - Date.parse(w.from);

  return { from: new Date(end - length).toISOString(), to: new Date(end).toISOString() };
}

/** The preset `w` is as wide as, to the nearest whole day (which absorbs an
    hour lost or gained across a DST seam), or null. */
export function presetOf(w: ReportWindow): WindowPreset | null {
  const days = Math.round((Date.parse(w.to) - Date.parse(w.from)) / DAY_MS);

  return WINDOW_PRESETS.find((preset) => preset.days === days)?.key ?? null;
}

export const sameWindow = (a: ReportWindow, b: ReportWindow): boolean =>
  a.from === b.from && a.to === b.to;

/** A copy of `params` with `from` and `to` set to `w` and `range` removed, since
    an explicit pair beats it anyway. Every other param is kept; the input is
    never mutated. */
export function withWindow(params: URLSearchParams, w: ReportWindow): URLSearchParams {
  const next = new URLSearchParams(params);
  next.set(FROM, w.from);
  next.set(TO, w.to);
  next.delete(RANGE);

  return next;
}

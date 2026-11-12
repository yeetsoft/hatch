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

export type WindowPreset =
  | '1h'
  | '2h'
  | '6h'
  | '12h'
  | '1d'
  | '1w'
  | '2w'
  | '1m'
  | '3m'
  | '6m'
  | '1y'
  | 'all';

/** The `range` shorthand and the control's choices, narrowest first. This is
    not the leaderboard's 24h..30d vocabulary. 183 is not 182 or 184: it sits
    exactly on the week/month edge of `tickUnit`, so `6m` is the widest window
    still ticked by the week. `all` is a century - wider than any board has
    been running - so the server's own clamp turns it into the board's real
    span rather than this file knowing one. The five sub-day presets end at
    `now` (`anchor: 'now'`); `1w` and up end at the next local midnight, as
    they always have (`anchor: 'midnight'`). */
export const WINDOW_PRESETS: {
  key: WindowPreset;
  label: string;
  ms: number;
  anchor: 'now' | 'midnight';
}[] = [
  { key: '1h', label: '1 hour', ms: 3_600_000, anchor: 'now' },
  { key: '2h', label: '2 hours', ms: 2 * 3_600_000, anchor: 'now' },
  { key: '6h', label: '6 hours', ms: 6 * 3_600_000, anchor: 'now' },
  { key: '12h', label: '12 hours', ms: 12 * 3_600_000, anchor: 'now' },
  { key: '1d', label: '24 hours', ms: DAY_MS, anchor: 'now' },
  { key: '1w', label: '1 week', ms: 7 * DAY_MS, anchor: 'midnight' },
  { key: '2w', label: '2 weeks', ms: 14 * DAY_MS, anchor: 'midnight' },
  { key: '1m', label: '1 month', ms: 30 * DAY_MS, anchor: 'midnight' },
  { key: '3m', label: '3 months', ms: 90 * DAY_MS, anchor: 'midnight' },
  { key: '6m', label: '6 months', ms: 183 * DAY_MS, anchor: 'midnight' },
  { key: '1y', label: '1 year', ms: 365 * DAY_MS, anchor: 'midnight' },
  { key: 'all', label: 'All time', ms: 36_525 * DAY_MS, anchor: 'midnight' },
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
 * the window `range` names: a `now`-anchored preset (`1h`..`1d`) ends at `now`
 * itself; a `midnight`-anchored one, or no recognised `range`, ends at the next
 * local midnight strictly after `now`, because half a window is not a window
 * and a pasted link with a typo in it should open the page. The width is
 * `defaultDays` when `range` names no preset; an unrecognised `range` is
 * ignored like any other typo.
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

  const preset = WINDOW_PRESETS.find((p) => p.key === parsePreset(params.get(RANGE)));
  const midnight = Date.parse(presetRange('today', now, offsetMinutes).to);
  const end = preset?.anchor === 'now' ? now.getTime() : midnight;
  const ms = preset?.ms ?? defaultDays * DAY_MS;

  return { from: new Date(end - ms).toISOString(), to: new Date(end).toISOString() };
}

/** The window moved half its own length earlier (-1) or later (+1). Both ends
    move by the same rounded amount, so the length is kept. */
export function shiftWindow(w: ReportWindow, direction: -1 | 1): ReportWindow {
  const from = Date.parse(w.from);
  const to = Date.parse(w.to);
  const step = direction * Math.round((to - from) / 2);

  return { from: new Date(from + step).toISOString(), to: new Date(to + step).toISOString() };
}

/** The window the preset's length, ending where `w` ends - except a
    `now`-anchored preset ends at `now` instead, if `w.to` would otherwise be
    later than it. A `midnight`-anchored preset always keeps `w.to`, so moving
    from an hour to a week keeps the hour's end, and Today then snaps it to
    midnight. Millisecond arithmetic only, so across a DST seam the wall clock
    differs by an hour and nothing else. */
export function resizeWindow(
  w: ReportWindow,
  preset: WindowPreset | { ms: number; anchor: 'now' | 'midnight' },
  now: Date,
): ReportWindow {
  const entry = typeof preset === 'string' ? WINDOW_PRESETS.find((p) => p.key === preset) : preset;
  if (!entry) return w;

  const to = Date.parse(w.to);
  const end = entry.anchor === 'now' && to > now.getTime() ? now.getTime() : to;

  return { from: new Date(end - entry.ms).toISOString(), to: new Date(end).toISOString() };
}

/** The window moved to end at `now`, when it is under two days long, else at
    the next local midnight after `now` - what `resolveWindow` ends at - with
    its length kept. */
export function windowEndingToday(w: ReportWindow, now: Date, offsetMinutes: number): ReportWindow {
  const length = Date.parse(w.to) - Date.parse(w.from);
  const end =
    length < 2 * DAY_MS ? now.getTime() : Date.parse(presetRange('today', now, offsetMinutes).to);

  return { from: new Date(end - length).toISOString(), to: new Date(end).toISOString() };
}

/** The preset `w` is as wide as, or null. A window under a day matches one to
    the nearest minute; a day or longer matches within an hour of the
    preset's length (which absorbs an hour lost or gained across a DST seam).
    The twelve lengths are all distinct, so the first match wins. */
export function presetOf(w: ReportWindow): WindowPreset | null {
  const ms = Date.parse(w.to) - Date.parse(w.from);

  if (ms < DAY_MS) {
    const minutes = Math.round(ms / 60_000);
    return WINDOW_PRESETS.find((preset) => Math.round(preset.ms / 60_000) === minutes)?.key ?? null;
  }

  return WINDOW_PRESETS.find((preset) => Math.abs(preset.ms - ms) <= 3_600_000)?.key ?? null;
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

/* The battery's arithmetic and its wording, apart from the component the way
   meter.ts is apart from StatusMeter.

   The hatch app has no DOM test setup and this story does not add one. The
   house pattern instead is that everything worth being wrong about lives here
   and is tested here, and the component is left thin enough to read: every
   sentence the battery says out loud, the fraction its ring is drawn from, and
   the one predicate deciding whether there is a battery at all.

   Two of them are only interesting at their edges, which is where the bugs are:

   - The ring runs down a five-hour window, and a row can arrive with no reset
     instant at all. Unknown is a third answer, not a zero - a ring drawn empty
     says "no time left", which is the opposite of "we do not know".
   - A reading can be older than the poll that fetched it. `state` is the whole
     of that story and `readAt` is how old, so the wording never guesses at
     staleness from a timestamp. */

import { channels, luminance } from './color';
import type { Utilization, UtilizationLimit } from '../types';

/** The window the ring runs down, in milliseconds. The session limit's own,
    which the account resets on. */
export const SESSION_WINDOW_MS = 5 * 60 * 60 * 1000;

const WEEK_MS = 7 * 24 * 60 * 60 * 1000;

/** How long each window Hatch knows about is. Nothing for `extra` - a monthly
    credit limit is not a window whose length Hatch knows - and nothing for a
    word never seen, so an unrecognised window draws with no time bar rather
    than a guessed one. */
const WINDOW_LENGTH_MS: Record<string, number> = {
  session: SESSION_WINDOW_MS,
  weekly: WEEK_MS,
  weeklyModel: WEEK_MS,
};

export const windowLengthMs = (window: string): number | null => WINDOW_LENGTH_MS[window] ?? null;

/**
 * The share of the five-hour window still to run, as 0..1, or null when there
 * is nothing to count to.
 *
 * Clamped at both ends rather than trusted: a reset instant that has just
 * passed is 0 rather than a negative arc, and one further out than the window
 * (an account that reset while the tab was asleep, or a clock a few seconds
 * apart from the server's) is a full ring rather than an arc that wraps.
 */
export function ringFraction(resetsAt: string | null | undefined, now: Date): number | null {
  if (!resetsAt) return null;

  const at = Date.parse(resetsAt);
  if (Number.isNaN(at)) return null;

  return Math.min(1, Math.max(0, (at - now.getTime()) / SESSION_WINDOW_MS));
}

/**
 * The share of a window's time that has gone, as 0..1, or null when there is
 * no reset instant to count from or the window's length is not one Hatch
 * knows (the `extra` row, or a word never seen).
 *
 * Clamped at both ends for the same reason `ringFraction` is: a reset in the
 * past reads as fully gone rather than past 100%, and one further out than
 * the window reads as not yet begun rather than negative.
 */
export function timeGoneFraction(
  window: string,
  resetsAt: string | null | undefined,
  now: Date,
): number | null {
  const length = windowLengthMs(window);
  if (length === null || !resetsAt) return null;

  const at = Date.parse(resetsAt);
  if (Number.isNaN(at)) return null;

  return Math.min(1, Math.max(0, 1 - (at - now.getTime()) / length));
}

/** Whether the window's own reset instant is behind us - the account's last
    word about it describes a window that is over. */
function hasReset(resetsAt: string | null | undefined, now: Date): boolean {
  if (!resetsAt) return false;

  const at = Date.parse(resetsAt);
  return !Number.isNaN(at) && at <= now.getTime();
}

/**
 * The percentage a row draws at: its own, unless its reset instant has
 * passed, in which case 0.
 *
 * The number the account last sent described a window that is over - drawing
 * it at 0 is the honest reading until the next heartbeat, rather than a spent
 * number sitting there as if the window were still running.
 */
export const rowPercent = (limit: UtilizationLimit, now: Date): number =>
  hasReset(limit.resetsAt, now) ? 0 : limit.percent;

/** The floor on the elapsed share `pace` divides by, so that the opening
    minutes of a window - which genuinely cannot support a rate - cannot
    project to several times the allowance. The first fifteen minutes of a
    five-hour window read as though fifteen minutes had gone; the floor can
    only ever read better than the truth, never worse. */
const PACE_GRACE = 0.05;

/** The ramp's top control point, and the ceiling `pace` clamps at. */
const PACE_CEILING = 150;

/**
 * The share of a row's allowance it is on course to have spent by its own
 * reset - the percentage spent divided by the share of its window's time
 * gone, as a projected percentage. `1.0` (100%) is level: the window
 * refreshes at the moment the allowance runs out. A row whose elapsed share
 * is unknown (`extra`, or any window with no reset instant) reads `gone` as
 * `1`, the same reading a window at its own end would give.
 *
 * Clamped at `PACE_CEILING` rather than left to run away.
 */
export function pace(limit: UtilizationLimit, now: Date): number {
  const used = rowPercent(limit, now) / 100;
  const goneFraction = timeGoneFraction(limit.window, limit.resetsAt, now);
  const gone = goneFraction ?? 1;

  return Math.min(PACE_CEILING, (used / Math.max(gone, PACE_GRACE)) * 100);
}

/**
 * When the window comes back, in plain words: `resets in 1h 20m`, `resets in
 * under a minute`, `reset time unknown`.
 *
 * This is the battery's accessible name and its tooltip, so it is a sentence
 * rather than a duration. "Under a minute" rather than "0m" for the same
 * reason: a battery that says `resets in 0m` for fifty-nine seconds reads as
 * broken.
 */
export function resetPhrase(resetsAt: string | null | undefined, now: Date): string {
  if (!resetsAt) return 'reset time unknown';

  const at = Date.parse(resetsAt);
  if (Number.isNaN(at)) return 'reset time unknown';

  const left = at - now.getTime();
  if (left <= 0) return 'resets in under a minute';

  const minutes = Math.floor(left / 60_000);
  if (minutes < 1) return 'resets in under a minute';

  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;

  if (hours === 0) return `resets in ${minutes}m`;
  return rest === 0 ? `resets in ${hours}h` : `resets in ${hours}h ${rest}m`;
}

/**
 * The row's own words about its reset, with the same past-the-post rule as
 * `rowPercent`: a window whose reset instant is behind us says so, rather
 * than `resetPhrase` saying `resets in under a minute` forever.
 */
export const rowResetPhrase = (limit: UtilizationLimit, now: Date): string =>
  hasReset(limit.resetsAt, now) ? 'has reset' : resetPhrase(limit.resetsAt, now);

/**
 * How old the reading is: `read just now`, `read 4 minutes ago`, `read 2 hours
 * ago`.
 *
 * Said in the modal rather than in the nav, because it only matters once
 * somebody is asking why a number looks wrong - and it is the whole of what a
 * `stale` answer is for.
 */
export function agePhrase(readAt: string | null | undefined, now: Date): string {
  if (!readAt) return 'never read';

  const at = Date.parse(readAt);
  if (Number.isNaN(at)) return 'never read';

  const minutes = Math.floor(Math.max(0, now.getTime() - at) / 60_000);
  if (minutes < 1) return 'read just now';
  if (minutes === 1) return 'read 1 minute ago';
  if (minutes < 60) return `read ${minutes} minutes ago`;

  const hours = Math.floor(minutes / 60);
  return hours === 1 ? 'read 1 hour ago' : `read ${hours} hours ago`;
}

/**
 * The row the glyph in the nav is drawn from - the five-hour session window.
 *
 * Found by `window` rather than by position: the runner decides the order and
 * this does not depend on it. Null when the reading carries no session row at
 * all - a runner could report a weekly window alone, between sessions.
 */
export const sessionLimit = (reading: Utilization | null): UtilizationLimit | null =>
  reading?.limits.find((limit) => limit.window === 'session') ?? null;

/**
 * The row that colours the battery: the highest pace a row is on, `extra`
 * left out - a spent credit line is a monthly budget the operator chose to
 * buy, not a window the account will refuse on, and it must never paint the
 * nav black on its own.
 *
 * Null only when the reading itself is null, or when every row it carries is
 * `extra`.
 */
export function worstRow(reading: Utilization | null, now: Date): UtilizationLimit | null {
  if (reading === null) return null;

  let worst: UtilizationLimit | null = null;
  let worstPace = -Infinity;

  for (const limit of reading.limits) {
    if (limit.window === 'extra') continue;

    const limitPace = pace(limit, now);
    if (limitPace > worstPace) {
      worstPace = limitPace;
      worst = limit;
    }
  }

  return worst;
}

/**
 * The five control points of the ramp every usage bar and the battery itself
 * are painted from, one shade per whole projected percent between them -
 * and, beside each colour, the phrase that names its band, so the two can
 * never name different bands.
 *
 * Constants here rather than tokens: CSS cannot interpolate a hundred-odd
 * shades of a gradient by itself, which is the same reason `INK_ON_LIGHT`/
 * `INK_ON_DARK` in `lib/color.ts` are literals rather than tokens. The same
 * ramp in both themes.
 *
 * Over percentage used, 100% was the point an allowance ran out, so the ramp
 * ran all the way to black. Over pace, 100% is level - the window refreshes
 * at the moment the allowance would run out, which is a window spent exactly
 * as intended, not bad news - so black and the deep red beside it both leave
 * the ramp, and the ramp runs to 150: real headroom above level for spending
 * ahead of the reset to still read as a gradient rather than a cliff.
 */
const RAMP: readonly [percent: number, color: string, phrase: string][] = [
  [0, '#1fd65f', 'allowance to spare'],
  // One off the old ramp's #2e9e44 (blue channel) - the 60-wide first
  // segment moves each channel by under one unit per percent in places, and
  // #2e9e44 lets two neighbouring percents (22 and 23) land on the same
  // rounded colour, which 1.6 forbids. Indistinguishable to the eye.
  [60, '#2e9e43', 'well ahead of the reset'],
  [90, '#f2c500', 'just ahead of the reset'],
  [100, '#f07c00', 'level with the reset'],
  [150, '#d32f2f', 'spending ahead of the reset'],
];

function mix(from: string, to: string, at: number): string {
  const [r0, g0, b0] = channels(from);
  const [r1, g1, b1] = channels(to);

  const channel = (a: number, b: number) => Math.round(a + (b - a) * at);
  return `#${[channel(r0, r1), channel(g0, g1), channel(b0, b1)]
    .map((value) => value.toString(16).padStart(2, '0'))
    .join('')}`;
}

/**
 * The ramp's colour at a projected percentage, clamped to 0..150 and rounded
 * to the nearest whole percent before it is looked up - 151 percentages, 151
 * distinct colours, interpolated linearly in sRGB between whichever pair of
 * control points the percentage falls between.
 */
export function rampColor(percent: number): string {
  const clamped = Math.round(Math.min(PACE_CEILING, Math.max(0, percent)));

  for (let i = 0; i < RAMP.length - 1; i++) {
    const [from, fromColor] = RAMP[i];
    const [to, toColor] = RAMP[i + 1];
    if (clamped >= from && clamped <= to) {
      return mix(fromColor, toColor, (clamped - from) / (to - from));
    }
  }

  return RAMP[RAMP.length - 1][1];
}

/** Which of the ramp's five control points a projected percentage names, for
    the phrase alone - the hex itself stays continuously interpolated. Ties
    round up, towards the band that is on its way. */
function nearestBand(percent: number): number {
  const clamped = Math.round(Math.min(PACE_CEILING, Math.max(0, percent)));

  for (let i = 0; i < RAMP.length - 1; i++) {
    const [from] = RAMP[i];
    const [to] = RAMP[i + 1];
    if (clamped >= from && clamped <= to) {
      return clamped - from < to - clamped ? i : i + 1;
    }
  }

  return RAMP.length - 1;
}

/** The phrase naming the band a projected percentage falls in - the same
    five words `batteryLabel` and `rowSentence` say out loud as the colour
    they sit beside. */
const paceLabel = (percent: number): string => RAMP[nearestBand(percent)][2];

/**
 * The ink to write on a ramp fill: pure black or pure white, whichever
 * measures the higher WCAG contrast ratio against it.
 *
 * Not `contrastInk` (`lib/color.ts:53`) as it stands - its dark ink is
 * `#1a1a1a`, which does not clear 4.5:1 against every colour on this ramp.
 * With `#000000`/`#ffffff` the lowest step across the ramp is 4.59:1, at a
 * projected 142% - comfortably over the 4.5:1 floor, and with headroom to
 * spare now that black and the deep red that used to sit at the top both
 * left the ramp. `luminance` is the same arithmetic `contrastInk` uses; only
 * the two inks being compared change.
 */
export function rampInk(color: string): string {
  const l = luminance(color);
  const withWhite = (1 + 0.05) / (l + 0.05);
  const withBlack = (l + 0.05) / 0.05;

  return withWhite >= withBlack ? '#ffffff' : '#000000';
}

/**
 * The custom properties a ramp-coloured element's `style` carries, read by
 * App.css - a copy of `statusVars` (`lib/color.ts:62`), and the only way a
 * ramp colour reaches the stylesheet. App.css holds no literal of its own.
 *
 * Takes a projected percentage of the allowance - a pace - not a percentage
 * spent.
 */
export function usageVars(pace: number): Record<string, string> {
  const color = rampColor(pace);
  return { '--usage-color': color, '--usage-ink': rampInk(color) };
}

/**
 * Whether there is a battery for me at all.
 *
 * Null is the 204 - no runner of mine has reported a reading - and it is the
 * common case: a fresh install is in it, and so is anybody who has never run
 * a runner. No element, no placeholder, no reserved space, and nothing
 * anywhere saying so.
 */
export const hasBattery = (reading: Utilization | null): reading is Utilization => reading !== null;

/**
 * The number beside the glyph. An em dash when there is no reading behind it,
 * which is what a reading with no session row at all reads as - a battery
 * that cannot say how full it is must not say "0".
 */
export const percentLabel = (limit: UtilizationLimit | null): string =>
  limit === null ? '—' : `${Math.round(limit.percent)}%`;

/**
 * The row's sentence for a screen reader: its name, the percent used, the
 * percent of its window gone, what its pace says in words, and when it
 * resets - one picture, one sentence, the way `StatusMeter.tsx` announces its
 * bar.
 *
 * The window-gone clause is left out for a row `timeGoneFraction` has
 * nothing to say about - the `extra` row, or a window with no reset instant -
 * which is exactly the row that draws no time bar at all. The pace phrase is
 * said regardless: `pace` always has an answer, even there.
 */
export function rowSentence(limit: UtilizationLimit, now: Date): string {
  const percent = rowPercent(limit, now);
  const goneFraction = timeGoneFraction(limit.window, limit.resetsAt, now);
  const goneClause = goneFraction === null ? '' : `, ${Math.round(goneFraction * 100)}% of the window gone`;

  return `${limit.label}: ${percent}% used${goneClause}, ${paceLabel(pace(limit, now))}, ${rowResetPhrase(limit, now)}`;
}

/**
 * The whole sentence the battery is announced as, and its tooltip.
 *
 * The state comes first when it is not `ok`, because "this number is old" is
 * the thing somebody reading a stale battery most needs to know and a screen
 * reader reads in order. The pace phrase names whichever row is colouring the
 * chip - `worstRow`, not the session row - and that row's own window is named
 * too whenever it is not the session's, so the battery never says less than
 * the chip it wears.
 */
export function batteryLabel(reading: Utilization | null, now: Date): string {
  if (reading === null) return 'Claude usage unknown — no runner of mine has reported one';

  const limit = sessionLimit(reading);
  if (limit === null) return 'Claude session usage unknown — the session window has not been reported';

  const worst = worstRow(reading, now);
  const phrase = paceLabel(worst ? pace(worst, now) : 0);
  const headline = `Claude session usage ${Math.round(limit.percent)}%, ${phrase}, ${resetPhrase(limit.resetsAt, now)}`;

  const colouredBy =
    worst && worst.window !== 'session' ? `, coloured by ${worst.label} at ${rowPercent(worst, now)}%` : '';

  const body = `${headline}${colouredBy}`;
  return reading.state === 'stale' ? `${body} (${agePhrase(reading.readAt, now)})` : body;
}

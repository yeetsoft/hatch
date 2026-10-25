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

import { luminance } from '@hatch/ui';
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
 * The row that colours the battery: the highest percentage a row draws at,
 * `extra` left out - a spent credit line is a monthly budget the operator
 * chose to buy, not a window the account will refuse on, and it must never
 * paint the nav red on its own.
 *
 * Read through `rowPercent`, so a window that has reset is 0 and cannot be
 * the worst however high its last number was. The first row wins a tie.
 *
 * Null only when the reading itself is null, or when every row it carries is
 * `extra`.
 */
export function worstRow(reading: Utilization | null, now: Date): UtilizationLimit | null {
  if (reading === null) return null;

  let worst: UtilizationLimit | null = null;
  let worstPercent = -Infinity;

  for (const limit of reading.limits) {
    if (limit.window === 'extra') continue;

    const limitPercent = rowPercent(limit, now);
    if (limitPercent > worstPercent) {
      worstPercent = limitPercent;
      worst = limit;
    }
  }

  return worst;
}

/**
 * The three bands every usage bar and the battery itself are painted from,
 * each with the phrase that names it, so the colour and the words can never
 * name different bands. `from` is the lowest whole percent used in the band:
 * green under 50, yellow 50-79, red 80 and above.
 *
 * Constants here rather than tokens, for the same reason `INK_ON_LIGHT`/
 * `INK_ON_DARK` in `lib/color.ts` are literals rather than tokens. The same
 * colours in both themes.
 */
const RAMP: readonly { from: number; color: string; phrase: string }[] = [
  { from: 0, color: '#1fd65f', phrase: 'plenty left' },
  { from: 50, color: '#f2c500', phrase: 'over half spent' },
  { from: 80, color: '#d32f2f', phrase: 'nearly spent' },
];

/** The band a percentage used falls in, clamped to 0..100 and rounded to the
    whole percent the badge shows before it is compared - 49.6 reads 50%, so
    it is yellow. */
function band(percent: number): (typeof RAMP)[number] {
  const clamped = Math.round(Math.min(100, Math.max(0, percent)));

  return RAMP.filter((entry) => entry.from <= clamped).at(-1) ?? RAMP[0];
}

/** The colour of the band a percentage used falls in. */
export const rampColor = (percent: number): string => band(percent).color;

/** The phrase naming the band a percentage used falls in - the same words
    `batteryLabel` and `rowSentence` say out loud as the colour they sit
    beside. */
const bandPhrase = (percent: number): string => band(percent).phrase;

/**
 * The ink to write on a ramp fill: pure black or pure white, whichever
 * measures the higher WCAG contrast ratio against it.
 *
 * Not `contrastInk` (`lib/color.ts:53`) as it stands - its dark ink is
 * `#1a1a1a`, which does not clear 4.5:1 against every colour on this ramp.
 * With `#000000`/`#ffffff` each of the three colours clears the 4.5:1 floor.
 * `luminance` is the same arithmetic `contrastInk` uses; only the two inks
 * being compared change.
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
 * Takes the percentage of the allowance spent, as the badge shows it.
 */
export function usageVars(percent: number): Record<string, string> {
  const color = rampColor(percent);
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
export const percentLabel = (limit: UtilizationLimit | null, now: Date): string =>
  limit === null ? '—' : `${Math.round(rowPercent(limit, now))}%`;

/**
 * The row's sentence for a screen reader: its name, the percent used, the
 * percent of its window gone, what the percentage says in words, and when
 * it resets - one picture, one sentence, the way `StatusMeter.tsx` announces its
 * bar.
 *
 * The window-gone clause is left out for a row `timeGoneFraction` has
 * nothing to say about - the `extra` row, or a window with no reset instant -
 * which is exactly the row that draws no time bar at all. The band phrase is
 * said regardless: a percentage always has an answer, even there.
 */
export function rowSentence(limit: UtilizationLimit, now: Date): string {
  const percent = rowPercent(limit, now);
  const goneFraction = timeGoneFraction(limit.window, limit.resetsAt, now);
  const goneClause = goneFraction === null ? '' : `, ${Math.round(goneFraction * 100)}% of the window gone`;

  return `${limit.label}: ${percent}% used${goneClause}, ${bandPhrase(percent)}, ${rowResetPhrase(limit, now)}`;
}

/**
 * The whole sentence the battery is announced as, and its tooltip.
 *
 * The state comes first when it is not `ok`, because "this number is old" is
 * the thing somebody reading a stale battery most needs to know and a screen
 * reader reads in order. The band phrase names whichever row is colouring the
 * chip - `worstRow`, not the session row - and that row's own window is named
 * too whenever it is not the session's, so the battery never says less than
 * the chip it wears.
 */
export function batteryLabel(reading: Utilization | null, now: Date): string {
  if (reading === null) return 'Claude usage unknown — no runner of mine has reported one';

  const limit = sessionLimit(reading);
  if (limit === null) return 'Claude session usage unknown — the session window has not been reported';

  const worst = worstRow(reading, now);
  const phrase = bandPhrase(worst ? rowPercent(worst, now) : 0);
  const headline = `Claude session usage ${Math.round(rowPercent(limit, now))}%, ${phrase}, ${rowResetPhrase(limit, now)}`;

  const colouredBy =
    worst && worst.window !== 'session' ? `, coloured by ${worst.label} at ${rowPercent(worst, now)}%` : '';

  const body = `${headline}${colouredBy}`;
  return reading.state === 'stale' ? `${body} (${agePhrase(reading.readAt, now)})` : body;
}

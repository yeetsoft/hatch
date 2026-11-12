/* The cumulative flow chart's geometry and every sentence it says, apart from
   the component - the same split lib/timeline.ts and lib/spend.ts make from
   theirs: this app has no DOM test setup, so what can be wrong lives in a
   function with a test beside it.

   The board's **last** column is drawn as the bottom band, baseline up, and
   the first column as the top band, whose upper edge is the grand total - a
   reader's eye reads a stack bottom to top, and the last column (closest to
   Done) is the one a team wants to watch sitting on the floor. `stackAt`
   reverses the counts once and never again; everything downstream of it
   works in that same bottom-up order.

   It imports `xOf` from ./timeline and never copies it. */

import { CHART_UNITS, type Scale, xOf } from './timeline';
import { statusVars } from './color';
import { markVars } from '@hatch/ui';
import type { Project, Status } from '../types';
import type { CumulativeFlow, CumulativeFlowPoint } from '../types';

/** What the stack is cut by: the board's own columns, or the projects in
    scope. */
export type ColourBy = 'column' | 'project';

export const BY = 'by';

/** `by` from the URL: `project` reads as such, absent or any other value as
    `column` - the page's default. */
export const resolveColourBy = (params: URLSearchParams): ColourBy =>
  params.get(BY) === 'project' ? 'project' : 'column';

/** `params` with `by` set to `by`, or cleared when `by` is the default
    (`column`), so the URL never carries the default explicitly. Never
    mutates `params`. */
export function withColourBy(params: URLSearchParams, by: ColourBy): URLSearchParams {
  const next = new URLSearchParams(params);
  by === 'project' ? next.set(BY, 'project') : next.delete(BY);
  return next;
}

/** The keys a cut stacks over: the board's columns, or the projects in
    scope. */
export const cutKeys = (answer: CumulativeFlow, by: ColourBy): (number | string)[] =>
  by === 'project' ? answer.projects : answer.columns;

/** One point's counts, cut the way `cutKeys` cuts the answer's own keys. */
const cutCounts = (point: CumulativeFlowPoint, by: ColourBy): number[] =>
  by === 'project' ? point.projectCounts : point.counts;

/** One key's label: a column's status name (`Unknown` for one no longer on
    the board), or a project's key, read back as a string. */
export const labelFor = (by: ColourBy, key: number | string, statuses: Status[]): string =>
  by === 'project' ? String(key) : statuses.find((status) => status.id === key)?.name ?? 'Unknown';

/** One band's vertical extent at one instant, bottom-up: `lower` is its
    baseline, `upper` its top. */
export interface Band {
  lower: number;
  upper: number;
}

/**
 * `counts`, bottom-up: the last column's band first (baseline at 0), the
 * first column's last (its upper bound the grand total).
 *
 * Reverses `counts` once, then an exclusive prefix sum: `stackAt([3,1,2])` ->
 * `[{0,2},{2,3},{3,6}]`.
 */
export function stackAt(counts: number[]): Band[] {
  let lower = 0;

  return [...counts].reverse().map((count) => {
    const band = { lower, upper: lower + count };
    lower = band.upper;

    return band;
  });
}

/** One band's point at one instant, in the pane's own units: `x` from `xOf`,
    `lowerFraction`/`upperFraction` against `tallestTotal`. */
export interface BandPoint {
  x: number;
  lowerFraction: number;
  upperFraction: number;
}

/** The grand total at every point, the tallest of them all - `0` for `[]`,
    which guards `Math.max()` on empty rather than returning `-Infinity`. Cut
    by `by`, though `tallestTotal` is the same value either way: the two
    cuts are over the same total at every point, by construction. */
export function tallestTotal(points: CumulativeFlowPoint[], by: ColourBy): number {
  return points.reduce((tallest, point) => Math.max(tallest, cutCounts(point, by).reduce((a, b) => a + b, 0)), 0);
}

/**
 * One series per column, bottom-up (the order `stackAt` returns its bands
 * in), each the column's `{x, lowerFraction, upperFraction}` across every
 * point.
 *
 * Fractions are against `tallestTotal`, guarded `0` rather than `NaN` when
 * the answer has no points or every count in it is zero.
 */
export function bands(answer: CumulativeFlow, scale: Scale, by: ColourBy): BandPoint[][] {
  const tallest = tallestTotal(answer.points, by);
  const series: BandPoint[][] = cutKeys(answer, by).map(() => []);

  for (const point of answer.points) {
    const x = xOf(scale, point.at);

    stackAt(cutCounts(point, by)).forEach((band, i) => {
      series[i].push({
        x,
        lowerFraction: tallest > 0 ? band.lower / tallest : 0,
        upperFraction: tallest > 0 ? band.upper / tallest : 0,
      });
    });
  }

  return series;
}

/** One band's SVG `points`, inside a pane `height` tall: the upper edge
    left-to-right, then the lower edge back right-to-left, so the polygon
    closes without crossing itself. */
export function bandPolygon(band: BandPoint[], height: number): string {
  const y = (fraction: number) => height - fraction * height;
  const upper = band.map((p) => `${p.x},${y(p.upperFraction)}`);
  const lower = [...band].reverse().map((p) => `${p.x},${y(p.lowerFraction)}`);

  return [...upper, ...lower].join(' ');
}

/** The top of the scale, drawn above the bands so a shape has a size, as
    `lib/spend.ts`'s `peakLabel` is. */
export const peakLabel = (points: CumulativeFlowPoint[], by: ColourBy): string => `peaking at ${tallestTotal(points, by)}`;

/** One point's hoverable slot, `{x, width}` in the pane's own units: a
 * Voronoi cell on x, its edges at the midpoint to each neighbour - except the
 * first, which starts at `0`, and the last, which ends at its own `x` rather
 * than `CHART_UNITS`. Nothing is drawn right of the last point, so nothing is
 * hoverable there either.
 */
export function slots(points: CumulativeFlowPoint[], scale: Scale): { x: number; width: number }[] {
  const xs = points.map((point) => xOf(scale, point.at));

  return xs.map((x, i) => {
    const left = i === 0 ? 0 : (xs[i - 1] + x) / 2;
    const right = i === xs.length - 1 ? x : (x + xs[i + 1]) / 2;

    return { x: left, width: right - left };
  });
}

/** The index of the point whose slot (`slots()`) an `x` in pane units falls
    in, clamped to `[0, CHART_UNITS]` first - a fast pointer move can report a
    clientX just outside the measured rect. `null` when there are no points.
    `slots()`'s own last cell stops at the last point's own x rather than
    `CHART_UNITS`, so past that edge this still resolves to the last index
    rather than null; on an exact slot boundary the right-hand cell wins. */
export function pointIndexAt(points: CumulativeFlowPoint[], scale: Scale, x: number): number | null {
  if (points.length === 0) return null;

  const clamped = Math.min(CHART_UNITS, Math.max(0, x));
  const cells = slots(points, scale);
  const index = cells.findIndex((cell) => clamped < cell.x + cell.width);

  return index === -1 ? cells.length - 1 : index;
}

/** One point as the hover card lists it: each column in board order with its
    name and count, plus the total. A column no longer on `statuses` (the
    same split `bucketLabel`, lib/spend.ts, makes for a bucket's own moment)
    names as `Unknown`. */
export function breakdown(
  point: CumulativeFlowPoint,
  keys: (number | string)[],
  by: ColourBy,
  statuses: Status[],
): { rows: { key: number | string; name: string; count: number }[]; total: number } {
  const counts = cutCounts(point, by);
  const rows = keys.map((key, i) => ({
    key,
    name: labelFor(by, key, statuses),
    count: counts[i],
  }));

  return { rows, total: rows.reduce((sum, row) => sum + row.count, 0) };
}

/** A moment's label: the day alone on a step of a day or more, the day plus
    the time on anything finer. */
export function momentLabel(at: string, stepMinutes: number, locale?: string): string {
  const date = new Date(at);
  const day = date.toLocaleDateString(locale, { day: 'numeric', month: 'short' });

  return stepMinutes < 1440 ? `${day} ${date.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}` : day;
}

/** The custom properties a band is painted with - its column's or its
    project's own colour, as `segmentStyle` (lib/timeline.ts) paints a
    timeline segment. */
export function bandStyle(
  by: ColourBy,
  key: number | string,
  statuses: Status[],
  projects: Project[],
): Record<string, string> {
  return by === 'project'
    ? markVars(projects.find((project) => project.key === key)?.color)
    : statusVars(statuses.find((status) => status.id === key)?.color);
}

/** Whether there is anything to draw: no points, or every count in every
    point zero. The page's empty-state guard. */
export function isEmpty(answer: CumulativeFlow): boolean {
  return answer.points.length === 0 || answer.points.every((point) => point.counts.every((count) => count === 0));
}

/** What can move the pinned point: a press or a drag onto the point at
    `index` (straight from `pointIndexAt`, so `null` off the pane), a
    keyboard step of one point, or a new answer landing. */
export type PinAction =
  | { type: 'press' | 'move'; index: number | null }
  | { type: 'step'; delta: -1 | 1 }
  | { type: 'answer' };

/** The pin's next index given `action`, against `answer.points.length` as
    `count`: `null` with nothing to pin, clamped to `[0, count - 1]`
    otherwise. A `press`/`move` off the pane (`index: null`) leaves `pin`
    where it was; a `step` with nothing yet pinned lands on the last point
    regardless of which way `delta` points. */
export function pinAfter(pin: number | null, count: number, action: PinAction): number | null {
  if (count === 0) return null;

  switch (action.type) {
    case 'answer':
      return null;
    case 'press':
    case 'move':
      return action.index === null ? pin : Math.min(count - 1, Math.max(0, action.index));
    case 'step':
      return pin === null ? count - 1 : Math.min(count - 1, Math.max(0, pin + action.delta));
  }
}

/** What the Projects control says: `None` for an empty choice, `All`,
    `All but AE` for all but one, else
    the chosen keys joined with `, ` - `typesSummary`'s own shape, shorter for
    having no plural to look up. */
export function projectsSummary(chosen: string[], projects: Project[]): string {
  if (chosen.length === 0) return 'None';
  if (chosen.length >= projects.length) return 'All';
  if (chosen.length === projects.length - 1) {
    const missing = projects.find((project) => !chosen.includes(project.key));
    if (missing) return `All but ${missing.key}`;
  }

  return projects
    .filter((project) => chosen.includes(project.key))
    .map((project) => project.key)
    .join(', ');
}

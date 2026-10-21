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
import type { Status } from '../types';
import type { CumulativeFlow, CumulativeFlowPoint } from '../types';

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
    which guards `Math.max()` on empty rather than returning `-Infinity`. */
export function tallestTotal(points: CumulativeFlowPoint[]): number {
  return points.reduce((tallest, point) => Math.max(tallest, point.counts.reduce((a, b) => a + b, 0)), 0);
}

/**
 * One series per column, bottom-up (the order `stackAt` returns its bands
 * in), each the column's `{x, lowerFraction, upperFraction}` across every
 * point.
 *
 * Fractions are against `tallestTotal`, guarded `0` rather than `NaN` when
 * the answer has no points or every count in it is zero.
 */
export function bands(answer: CumulativeFlow, scale: Scale): BandPoint[][] {
  const tallest = tallestTotal(answer.points);
  const series: BandPoint[][] = answer.columns.map(() => []);

  for (const point of answer.points) {
    const x = xOf(scale, point.at);

    stackAt(point.counts).forEach((band, i) => {
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
export const peakLabel = (points: CumulativeFlowPoint[]): string => `peaking at ${tallestTotal(points)}`;

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
  columns: number[],
  statuses: Status[],
): { rows: { columnId: number; name: string; count: number }[]; total: number } {
  const rows = columns.map((columnId, i) => ({
    columnId,
    name: statuses.find((status) => status.id === columnId)?.name ?? 'Unknown',
    count: point.counts[i],
  }));

  return { rows, total: rows.reduce((sum, row) => sum + row.count, 0) };
}

/** A moment's label: the day alone on a `day` or `week` step, the day plus the
    time on `hour`. */
export function momentLabel(at: string, step: CumulativeFlow['step'], locale?: string): string {
  const date = new Date(at);
  const day = date.toLocaleDateString(locale, { day: 'numeric', month: 'short' });

  return step === 'hour' ? `${day} ${date.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}` : day;
}

/** The custom properties a band is painted with - its column's own colour,
    as `segmentStyle` (lib/timeline.ts) paints a timeline segment. */
export function bandStyle(statuses: Status[], statusId: number): Record<string, string> {
  return statusVars(statuses.find((status) => status.id === statusId)?.color);
}

/** Whether there is anything to draw: no points, or every count in every
    point zero. The page's empty-state guard. */
export function isEmpty(answer: CumulativeFlow): boolean {
  return answer.points.length === 0 || answer.points.every((point) => point.counts.every((count) => count === 0));
}

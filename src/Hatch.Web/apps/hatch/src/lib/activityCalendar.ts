/* The activity calendar's geometry, apart from the component - `activityGraph.ts`
   split from `ActivityGraph.tsx` for the same reason: this app has no DOM test
   setup, so what can be wrong lives in a function with a test beside it.

   It draws the same daily `series` and the same `peak` the bars do, so the two
   pictures never disagree - only `bars`' own fraction differs from this file's
   four-step band. */

import { bucketLabel, peak, quantity, type ActivityMeasure } from './activityGraph';
import type { ActivityReport, ActivitySeriesPoint } from '../types';

const MINUTE_MS = 60_000;
const DAY_MS = 86_400_000;

/** The URL param this view owns. */
export const VIEW = 'view';

export type ActivityView = 'bars' | 'calendar';

export const DEFAULT_VIEW: ActivityView = 'bars';

/** `calendar` exactly; anything else - missing, blank, misspelt, `bars`
    itself - reads as `bars`. */
export const parseView = (raw: string | null): ActivityView => (raw === 'calendar' ? 'calendar' : DEFAULT_VIEW);

/** A copy of `params` with `view` set - or removed for the default, so the
    clean URL is the bars graph. Every other param is kept; the input is never
    mutated. */
export function withView(params: URLSearchParams, view: ActivityView): URLSearchParams {
  const next = new URLSearchParams(params);
  if (view === DEFAULT_VIEW) next.delete(VIEW);
  else next.set(VIEW, view);

  return next;
}

/** The toggle offers Calendar only past this many days. */
export const EIGHT_WEEKS_DAYS = 56;

/** Whether the window from `from` to `to` is long enough to offer Calendar -
    strictly more than eight weeks, in whole days. */
export function offersCalendar(from: string, to: string): boolean {
  const days = Math.round((Date.parse(to) - Date.parse(from)) / DAY_MS);

  return days > EIGHT_WEEKS_DAYS;
}

/** One day's cell. `null`, in `calendarWeeks`' own return, is a day outside
    the window altogether - padding at the start or end of a week - never a
    present day with nothing in it. */
export interface CalendarCell {
  at: string;
  value: number;
  step: 0 | 1 | 2 | 3 | 4;
  label: string;
}

const valueOf = (point: ActivitySeriesPoint, measure: ActivityMeasure): number =>
  measure === 'hours' ? point.hours : point.events;

/** A point's weekday in the reader's clock, Monday first (`0`) through Sunday
    (`6`) - the house's own week start, as `timeline.ts`'s week tick uses.
    Shifts `at` by `offsetMinutes` exactly as `bucketLabel` does. */
function weekday(at: string, offsetMinutes: number): number {
  const shifted = new Date(Date.parse(at) + offsetMinutes * MINUTE_MS);

  return (shifted.getUTCDay() + 6) % 7;
}

/** `0` only for a value of exactly zero, or when the whole range is zero;
    otherwise four bands above zero against `tallest`. */
function step(value: number, tallest: number): CalendarCell['step'] {
  if (value <= 0 || tallest <= 0) return 0;

  return Math.min(4, Math.max(1, Math.ceil((value / tallest) * 4))) as CalendarCell['step'];
}

/**
 * `series` laid out as weeks: one column per week, each a Monday-to-Sunday
 * array of seven. The first column is padded with leading `null`s for the
 * weekdays before the first point, the last with trailing `null`s for the
 * weekdays after the last - a range that starts or ends mid-week never grows
 * or shrinks a column to seven days of its own.
 *
 * `step` reuses `peak` from `activityGraph.ts`, so this picture and the bars
 * share one scale.
 */
export function calendarWeeks(
  report: Pick<ActivityReport, 'series' | 'offsetMinutes'>,
  measure: ActivityMeasure,
  locale?: string,
): (CalendarCell | null)[][] {
  if (report.series.length === 0) return [];

  const tallest = peak(report, measure);
  const weeks: (CalendarCell | null)[][] = [];
  let column: (CalendarCell | null)[] = new Array(weekday(report.series[0].at, report.offsetMinutes)).fill(null);

  for (const point of report.series) {
    const value = valueOf(point, measure);
    column.push({
      at: point.at,
      value,
      step: step(value, tallest),
      label: `${bucketLabel(point.at, 'day', report.offsetMinutes, locale)} — ${quantity(value, measure)}`,
    });

    if (column.length === 7) {
      weeks.push(column);
      column = [];
    }
  }

  if (column.length > 0) weeks.push([...column, ...new Array(7 - column.length).fill(null)]);

  return weeks;
}

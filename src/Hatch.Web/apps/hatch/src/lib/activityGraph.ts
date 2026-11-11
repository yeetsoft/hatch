/* The activity graph's geometry and every sentence it says, apart from the
   component - the split lib/spend.ts makes for the spend graph, and for the
   same reason: this app has no DOM test setup, so what can be wrong lives in a
   function with a test beside it.

   What it is deliberately not:

   - It is not a charting library. One rect per bucket and one polyline, in the
     house's own tokens; `SpendGraph` is the precedent.
   - It draws no gaps. A bucket in which nothing happened is a zero-height bar:
     the server emits it, and a break would assert an absence where there is a
     measurement.
   - It never puts the two measures on one axis. Events and estimated hours
     differ in kind, so a measure switch rescales the whole graph. The bars and
     the rolling mean of one measure do share an axis - the peak is the tallest
     of either, so the line never leaves the plot. */

import type { ActivityReport, ActivitySeriesPoint } from '../types';

const MINUTE_MS = 60_000;

/** The URL params this graph owns. */
export const MEASURE = 'measure';
export const WINDOW = 'window';

export type ActivityMeasure = 'events' | 'hours';

export const DEFAULT_MEASURE: ActivityMeasure = 'events';

/** The widest rolling mean the server answers. */
export const MAX_WINDOW = 90;

/** What the control's buttons say, in the order they are drawn. */
export const MEASURES: { key: ActivityMeasure; label: string }[] = [
  { key: 'events', label: 'Events' },
  { key: 'hours', label: 'Estimated hours' },
];

/** `events` or `hours`; anything else - missing, blank, misspelt - reads as
    `events`. */
export const parseMeasure = (raw: string | null): ActivityMeasure => (raw === 'hours' ? 'hours' : DEFAULT_MEASURE);

/** A whole number from 1 to 90, as the field or the URL gave it; `null` for
    anything else - blank, `3.5`, `abc`, `0`, `91`. `null` is "not a window":
    nothing is written and nothing is sent. */
export function parseWindow(raw: string | null): number | null {
  if (raw === null || !/^\s*\d+\s*$/.test(raw)) return null;

  const days = Number(raw);

  return days >= 1 && days <= MAX_WINDOW ? days : null;
}

/** A copy of `params` with `measure` set - or removed for the default, so the
    clean URL is the events graph. Every other param is kept; the input is never
    mutated. */
export function withMeasure(params: URLSearchParams, measure: ActivityMeasure): URLSearchParams {
  const next = new URLSearchParams(params);
  if (measure === DEFAULT_MEASURE) next.delete(MEASURE);
  else next.set(MEASURE, measure);

  return next;
}

/** A copy of `params` with `window` set to `days` - removed when `days` is not
    a window at all, so the server's own default stands. */
export function withWindow(params: URLSearchParams, days: number | null): URLSearchParams {
  const next = new URLSearchParams(params);
  if (days === null || parseWindow(String(days)) === null) next.delete(WINDOW);
  else next.set(WINDOW, String(days));

  return next;
}

/** One bar and the point of the line over it. */
export interface ActivityBar {
  at: string;
  value: number;
  rolling: number;
  /** 0…1 against `peak`. Zero for every bar when nothing happened, rather than
      NaN - see `bars`. */
  fraction: number;
  rollingFraction: number;
  /** What a hover and a screen reader get:
      `Wed 4 Nov — 3 events, 2.3 rolling mean`. */
  label: string;
}

const valueOf = (point: ActivitySeriesPoint, measure: ActivityMeasure): number =>
  measure === 'hours' ? point.hours : point.events;

const rollingOf = (point: ActivitySeriesPoint, measure: ActivityMeasure): number =>
  measure === 'hours' ? point.rollingHours : point.rollingEvents;

/** The tallest value or rolling mean of the measure, or 0 when everything is
    empty. Both are scaled against it so the line and the bars share one axis. */
export const peak = (report: Pick<ActivityReport, 'series'>, measure: ActivityMeasure): number =>
  report.series.reduce(
    (tallest, point) => Math.max(tallest, valueOf(point, measure), rollingOf(point, measure)),
    0,
  );

/** A figure to one decimal place with no trailing zero: `3`, `2.3`, `0.5`. */
export const figure = (n: number): string => String(Math.round(n * 10) / 10);

/** A value in words, by measure: `3 events`, `2.3 estimated hours`. Shared
    with `lib/activityCalendar.ts` so a bar's hover and a cell's title never
    put the same number into two different sentences. */
export const quantity = (n: number, measure: ActivityMeasure): string =>
  measure === 'hours' ? `${figure(n)} estimated ${n === 1 ? 'hour' : 'hours'}` : `${n} ${n === 1 ? 'event' : 'events'}`;

/**
 * A bucket's own moment in the reader's clock: the day for a daily bucket, the
 * day and hour for an hourly one. `offsetMinutes` (east of UTC) shifts it onto
 * the reader's wall clock, which is then formatted as UTC so the runtime's zone
 * never moves it, as `touchWords` does.
 */
export function bucketLabel(
  at: string,
  bucket: ActivityReport['bucket'],
  offsetMinutes: number,
  locale?: string,
): string {
  const shifted = new Date(Date.parse(at) + offsetMinutes * MINUTE_MS);
  const day = shifted.toLocaleDateString(locale, { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' });
  if (bucket === 'day') return day;

  return `${day}, ${shifted.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', timeZone: 'UTC' })}`;
}

/**
 * One bar per series point, oldest first as the server sent them - **the empty
 * ones included and never filtered out**.
 *
 * A range in which nothing happened has no tallest bar to scale against, and
 * every fraction is `0` there rather than `NaN` or `Infinity`: a flat range
 * draws as a flat range.
 */
export function bars(
  report: Pick<ActivityReport, 'series' | 'bucket' | 'offsetMinutes'>,
  measure: ActivityMeasure,
  locale?: string,
): ActivityBar[] {
  const tallest = peak(report, measure);

  return report.series.map((point) => {
    const value = valueOf(point, measure);
    const rolling = rollingOf(point, measure);

    return {
      at: point.at,
      value,
      rolling,
      fraction: tallest > 0 ? value / tallest : 0,
      rollingFraction: tallest > 0 ? rolling / tallest : 0,
      label:
        `${bucketLabel(point.at, report.bucket, report.offsetMinutes, locale)} — ` +
        `${quantity(value, measure)}, ${figure(rolling)} rolling mean`,
    };
  });
}

/** Where the line goes: one point at the centre of each bar's slot, `y` down
    from the top of a plot `height` tall. A single bucket is a single point,
    which the component draws as a circle. */
export function linePoints(
  drawn: Pick<ActivityBar, 'rollingFraction'>[],
  slot: number,
  height: number,
): { x: number; y: number }[] {
  return drawn.map((bar, i) => ({ x: i * slot + slot / 2, y: height - height * bar.rollingFraction }));
}

/** The top of the scale, drawn above the bars so a shape has a size. */
export const peakLabel = (report: Pick<ActivityReport, 'series'>, measure: ActivityMeasure): string =>
  `Peak ${quantity(peak(report, measure), measure)}`;

/** An end of the window in the reader's clock: `4 Nov 2026`, and for hourly
    buckets the hour too. */
export function endLabel(
  iso: string,
  bucket: ActivityReport['bucket'],
  offsetMinutes: number,
  locale?: string,
): string {
  const shifted = new Date(Date.parse(iso) + offsetMinutes * MINUTE_MS);
  const date = shifted.toLocaleDateString(locale, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
  if (bucket === 'day') return date;

  return `${date}, ${shifted.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', timeZone: 'UTC' })}`;
}

/** `by hour` or `by day`: the size the server chose, named from the answer. */
export const bucketPhrase = (bucket: ActivityReport['bucket']): string => (bucket === 'day' ? 'by day' : 'by hour');

/** The axis row's three parts: the window's two ends, and between them what a
    bar is and how wide the mean is - all from the answer. */
export function axisWords(
  report: Pick<ActivityReport, 'from' | 'to' | 'bucket' | 'offsetMinutes' | 'estimate'>,
  locale?: string,
): { from: string; caption: string; to: string } {
  return {
    from: endLabel(report.from, report.bucket, report.offsetMinutes, locale),
    caption: `Activity ${bucketPhrase(report.bucket)}, ${report.estimate.windowDays}-day window`,
    to: endLabel(report.to, report.bucket, report.offsetMinutes, locale),
  };
}

/** What the line is, in a sentence: `3-day rolling mean of events per day`. */
export const rollingWords = (
  windowDays: number,
  measure: ActivityMeasure,
  bucket: ActivityReport['bucket'],
): string =>
  `${windowDays}-day rolling mean of ${measure === 'hours' ? 'estimated hours' : 'events'} per ${bucket}`;

/** The SVG's accessible name. The marks say nothing to a screen reader, so the
    whole picture is said in one sentence. */
export function graphLabel(
  report: Pick<ActivityReport, 'series' | 'from' | 'to' | 'bucket' | 'offsetMinutes' | 'estimate'>,
  measure: ActivityMeasure,
  locale?: string,
): string {
  const axis = axisWords(report, locale);

  return (
    `${measure === 'hours' ? 'Estimated hours' : 'Events'} ${bucketPhrase(report.bucket)}, ` +
    `${axis.from} to ${axis.to}, ` +
    `${rollingWords(report.estimate.windowDays, measure, report.bucket)}, ${peakLabel(report, measure).toLowerCase()}`
  );
}

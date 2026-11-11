import { describe, expect, it } from 'vitest';
import {
  MAX_WINDOW,
  axisWords,
  bars,
  bucketLabel,
  graphLabel,
  linePoints,
  parseMeasure,
  parseWindow,
  peak,
  peakLabel,
  rollingWords,
  withMeasure,
  withWindow,
} from './activityGraph';
import type { ActivityReport, ActivitySeriesPoint } from '../types';

const point = (at: string, events: number, hours: number, rollingEvents = events, rollingHours = hours): ActivitySeriesPoint => ({
  at,
  events,
  hours,
  rollingEvents,
  rollingHours,
});

type Shape = Pick<ActivityReport, 'series' | 'bucket' | 'offsetMinutes' | 'from' | 'to' | 'estimate'>;

const shape = (series: ActivitySeriesPoint[], over: Partial<Shape> = {}): Shape => ({
  series,
  bucket: 'day',
  offsetMinutes: 0,
  from: '2026-11-02T00:00:00.000Z',
  to: '2026-11-05T00:00:00.000Z',
  estimate: { sessions: 0, minutes: 0, gapMinutes: 30, leadMinutes: 15, windowDays: 3 },
  ...over,
});

const week = shape([
  point('2026-11-02T00:00:00.000Z', 4, 1, 4, 1),
  point('2026-11-03T00:00:00.000Z', 0, 0, 2, 0.5),
  point('2026-11-04T00:00:00.000Z', 2, 8, 2, 3),
]);

describe('bars', () => {
  it('scales each measure against its own peak, the line and the bars on one axis', () => {
    const events = bars(week, 'events', 'en-GB');
    expect(events.map((b) => b.fraction)).toEqual([1, 0, 0.5]);
    expect(events.map((b) => b.rollingFraction)).toEqual([1, 0.5, 0.5]);

    const hours = bars(week, 'hours', 'en-GB');
    expect(hours.map((b) => b.fraction)).toEqual([0.125, 0, 1]);
    expect(hours[0].rollingFraction).toBe(0.125);
  });

  it('never shares a scale between measures', () => {
    expect(peak(week, 'events')).toBe(4);
    expect(peak(week, 'hours')).toBe(8);
  });

  it('lets a rolling mean above every bar set the peak, so the line stays inside the plot', () => {
    const odd = shape([point('2026-11-02T00:00:00.000Z', 1, 0, 3, 0)]);

    expect(peak(odd, 'events')).toBe(3);
    expect(bars(odd, 'events')[0].fraction).toBeCloseTo(1 / 3);
    expect(bars(odd, 'events')[0].rollingFraction).toBe(1);
  });

  it('keeps an empty bucket, at zero height, and labels it', () => {
    const drawn = bars(week, 'events', 'en-GB');

    expect(drawn).toHaveLength(3);
    expect(drawn[1].value).toBe(0);
    expect(drawn[1].fraction).toBe(0);
    expect(drawn[1].label).toBe('Tue 3 Nov — 0 events, 2 rolling mean');
  });

  it('says the value and the rolling mean in the label, by measure', () => {
    expect(bars(week, 'events', 'en-GB')[0].label).toBe('Mon 2 Nov — 4 events, 4 rolling mean');
    expect(bars(week, 'hours', 'en-GB')[2].label).toBe('Wed 4 Nov — 8 estimated hours, 3 rolling mean');
    expect(bars(shape([point('2026-11-04T00:00:00.000Z', 1, 1, 2.34, 1)]), 'events', 'en-GB')[0].label).toBe(
      'Wed 4 Nov — 1 event, 2.3 rolling mean',
    );
  });

  it('is flat and never NaN when nothing happened', () => {
    const flat = shape([point('2026-11-02T00:00:00.000Z', 0, 0), point('2026-11-03T00:00:00.000Z', 0, 0)]);

    for (const measure of ['events', 'hours'] as const) {
      const drawn = bars(flat, measure);
      expect(peak(flat, measure)).toBe(0);
      expect(drawn.map((b) => b.fraction)).toEqual([0, 0]);
      expect(drawn.map((b) => b.rollingFraction)).toEqual([0, 0]);
      // The line lies on the baseline.
      expect(linePoints(drawn, 4, 100).map((p) => p.y)).toEqual([100, 100]);
    }
  });

  it('draws an empty series as nothing', () => {
    expect(bars(shape([]), 'events')).toEqual([]);
    expect(peak(shape([]), 'events')).toBe(0);
  });

  it('handles a single bucket', () => {
    const one = shape([point('2026-11-04T00:00:00.000Z', 5, 2, 5, 2)]);
    const drawn = bars(one, 'events');

    expect(drawn).toHaveLength(1);
    expect(drawn[0].fraction).toBe(1);
    expect(linePoints(drawn, 4, 100)).toEqual([{ x: 2, y: 0 }]);
  });
});

describe('bucketLabel', () => {
  it('moves a late bucket onto the next day with a positive offset', () => {
    // 23:00 UTC is already the 5th at UTC+2.
    expect(bucketLabel('2026-11-04T23:00:00.000Z', 'day', 0, 'en-GB')).toBe('Wed 4 Nov');
    expect(bucketLabel('2026-11-04T23:00:00.000Z', 'day', 120, 'en-GB')).toBe('Thu 5 Nov');
  });

  it('moves an early bucket onto the previous day with a negative offset', () => {
    expect(bucketLabel('2026-11-04T01:00:00.000Z', 'day', -300, 'en-GB')).toBe('Tue 3 Nov');
  });

  it('adds the hour for an hourly bucket, in the reader\'s clock', () => {
    expect(bucketLabel('2026-11-04T14:00:00.000Z', 'hour', 60, 'en-GB')).toBe('Wed 4 Nov, 15:00');
  });
});

describe('linePoints', () => {
  it('places points at slot centres, higher mean higher up', () => {
    const drawn = bars(week, 'events');

    expect(linePoints(drawn, 4, 100)).toEqual([
      { x: 2, y: 0 },
      { x: 6, y: 50 },
      { x: 10, y: 50 },
    ]);
  });

  it('is empty for no bars', () => {
    expect(linePoints([], 4, 100)).toEqual([]);
  });
});

describe('the words', () => {
  it('names the peak by measure', () => {
    expect(peakLabel(week, 'events')).toBe('Peak 4 events');
    expect(peakLabel(week, 'hours')).toBe('Peak 8 estimated hours');
    expect(peakLabel(shape([point('2026-11-02T00:00:00.000Z', 1, 1)]), 'events')).toBe('Peak 1 event');
  });

  it('names the axis from the answer: both ends in the reader\'s clock, the size, the window', () => {
    expect(axisWords(week, 'en-GB')).toEqual({
      from: '2 Nov 2026',
      caption: 'Activity by day, 3-day window',
      to: '5 Nov 2026',
    });

    expect(
      axisWords(
        shape([], {
          bucket: 'hour',
          offsetMinutes: 60,
          from: '2026-11-04T23:00:00.000Z',
          to: '2026-11-05T23:00:00.000Z',
          estimate: { sessions: 0, minutes: 0, gapMinutes: 30, leadMinutes: 15, windowDays: 1 },
        }),
        'en-GB',
      ),
    ).toEqual({ from: '5 Nov 2026, 00:00', caption: 'Activity by hour, 1-day window', to: '6 Nov 2026, 00:00' });
  });

  it('says what the line is', () => {
    expect(rollingWords(3, 'events', 'day')).toBe('3-day rolling mean of events per day');
    expect(rollingWords(1, 'hours', 'hour')).toBe('1-day rolling mean of estimated hours per hour');
    expect(rollingWords(7, 'hours', 'day')).toBe('7-day rolling mean of estimated hours per day');
  });

  it('names the whole picture for a screen reader', () => {
    expect(graphLabel(week, 'events', 'en-GB')).toBe(
      'Events by day, 2 Nov 2026 to 5 Nov 2026, 3-day rolling mean of events per day, peak 4 events',
    );
  });
});

describe('parseMeasure', () => {
  it('reads hours, and everything else as events', () => {
    expect(parseMeasure('hours')).toBe('hours');
    expect(parseMeasure('events')).toBe('events');
    expect(parseMeasure('cost')).toBe('events');
    expect(parseMeasure('')).toBe('events');
    expect(parseMeasure(null)).toBe('events');
  });
});

describe('parseWindow', () => {
  it('takes a whole number from 1 to 90', () => {
    expect(parseWindow('1')).toBe(1);
    expect(parseWindow('90')).toBe(MAX_WINDOW);
    expect(parseWindow('45')).toBe(45);
    expect(parseWindow(' 7 ')).toBe(7);
  });

  it('refuses everything else', () => {
    for (const raw of ['0', '91', '-1', '3.5', 'abc', '', '  ', '1e1', '3 days', null]) {
      expect(parseWindow(raw)).toBeNull();
    }
  });
});

describe('withMeasure and withWindow', () => {
  it('keeps every other param and does not mutate the input', () => {
    const before = new URLSearchParams('actor=Ada&range=2w&project=HA');

    const hours = withMeasure(before, 'hours');
    expect(hours.get('measure')).toBe('hours');
    expect(hours.get('actor')).toBe('Ada');
    expect(hours.getAll('project')).toEqual(['HA']);
    expect(before.toString()).toBe('actor=Ada&range=2w&project=HA');

    const wide = withWindow(before, 7);
    expect(wide.get('window')).toBe('7');
    expect(wide.get('range')).toBe('2w');
    expect(before.has('window')).toBe(false);
  });

  it('deletes the param for the default measure and for a non-window', () => {
    expect(withMeasure(new URLSearchParams('measure=hours&actor=Ada'), 'events').toString()).toBe('actor=Ada');
    expect(withWindow(new URLSearchParams('window=7&actor=Ada'), null).toString()).toBe('actor=Ada');
    expect(withWindow(new URLSearchParams('actor=Ada'), 91).has('window')).toBe(false);
  });
});

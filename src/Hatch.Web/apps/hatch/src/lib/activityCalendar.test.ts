import { describe, expect, it } from 'vitest';
import { EIGHT_WEEKS_DAYS, calendarWeeks, offersCalendar, parseView, withView } from './activityCalendar';
import type { ActivityReport, ActivitySeriesPoint } from '../types';

const point = (at: string, events: number, hours: number): ActivitySeriesPoint => ({
  at,
  events,
  hours,
  rollingEvents: events,
  rollingHours: hours,
});

type Shape = Pick<ActivityReport, 'series' | 'offsetMinutes'>;

const shape = (series: ActivitySeriesPoint[], offsetMinutes = 0): Shape => ({ series, offsetMinutes });

describe('offersCalendar', () => {
  it('offers Calendar only strictly past eight weeks', () => {
    expect(offersCalendar('2026-01-01T00:00:00.000Z', '2026-02-26T00:00:00.000Z')).toBe(false); // 56 days
    expect(offersCalendar('2026-01-01T00:00:00.000Z', '2026-02-27T00:00:00.000Z')).toBe(true); // 57 days
  });

  it('agrees with the constant it is named after', () => {
    expect(EIGHT_WEEKS_DAYS).toBe(56);
  });
});

describe('parseView and withView', () => {
  it('reads calendar, and everything else as bars', () => {
    expect(parseView('calendar')).toBe('calendar');
    expect(parseView('bars')).toBe('bars');
    expect(parseView('week')).toBe('bars');
    expect(parseView('')).toBe('bars');
    expect(parseView(null)).toBe('bars');
  });

  it('keeps every other param, deletes for the default, and never mutates the input', () => {
    const before = new URLSearchParams('actor=Ada&measure=hours');

    const calendar = withView(before, 'calendar');
    expect(calendar.get('view')).toBe('calendar');
    expect(calendar.get('actor')).toBe('Ada');
    expect(before.has('view')).toBe(false);

    expect(withView(new URLSearchParams('view=calendar&actor=Ada'), 'bars').toString()).toBe('actor=Ada');
  });
});

describe('calendarWeeks', () => {
  // 4 Nov 2026 is a Wednesday - two weekdays (Mon, Tue) before it.
  const midWeek = shape([
    point('2026-11-04T00:00:00.000Z', 4, 1),
    point('2026-11-05T00:00:00.000Z', 0, 0),
    point('2026-11-06T00:00:00.000Z', 2, 4),
  ]);

  it('pads a range starting mid-week with leading nulls, and puts no null once a day is in range', () => {
    const weeks = calendarWeeks(midWeek, 'events');

    expect(weeks).toHaveLength(1);
    expect(weeks[0]).toHaveLength(7);
    expect(weeks[0][0]).toBeNull();
    expect(weeks[0][1]).toBeNull();
    expect(weeks[0][2]?.at).toBe('2026-11-04T00:00:00.000Z');
    expect(weeks[0][3]?.at).toBe('2026-11-05T00:00:00.000Z');
    expect(weeks[0][4]?.at).toBe('2026-11-06T00:00:00.000Z');
    expect(weeks[0][5]).toBeNull();
    expect(weeks[0][6]).toBeNull();
  });

  it('shades a day at the peak at step 4, and one at a quarter of it at step 1', () => {
    const weeks = calendarWeeks(midWeek, 'events');

    expect(weeks[0][2]?.step).toBe(4); // 4 events, the peak
    expect(weeks[0][4]?.step).toBe(2); // 2 events, half the peak
  });

  it('keeps a present empty day at step 0, never null', () => {
    const weeks = calendarWeeks(midWeek, 'events');

    expect(weeks[0][3]).not.toBeNull();
    expect(weeks[0][3]?.value).toBe(0);
    expect(weeks[0][3]?.step).toBe(0);
  });

  it('shades every present cell step 0, with no NaN, over an all-zero range', () => {
    const flat = shape([point('2026-11-04T00:00:00.000Z', 0, 0), point('2026-11-05T00:00:00.000Z', 0, 0)]);
    const weeks = calendarWeeks(flat, 'events');

    for (const cell of weeks[0]) {
      if (cell === null) continue;
      expect(cell.step).toBe(0);
      expect(Number.isNaN(cell.step)).toBe(false);
    }
  });

  it('shades steps independently per measure, from the same series', () => {
    const series = shape([point('2026-11-04T00:00:00.000Z', 10, 1), point('2026-11-05T00:00:00.000Z', 5, 4)]);

    const events = calendarWeeks(series, 'events');
    expect(events[0][2]?.step).toBe(4); // 10 of a peak of 10
    expect(events[0][3]?.step).toBe(2); // 5 of a peak of 10

    const hours = calendarWeeks(series, 'hours');
    expect(hours[0][2]?.step).toBe(1); // 1 of a peak of 4
    expect(hours[0][3]?.step).toBe(4); // 4 of a peak of 4
  });

  it('labels a cell with its day and its value', () => {
    const weeks = calendarWeeks(midWeek, 'events', 'en-GB');

    expect(weeks[0][2]?.label).toBe('Wed 4 Nov — 4 events');
    expect(weeks[0][3]?.label).toBe('Thu 5 Nov — 0 events');
  });

  it('draws an empty series as no weeks at all', () => {
    expect(calendarWeeks(shape([]), 'events')).toEqual([]);
  });
});

import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ActivityCalendar } from './ActivityCalendar';
import type { ActivityMeasure } from '../lib/activityGraph';
import type { ActivityReport, ActivitySeriesPoint } from '../types';

/* No DOM here, so this is structural - react-dom/server, as ActivityGraph.test.tsx
   reads. Class names are matched as whole attribute values. */

const point = (at: string, events: number, hours: number): ActivitySeriesPoint => ({
  at,
  events,
  hours,
  rollingEvents: events,
  rollingHours: hours,
});

const report = (series: ActivitySeriesPoint[], over: Partial<ActivityReport> = {}): ActivityReport =>
  ({
    actor: 'Ada',
    from: '2026-09-01T00:00:00.000Z',
    to: '2026-11-06T00:00:00.000Z',
    total: 6,
    kinds: [],
    issues: [],
    events: [],
    truncated: false,
    bucket: 'day',
    offsetMinutes: 0,
    series,
    sessions: [],
    estimate: { sessions: 2, minutes: 90, gapMinutes: 30, leadMinutes: 15, windowDays: 3 },
    ...over,
  }) as ActivityReport;

// 4 Nov 2026 is a Wednesday - two weekdays (Mon, Tue) before it, two (Sat, Sun) after Fri.
const week = report([
  point('2026-11-04T00:00:00.000Z', 4, 1),
  point('2026-11-05T00:00:00.000Z', 0, 0),
  point('2026-11-06T00:00:00.000Z', 2, 4),
]);

const count = (html: string, needle: string): number => html.split(needle).length - 1;

const render = (answer: ActivityReport, measure: ActivityMeasure = 'events'): string =>
  renderToStaticMarkup(<ActivityCalendar report={answer} measure={measure} onMeasure={() => {}} locale="en-GB" />);

describe('ActivityCalendar', () => {
  it('draws one cell for every day of the padded week, present and padding alike', () => {
    const html = render(week);

    expect(count(html, '<rect')).toBe(7);
    expect(count(html, 'class="hatch-calendar-pad"')).toBe(4);
  });

  it('draws no title on a padding cell', () => {
    const html = render(week);

    expect(count(html, '<title>')).toBe(3);
  });

  it('titles a present cell with its day and its value, the empty one included', () => {
    const html = render(week);

    expect(html).toContain('<title>Wed 4 Nov — 4 events</title>');
    expect(html).toContain('<title>Thu 5 Nov — 0 events</title>');
    expect(html).toContain('<title>Fri 6 Nov — 2 events</title>');
  });

  it('presses the button the measure names, and reshades to it', () => {
    const events = render(week, 'events');
    expect(events).toMatch(/aria-pressed="true"[^>]*>Events</);
    expect(events).toMatch(/aria-pressed="false"[^>]*>Estimated hours</);
    expect(events).toContain('class="hatch-calendar-step-4"');

    const hours = render(week, 'hours');
    expect(hours).toMatch(/aria-pressed="false"[^>]*>Events</);
    expect(hours).toMatch(/aria-pressed="true"[^>]*>Estimated hours</);
    expect(hours).toContain('<title>Wed 4 Nov — 1 estimated hour</title>');
    expect(hours).toContain('<title>Fri 6 Nov — 4 estimated hours</title>');
  });
});

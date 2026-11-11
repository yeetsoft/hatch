import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ActivityGraph } from './ActivityGraph';
import type { ActivityMeasure } from '../lib/activityGraph';
import type { ActivityReport, ActivitySeriesPoint } from '../types';

/* No DOM here, so this is structural - react-dom/server, as TimelineChart.test.tsx
   reads. Class names are matched as whole attribute values. */

const point = (at: string, events: number, hours: number, rollingEvents: number, rollingHours: number): ActivitySeriesPoint => ({
  at,
  events,
  hours,
  rollingEvents,
  rollingHours,
});

const report = (series: ActivitySeriesPoint[], over: Partial<ActivityReport> = {}): ActivityReport =>
  ({
    actor: 'Ada',
    from: '2026-11-02T00:00:00.000Z',
    to: '2026-11-05T00:00:00.000Z',
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

const week = report([
  point('2026-11-02T00:00:00.000Z', 4, 1, 4, 1),
  point('2026-11-03T00:00:00.000Z', 0, 0, 2, 0.5),
  point('2026-11-04T00:00:00.000Z', 2, 8, 2, 3),
]);

const render = (answer: ActivityReport, measure: ActivityMeasure = 'events', windowDays = 3): string =>
  renderToStaticMarkup(
    <ActivityGraph
      report={answer}
      measure={measure}
      onMeasure={() => {}}
      windowDays={windowDays}
      onWindow={() => {}}
      locale="en-GB"
    />,
  );

const count = (html: string, needle: string): number => html.split(needle).length - 1;

describe('ActivityGraph', () => {
  it('draws a hoverable slot and a bar per bucket, the empty one included', () => {
    const html = render(week);

    expect(count(html, 'class="hatch-activity-slot"')).toBe(3);
    expect(count(html, 'class="hatch-activity-bar"')).toBe(3);
    expect(html).toContain('<title>Mon 2 Nov — 4 events, 4 rolling mean</title>');
    expect(html).toContain('<title>Tue 3 Nov — 0 events, 2 rolling mean</title>');
    expect(html).toContain('<title>Wed 4 Nov — 2 events, 2 rolling mean</title>');
  });

  it('draws the empty bucket as a zero-height bar', () => {
    const html = render(week);

    expect(html).toMatch(/<rect class="hatch-activity-bar" x="4\.4" y="100" width="3\.2" height="0"/);
  });

  it('draws one polyline through the slot centres', () => {
    const html = render(week);

    expect(count(html, '<polyline')).toBe(1);
    expect(html).toContain('points="2,0 6,50 10,50"');
  });

  it('presses the button the measure names, and rescales to it', () => {
    const events = render(week, 'events');
    expect(events).toMatch(/aria-pressed="true"[^>]*>Events</);
    expect(events).toMatch(/aria-pressed="false"[^>]*>Estimated hours</);
    expect(events).toContain('Peak 4 events');

    const hours = render(week, 'hours');
    expect(hours).toMatch(/aria-pressed="false"[^>]*>Events</);
    expect(hours).toMatch(/aria-pressed="true"[^>]*>Estimated hours</);
    expect(hours).toContain('Peak 8 estimated hours');
    expect(hours).toContain('<title>Wed 4 Nov — 8 estimated hours, 3 rolling mean</title>');
  });

  it('names the axis, the line, and holds the window in a bounded number field', () => {
    const html = render(week);

    expect(html).toContain('<span>2 Nov 2026</span>');
    expect(html).toContain('<span>5 Nov 2026</span>');
    expect(html).toContain('Activity by day, 3-day window');
    expect(html).toContain('3-day rolling mean of events per day');
    expect(html).toMatch(/<input type="number"[^>]*min="1"[^>]*max="90"[^>]*value="3"/);
  });

  it('says hourly in the axis and the sentence when the answer is hourly', () => {
    const hourly = report([point('2026-11-04T09:00:00.000Z', 1, 0.25, 1, 0.25)], {
      bucket: 'hour',
      from: '2026-11-04T09:00:00.000Z',
      to: '2026-11-04T10:00:00.000Z',
      estimate: { sessions: 1, minutes: 15, gapMinutes: 30, leadMinutes: 15, windowDays: 1 },
    });
    const html = render(hourly, 'hours', 1);

    expect(html).toContain('Activity by hour, 1-day window');
    expect(html).toContain('1-day rolling mean of estimated hours per hour');
    expect(html).toContain('<title>Wed 4 Nov, 09:00 — 0.3 estimated hours, 0.3 rolling mean</title>');
  });

  it('draws a single bucket as a dot, not a polyline', () => {
    const html = render(report([point('2026-11-04T00:00:00.000Z', 5, 2, 5, 2)]));

    expect(count(html, 'class="hatch-activity-slot"')).toBe(1);
    expect(html).toContain('viewBox="0 0 4 100"');
    expect(html).toContain('hatch-activity-line--dot');
    expect(html).not.toContain('<polyline');
  });

  it('draws an all-zero range flat, with the line on the baseline and no NaN', () => {
    const html = render(
      report([point('2026-11-02T00:00:00.000Z', 0, 0, 0, 0), point('2026-11-03T00:00:00.000Z', 0, 0, 0, 0)]),
    );

    expect(html).not.toContain('NaN');
    expect(html).toContain('Peak 0 events');
    expect(html).toContain('points="2,100 6,100"');
    expect(count(html, 'height="0"')).toBe(2);
  });

  it('names the picture for a screen reader', () => {
    expect(render(week)).toContain(
      'aria-label="Events by day, 2 Nov 2026 to 5 Nov 2026, 3-day rolling mean of events per day, peak 4 events"',
    );
  });
});

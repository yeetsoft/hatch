import type { ReactNode } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { ActivityReportBody } from './ActivityReportPage';
import {
  chooseActorWords,
  emptyWords,
  estimateHeadlineWords,
  estimateRuleWords,
  headlineWords,
  sessionLengthWords,
  sessionWhenWords,
  truncatedWords,
} from '../lib/activity';
import type { ActivityView } from '../lib/activityCalendar';
import { noProjectWords } from '../lib/timeline';
import type { ActivityEvent, ActivityReport, ActivityReportIssue, ActivitySession, IssueCard } from '../types';

/* No DOM runner and no fetch: the body is rendered from props, as
   ChangelogReportPage.test.tsx does with ChangelogReportBody. */

const event = (id: number, over: Partial<ActivityEvent> = {}): ActivityEvent => ({
  id,
  issueKey: 'HA-5',
  actor: 'Ada',
  kind: 'status_changed',
  payload: { from: 'To Do', to: 'In Progress' },
  at: '2026-11-04T14:05:00.000Z',
  ...over,
});

const row = (key: string, over: Partial<ActivityReportIssue> = {}): ActivityReportIssue => ({
  issue: { key, projectKey: 'HA', type: 'story', title: `title ${key}` } as IssueCard,
  first: '2026-11-03T09:00:00.000Z',
  last: '2026-11-04T14:05:00.000Z',
  count: 3,
  kinds: [
    { kind: 'commented', count: 2 },
    { kind: 'status_changed', count: 1 },
  ],
  ...over,
});

const session = (from: string, over: Partial<ActivitySession> = {}): ActivitySession => ({
  from,
  to: '2026-11-04T15:00:00.000Z',
  events: 4,
  issueKeys: ['HA-5', 'HA-6'],
  estimatedMinutes: 75,
  ...over,
});

const report = (over: Partial<ActivityReport> = {}): ActivityReport => ({
  actor: 'Ada',
  from: '2026-10-28T00:00:00.000Z',
  to: '2026-11-11T00:00:00.000Z',
  total: 6,
  kinds: [
    { kind: 'commented', count: 4 },
    { kind: 'status_changed', count: 2 },
  ],
  issues: [row('HA-5'), row('HA-6')],
  events: [event(2, { issueKey: 'HA-6' }), event(1)],
  truncated: false,
  bucket: 'day',
  offsetMinutes: 0,
  series: [
    { at: '2026-11-02T00:00:00.000Z', events: 4, hours: 1, rollingEvents: 4, rollingHours: 1 },
    { at: '2026-11-03T00:00:00.000Z', events: 0, hours: 0, rollingEvents: 2, rollingHours: 0.5 },
    { at: '2026-11-04T00:00:00.000Z', events: 2, hours: 1.5, rollingEvents: 2, rollingHours: 0.8 },
  ],
  sessions: [session('2026-11-04T13:45:00.000Z')],
  estimate: { sessions: 1, minutes: 75, gapMinutes: 30, leadMinutes: 15, windowDays: 3 },
  ...over,
});

const render = (
  actor: string | null,
  answer: ActivityReport | null,
  over: { measure?: 'events' | 'hours'; windowDays?: number | null; view?: ActivityView; sessionControls?: ReactNode } = {},
): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <ActivityReportBody
        actor={actor}
        report={answer}
        offsetMinutes={0}
        measure={over.measure ?? 'events'}
        windowDays={over.windowDays ?? null}
        view={over.view ?? 'bars'}
        onMeasure={() => {}}
        onWindow={() => {}}
        onView={() => {}}
        locale="en-GB"
        sessionControls={over.sessionControls}
      />
    </MemoryRouter>,
  );

describe('ActivityReportBody', () => {
  it('draws the headline, a rundown row per issue linked to it, and the trail', () => {
    const answer = report();
    const html = render('Ada', answer);

    expect(html).toContain(headlineWords(answer));
    expect(html).toContain('6 events: 4 comments, 2 moves');

    // The rundown: key and title in the link's row, first and last, what was done.
    expect(html).toContain('href="/issues/HA-5"');
    expect(html).toContain('title HA-5');
    expect(html).toContain('href="/issues/HA-6"');
    expect(html).toContain('3 Nov 2026, 09:00');
    expect(html).toContain('2 comments, 1 move');

    // The trail: History's columns, with describe's line.
    for (const column of ['When', 'Issue', 'What', 'Who', 'Changed']) {
      expect(html).toContain(`<th>${column}</th>`);
    }
    expect(html).toContain('status changed');
    expect(html).toContain('4 Nov 2026, 14:05');
    expect(html).toContain('To Do → In Progress');
    expect(html).not.toContain(truncatedWords(answer));

    // Sessions: the headline, the rule, the table with its own columns.
    expect(html).toContain(estimateHeadlineWords(answer.estimate));
    expect(html).toContain(estimateRuleWords(answer.estimate));
    for (const column of ['When', 'Length', 'Events', 'Issues']) {
      expect(html).toContain(`<th>${column}</th>`);
    }
    expect(html).toContain(sessionWhenWords(answer.sessions[0], 0, 'en-GB'));
    expect(html).toContain(sessionLengthWords(answer.sessions[0].estimatedMinutes));
  });

  it('draws the graph between the headline and the rundown', () => {
    const html = render('Ada', report());

    expect(html).toContain('<h2>Over time</h2>');
    expect(html).toContain('hatch-activity-plot');
    expect(html).toContain('3-day rolling mean of events per day');
    expect(html.indexOf('6 events:')).toBeLessThan(html.indexOf('<h2>Over time</h2>'));
    expect(html.indexOf('<h2>Over time</h2>')).toBeLessThan(html.indexOf('<h2>Rundown</h2>'));
  });

  it('draws the graph on the measure and window it is given', () => {
    const html = render('Ada', report(), { measure: 'hours', windowDays: 7 });

    expect(html).toMatch(/aria-pressed="true"[^>]*>Estimated hours</);
    expect(html).toMatch(/<input type="number"[^>]*value="7"/);
  });

  it('offers the Bars/Calendar toggle only past eight weeks, and draws Bars under it', () => {
    const short = render('Ada', report());
    expect(short).not.toContain('Calendar');

    const long = render('Ada', report({ from: '2026-08-01T00:00:00.000Z', to: '2026-11-11T00:00:00.000Z' }));
    expect(long).toMatch(/aria-pressed="true"[^>]*>Bars</);
    expect(long).toMatch(/aria-pressed="false"[^>]*>Calendar</);
    expect(long).toContain('hatch-activity-plot');
  });

  it('draws the calendar when chosen, over a window past eight weeks', () => {
    const html = render('Ada', report({ from: '2026-08-01T00:00:00.000Z', to: '2026-11-11T00:00:00.000Z' }), {
      view: 'calendar',
    });

    expect(html).toMatch(/aria-pressed="false"[^>]*>Bars</);
    expect(html).toMatch(/aria-pressed="true"[^>]*>Calendar</);
    expect(html).toContain('hatch-calendar-plot');
    expect(html).not.toContain('hatch-activity-plot');
  });

  it('reads view=calendar as Bars when the window is eight weeks or under', () => {
    const html = render('Ada', report(), { view: 'calendar' });

    expect(html).not.toContain('Calendar');
    expect(html).toContain('hatch-activity-plot');
  });

  it('draws the graph only when something happened', () => {
    const empty = report({ total: 0, kinds: [], issues: [], events: [], series: [] });

    expect(render('Ada', empty)).not.toContain('Over time');
    expect(render(null, null)).not.toContain('Over time');
    expect(render('Ada', null)).not.toContain('Over time');
  });

  it('draws the cut line, and only then', () => {
    const answer = report({ truncated: true, total: 612 });

    expect(render('Ada', answer)).toContain(
      'Showing the newest 2 of 612 events; the rundown above covers all of them.',
    );
  });

  it('is a sentence and no table for an empty range', () => {
    const html = render('Ada', report({ total: 0, kinds: [], issues: [], events: [] }));

    expect(html).toContain(emptyWords('Ada'));
    expect(html).not.toContain('<table');
  });

  it('says to choose when nobody is', () => {
    const html = render(null, null);

    expect(html).toContain(chooseActorWords());
    expect(html).not.toContain('<table');
  });

  it('says no project is chosen when the read was not made', () => {
    expect(render('Ada', null)).toContain(noProjectWords());
  });

  it('draws nothing that could write to the board', () => {
    const html = render('Ada', report());

    // The graph's controls only move the address; none of them submits.
    for (const tag of ['<form', '<select', '<textarea']) expect(html).not.toContain(tag);
    for (const button of html.match(/<button[^>]*>/g) ?? []) expect(button).toContain('type="button"');
  });

  it('draws sessionControls in the Sessions section, above the table', () => {
    const html = render('Ada', report(), { sessionControls: <span>Gap and Lead live here</span> });

    expect(html).toContain('Gap and Lead live here');
  });
});

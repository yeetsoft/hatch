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
  sessions: [session('2026-11-04T13:45:00.000Z')],
  estimate: { sessions: 1, minutes: 75, gapMinutes: 30, leadMinutes: 15, windowDays: 3 },
  ...over,
});

const render = (actor: string | null, answer: ActivityReport | null, sessionControls?: ReactNode): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <ActivityReportBody
        actor={actor}
        report={answer}
        offsetMinutes={0}
        locale="en-GB"
        sessionControls={sessionControls}
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

  it('draws nothing that could write', () => {
    const html = render('Ada', report());

    for (const tag of ['<button', '<form', '<input', '<select', '<textarea']) {
      expect(html).not.toContain(tag);
    }
  });

  it('draws sessionControls in the Sessions section, above the table', () => {
    const html = render('Ada', report(), <span>Gap and Lead live here</span>);

    expect(html).toContain('Gap and Lead live here');
  });
});

import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { CardPreview } from './BoardCard';
import { countWords } from '../lib/storiesOnEpics';
import type { IssueCard, Project, Status } from '../types';

const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'AER-1',
  projectKey: 'AER',
  type: 'task',
  title: 'A task',
  statusId: 1,
  rank: 0,
  parentKey: null,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
  runnerTags: [],
  runnerTagsOwn: [],
  runnerTagsFrom: [],
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
  ...over,
});

const project = (over: Partial<Project> = {}): Project => ({
  id: 1,
  key: 'AER',
  name: 'Aerial',
  issueCount: 1,
  createdAt: '2026-01-01T00:00:00Z',
  color: '#336699',
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  members: [],
  canApprove: false,
  canAdminister: false,
  landing: 'pull-request',
  checksRequired: true,
  onePullRequest: false,
  autoHealBugEnabled: false,
  autoHealBugPriority: 'emergency',
  autoHealBugPreApproval: 'express',
  autoHealBugStatusId: null,
  repoSourced: false,
  repoSourcedBy: null,
  repoSourcedAt: null,
  bindsTracker: false,
  bindsTrackerBy: null,
  bindsTrackerAt: null,
  reflectionCadenceDays: null,
  reflectionLastPeriodStart: null,
  reflectionNextDueAt: null,
  storiesOnEpics: false,
  ...over,
});

const status = (id: number, sortOrder: number, opts: Partial<Status> = {}): Status => ({
  id,
  name: `status-${id}`,
  sortOrder,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
  color: '#6b7280',
  ...opts,
});

const between = (html: string, open: string) => {
  const start = html.indexOf(open);
  expect(start).toBeGreaterThanOrEqual(0);
  const end = html.indexOf('</div>', start);
  return html.slice(start, end);
};

describe('BoardCard CardFace', () => {
  it('draws only the project mark and the key on the head, no type word and no enhancers', () => {
    const full = card({
      priority: 'expedited',
      express: true,
      claim: { claimedBy: 'hatch', runner: 'Jeff Winger', claimedAt: new Date().toISOString(), heartbeatAt: new Date().toISOString(), chatter: null, chatterAt: null, ttlSeconds: 300 },
      openQuestions: 2,
      held: true,
      assignee: { kind: 'person', id: '1', name: 'Ada' },
    });
    const html = renderToStaticMarkup(
      <CardPreview card={full} waiting={false} terminal={false} project={project()} />,
    );

    const head = between(html, '<div class="hatch-card-head">');
    expect(head).toContain('hatch-project-mark');
    expect(head).toContain('hatch-card-key');
    expect(head).not.toContain('hatch-card-expedited');
    expect(head).not.toContain('hatch-card-express');
    expect(head).not.toContain('hatch-card-preapproved');
    expect(head).not.toContain('hatch-card-claim');
    expect(head).not.toContain('hatch-card-asking');
    expect(head).not.toContain('hatch-card-held');
    expect(head).not.toContain('hatch-card-assignee');
    expect(head).not.toContain('hatch-badge');

    // The type is a glyph on the card now, not a word anywhere on it - fixture's
    // default type is 'task'.
    expect(html).toContain('hatch-type-glyph--task');
    expect(html).not.toContain('>epic<');
    expect(html).not.toContain('>story<');
    expect(html).not.toContain('>task<');
    expect(html).not.toContain('>bug<');
  });

  it('wears the pre-approved mark when pre-approved', () => {
    const preapproved = card({ preApproval: 'preapproved', express: true });
    const html = renderToStaticMarkup(
      <CardPreview card={preapproved} waiting={false} terminal={false} project={project()} />,
    );

    const marks = between(html, '<div class="hatch-card-marks">');
    expect(marks).toContain('hatch-card-preapproved');
  });

  it('draws every enhancer together on the marks row, in order, ending in the assignee', () => {
    const full = card({
      priority: 'expedited',
      express: true,
      preApproval: 'preapproved',
      claim: { claimedBy: 'hatch', runner: 'Jeff Winger', claimedAt: new Date().toISOString(), heartbeatAt: new Date().toISOString(), chatter: null, chatterAt: null, ttlSeconds: 300 },
      openQuestions: 2,
      held: true,
      assignee: { kind: 'person', id: '1', name: 'Ada' },
    });
    const html = renderToStaticMarkup(
      <CardPreview card={full} waiting={false} terminal={false} project={project()} />,
    );

    const marks = between(html, '<div class="hatch-card-marks">');
    const classes = [
      'hatch-card-expedited',
      'hatch-card-express',
      'hatch-card-preapproved',
      'hatch-card-claim',
      'hatch-card-asking',
      'hatch-card-held',
      'hatch-card-assignee',
    ];
    const indices = classes.map((c) => marks.indexOf(c));
    for (const i of indices) expect(i).toBeGreaterThanOrEqual(0);
    for (let i = 1; i < indices.length; i++) expect(indices[i]).toBeGreaterThan(indices[i - 1]);
    expect(indices[indices.length - 1]).toBe(marks.lastIndexOf('hatch-card-assignee'));
  });

  it('draws no marks row at all on a card with nothing to say', () => {
    const bare = card();
    const html = renderToStaticMarkup(
      <CardPreview card={bare} waiting={false} terminal={false} project={null} />,
    );

    expect(html).not.toContain('hatch-card-marks');
  });

  it('draws the parent chip from the card\'s own parentKey, with no parent card to look up', () => {
    const child = card({ parentKey: 'AER-999' });
    const html = renderToStaticMarkup(
      <CardPreview card={child} waiting={false} terminal={false} project={null} />,
    );

    // Asserted apart: the arrow and the key may be separate text nodes.
    expect(html).toContain('hatch-card-parent');
    expect(html).toContain('AER-999');
  });

  it('shows held and not stalled when both are set', () => {
    const both = card({ held: true, stalledAt: new Date().toISOString() });
    const html = renderToStaticMarkup(
      <CardPreview card={both} waiting={false} terminal={false} project={null} />,
    );

    expect(html).toContain('hatch-card-held');
    expect(html).not.toContain('hatch-card-stalled');
  });

  it('draws a dot per story, each coloured and titled from its own column, and the right "N of M done"', () => {
    const epic = card({ type: 'epic', key: 'AER-1' });
    const statuses = [
      status(1, 0, { color: '#111111' }),
      status(2, 1, { color: '#222222' }),
      status(3, 2, { color: '#333333', isTerminal: true }),
    ];
    const stories = [
      card({ key: 'AER-2', type: 'story', parentKey: 'AER-1', statusId: 1 }),
      card({ key: 'AER-3', type: 'story', parentKey: 'AER-1', statusId: 2 }),
      card({ key: 'AER-4', type: 'story', parentKey: 'AER-1', statusId: 3 }),
    ];
    const html = renderToStaticMarkup(
      <CardPreview card={epic} waiting={false} terminal={false} project={null} stories={stories} statuses={statuses} />,
    );

    expect(html.split('hatch-card-story-dot').length - 1).toBe(3);
    for (const [story, st] of [
      [stories[0], statuses[0]],
      [stories[1], statuses[1]],
      [stories[2], statuses[2]],
    ] as const) {
      const dotStart = html.indexOf(`title="${story.key} - ${st.name}"`);
      expect(dotStart).toBeGreaterThanOrEqual(0);
    }
    expect(html).toContain(countWords(3, 1));

    const strip = between(html, '<div class="hatch-card-stories"');
    expect(strip).toContain('aria-label="3 stories, 1 done"');
  });

  it('draws no stories strip when stories is absent or empty', () => {
    const bare = card();
    const noneHtml = renderToStaticMarkup(
      <CardPreview card={bare} waiting={false} terminal={false} project={null} />,
    );
    expect(noneHtml).not.toContain('hatch-card-stories');

    const emptyHtml = renderToStaticMarkup(
      <CardPreview card={bare} waiting={false} terminal={false} project={null} stories={[]} statuses={[]} />,
    );
    expect(emptyHtml).not.toContain('hatch-card-stories');
  });
});

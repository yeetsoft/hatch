import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { QueueModal } from './QueueModal';
import type { QueueStatus } from '../lib/useQueue';
import type { Issue, IssueCard, IssueClaim, QueueEntry, Status } from '../types';

const status = (over: Partial<Status> = {}): Status => ({
  id: 1,
  name: 'To Do',
  sortOrder: 0,
  isTerminal: false,
  isDeferred: false,
  isWip: true,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  color: '#888888',
  ...over,
});

const issue = (over: Partial<Issue> = {}): Issue => ({
  key: 'AER-1',
  projectId: 1,
  projectKey: 'AER',
  type: 'task',
  title: 'A task',
  description: '',
  statusId: 1,
  rank: 0,
  parentKey: null,
  childKeys: [],
  dependsOnKeys: [],
  dependentKeys: [],
  readyAt: null,
  dueAt: null,
  pullRequestUrl: null,
  modelOverride: null,
  effortOverride: null,
  assignee: null,
  createdBy: 'Britta Perry',
  createdAt: '2026-09-09T12:00:00Z',
  updatedAt: '2026-09-09T12:00:00Z',
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  express: false,
  mergeChecks: [],
  buildChecks: [],
  wipLimit: null,
  ...over,
});

const entry = (over: Partial<QueueEntry> = {}): QueueEntry => ({
  issue: issue(),
  fromStatus: status(),
  toStatus: status({ id: 2, name: 'In Progress' }),
  blocked: null,
  kind: 'advance',
  hop: false,
  hopKind: null,
  hopUnder: null,
  clearNote: null,
  ...over,
});

const claim = (over: Partial<IssueClaim> = {}): IssueClaim => ({
  claimedBy: 'hatch',
  runner: 'Jeff Winger',
  claimedAt: new Date().toISOString(),
  heartbeatAt: new Date().toISOString(),
  chatter: null,
  chatterAt: null,
  ttlSeconds: 300,
  ...over,
});

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
  express: false,
  ...over,
});

const render = (
  status: QueueStatus,
  queue: QueueEntry[],
  error: string | null = null,
  cards: readonly IssueCard[] = [],
  onTake: (card: IssueCard) => void = () => {},
) =>
  renderToStaticMarkup(
    <QueueModal
      open
      onClose={() => {}}
      status={status}
      queue={queue}
      error={error}
      onRefresh={async () => {}}
      cards={cards}
      onTake={onTake}
    />,
  );

describe('QueueModal', () => {
  it('renders rows in the array\'s own order', () => {
    const html = render('ready', [entry({ issue: issue({ key: 'AER-1' }) }), entry({ issue: issue({ key: 'AER-2' }) }), entry({ issue: issue({ key: 'AER-3' }) })]);

    const at1 = html.indexOf('AER-1');
    const at2 = html.indexOf('AER-2');
    const at3 = html.indexOf('AER-3');

    expect(at1).toBeGreaterThan(-1);
    expect(at2).toBeGreaterThan(at1);
    expect(at3).toBeGreaterThan(at2);
  });

  it('marks a folded row as folded, not clear', () => {
    const html = render('ready', [entry({ blocked: 'waiting on a dependency' })]);

    expect(html).toContain('hatch-queue-row--folded');
    expect(html).not.toContain('hatch-queue-row--clear');
  });

  it('marks a clear row as clear, not folded', () => {
    const html = render('ready', [entry({ blocked: null })]);

    expect(html).toContain('hatch-queue-row--clear');
    expect(html).not.toContain('hatch-queue-row--folded');
  });

  it('draws the marker column on every row once any entry is above normal', () => {
    const html = render('ready', [entry(), entry({ issue: issue({ priority: 'expedited' }) })]);

    expect(html.match(/hatch-queue-marker/g)).toHaveLength(2);
  });

  it('draws no marker column when every entry is normal', () => {
    const html = render('ready', [entry(), entry()]);

    expect(html).not.toContain('hatch-queue-marker');
  });

  it('draws the robot head on every row once any entry\'s issue is claimed', () => {
    const html = render('ready', [entry(), entry({ issue: issue({ claim: claim() }) })]);

    expect(html.match(/hatch-card-claim /g)).toHaveLength(1);
  });

  it('draws no robot head when nothing in the pass is claimed', () => {
    const html = render('ready', [entry(), entry()]);

    expect(html).not.toContain('hatch-card-claim');
  });

  it('reads the row\'s own claim, not a relative\'s, for the robot head', () => {
    const html = render('ready', [entry({ blocked: 'HA-12, above this, is working this' })]);

    expect(html).not.toContain('hatch-card-claim');
  });

  it('tallies the pass in the footer', () => {
    const html = render('ready', [entry(), entry({ blocked: 'waiting' }), entry(), entry({ blocked: 'waiting' })]);

    expect(html).toContain('4 issues in the pass, 2 clear');
  });

  it('uses the singular for one row', () => {
    const html = render('ready', [entry()]);

    expect(html).toContain('1 issue in the pass, 1 clear');
  });

  it('renders the empty answer for a pass with nothing on its path', () => {
    const html = render('ready', []);

    expect(html).toContain('hatch-empty__message');
    expect(html).toContain('Nothing on the dispatcher');
    expect(html).not.toContain('<ul');
  });

  it('renders the reading line while loading, with no rows', () => {
    const html = render('loading', []);

    expect(html).toContain('Reading the queue…');
    expect(html).not.toContain('<ul');
  });

  it('renders the refusal and a retry press on error', () => {
    const html = render('error', [], 'the server refused');

    expect(html).toContain('the server refused');
    expect(html).toContain('Try again');
  });

  it('renders each row as a button, not a bare line of text', () => {
    const html = render('ready', [entry({ issue: issue({ key: 'AER-1' }) })], null, [card({ key: 'AER-1' })]);

    expect(html).toContain('<button');
    expect(html).not.toMatch(/<li[^>]*class="hatch-queue-row/);
  });

  it('disables a row whose issue the board is not holding', () => {
    const html = render('ready', [entry({ issue: issue({ key: 'AER-1' }) })], null, [card({ key: 'AER-2' })]);

    expect(html).toMatch(/<button[^>]*class="hatch-queue-row[^>]*disabled/);
  });

  it('leaves a matched row enabled', () => {
    const html = render('ready', [entry({ issue: issue({ key: 'AER-1' }) })], null, [card({ key: 'AER-1' })]);

    expect(html).not.toMatch(/<button[^>]*disabled/);
  });
});

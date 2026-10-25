import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { QueueModal } from './QueueModal';
import type { QueueStatus } from '../lib/useQueue';
import type { Issue, IssueCard, IssueClaim, QueueEntry, Runner, Status } from '../types';

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
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
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
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
  releaseNote: null,
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
  stallResumeSeconds: 0,
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
  model: null,
  deniedTools: null,
  blockedKind: null,
  deadlocked: false,
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

const runner = (over: Partial<Runner> = {}): Runner => ({
  name: 'Jeff Winger',
  kind: 'loop',
  firstSeenAt: '2026-09-09T12:00:00Z',
  lastSeenAt: '2026-09-09T12:00:00Z',
  claimKey: null,
  line: null,
  lineAt: null,
  state: 'running',
  under: null,
  repositories: [],
  tags: [],
  projects: [],
  drives: [],
  clones: null,
  mine: null,
  where: null,
  agent: null,
  maxRuns: null,
  maxSpend: null,
  untilAt: null,
  goneAfterSeconds: 300,
  exhaustedUntil: null,
  for: null,
  canControl: true,
  yours: true,
  order: null,
  revision: null,
  sequence: null,
  builtAt: null,
  boardDrift: 'Unknown',
  sourceDrift: 'Unknown',
  ...over,
});

const render = (
  status: QueueStatus,
  queue: QueueEntry[],
  error: string | null = null,
  cards: readonly IssueCard[] = [],
  onTake: (card: IssueCard) => void = () => {},
  runnerChoice: string | null = null,
  runners: Runner[] | null = [],
  onChoose: (runner: string | null) => void = () => {},
  now?: Date,
) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <QueueModal
        open
        onClose={() => {}}
        status={status}
        queue={queue}
        error={error}
        runner={runnerChoice}
        runners={runners}
        onChoose={onChoose}
        onRefresh={async () => {}}
        cards={cards}
        onTake={onTake}
        now={now}
      />
    </MemoryRouter>,
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

  it('marks a deadlocked row as deadlocked, folded, and shows its sentence', () => {
    const html = render('ready', [entry({ blocked: 'deadlocked - waiting on HA-2', deadlocked: true })]);

    expect(html).toContain('hatch-queue-row--deadlocked');
    expect(html).toContain('hatch-queue-row--folded');
    expect(html).toContain('deadlocked - waiting on HA-2');
  });

  it('does not mark an ordinary folded row as deadlocked', () => {
    const html = render('ready', [entry({ blocked: 'waiting on a dependency' })]);

    expect(html).not.toContain('hatch-queue-row--deadlocked');
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

  it('offers every runner GET /api/hatch/runners returned, as options', () => {
    const html = render('ready', [], null, [], () => {}, null, [runner({ name: 'Jeff Winger' }), runner({ name: 'Britta Perry' })]);

    expect(html).toContain('>Jeff Winger<');
    expect(html).toContain('>Britta Perry<');
  });

  it('selects Board-wide when nobody is chosen', () => {
    const html = render('ready', [], null, [], () => {}, null, [runner({ name: 'Jeff Winger' })]);

    expect(html).toMatch(/<option value="" selected[^>]*>Board-wide<\/option>/);
  });

  it("selects the chosen runner's own option", () => {
    const html = render('ready', [], null, [], () => {}, 'Jeff Winger', [runner({ name: 'Jeff Winger' })]);

    expect(html).toMatch(/<option value="Jeff Winger" selected[^>]*>Jeff Winger<\/option>/);
  });

  it('names the board-wide pass when nobody is chosen', () => {
    const html = render('ready', [], null, [], () => {}, null);

    expect(html).toContain('Reading nobody');
    expect(html).toContain('the board-wide pass');
  });

  it('names the chosen runner when one is chosen', () => {
    const html = render('ready', [], null, [], () => {}, 'A', [runner({ name: 'A' })]);

    expect(html).toContain('Reading A');
    expect(html).toContain('own queue');
  });

  it('names the project, shows the row, and says no runner is driving it', () => {
    const html = render('ready', [entry({ issue: issue({ key: 'AER-1', projectKey: 'AER', mergeWork: { kind: 'merge', sentence: '' } }) })]);

    expect(html).toContain('AER-1');
    expect(html).toContain('no runner is driving it');
  });

  it('shows both rows of a two-row train, marking only the head', () => {
    const head = entry({ issue: issue({ key: 'AER-1', projectKey: 'AER', mergeWork: { kind: 'merge', sentence: '' } }) });
    const behind = entry({ issue: issue({ key: 'AER-2', projectKey: 'AER', mergeWork: { kind: 'update', sentence: '' } }) });
    const html = render('ready', [head, behind]);

    expect(html).toContain('AER-1');
    expect(html).toContain('AER-2');
    expect(html.match(/hatch-queue-train-head-word/g)).toHaveLength(1);
  });

  it("names the runner driving a train's project", () => {
    const row = entry({ issue: issue({ key: 'AER-1', projectKey: 'AER', mergeWork: { kind: 'merge', sentence: '' } }) });
    const html = render('ready', [row], null, [], () => {}, null, [runner({ name: 'Jeff Winger', drives: ['AER'] })]);

    expect(html).toContain('driven by Jeff Winger');
  });

  it('separates two projects into their own train blocks', () => {
    const a = entry({ issue: issue({ key: 'AER-1', projectKey: 'AER', mergeWork: { kind: 'merge', sentence: '' } }) });
    const b = entry({ issue: issue({ key: 'BER-1', projectKey: 'BER', mergeWork: { kind: 'merge', sentence: '' } }) });
    const html = render('ready', [a, b]);

    const trainsAt = html.indexOf('hatch-queue-trains');
    const aerAt = html.indexOf('>AER<', trainsAt);
    const berAt = html.indexOf('>BER<', trainsAt);
    const aer1At = html.indexOf('AER-1', trainsAt);
    const ber1At = html.indexOf('BER-1', trainsAt);

    expect(trainsAt).toBeGreaterThan(-1);
    expect(aerAt).toBeGreaterThan(-1);
    expect(berAt).toBeGreaterThan(aerAt);
    expect(aer1At).toBeGreaterThan(aerAt);
    expect(aer1At).toBeLessThan(berAt);
    expect(ber1At).toBeGreaterThan(berAt);
  });

  it('draws no train section when no row carries mergeWork', () => {
    const html = render('ready', [entry()]);

    expect(html).not.toContain('hatch-queue-trains');
  });
});

describe('QueueModal runner strip', () => {
  const now = new Date('2026-09-09T12:00:00Z');
  const strip = (runners: Runner[] | null, queue: QueueEntry[] = []) =>
    render('ready', queue, null, [], () => {}, null, runners, () => {}, now);

  it('draws the strip before the queue rows', () => {
    const html = strip([runner({ lastSeenAt: '2026-09-09T11:59:50Z' })], [entry()]);

    expect(html).toContain('hatch-queue-runners');
    expect(html.indexOf('hatch-queue-runners')).toBeLessThan(html.indexOf('hatch-queue-rows'));
  });

  it('draws a working runner with its issue link, its line and no gone class', () => {
    const html = strip([
      runner({ claimKey: 'AER-1', line: 'running make test-api', lastSeenAt: '2026-09-09T11:59:50Z' }),
    ]);

    expect(html).toContain('href="/issues/AER-1"');
    expect(html).toContain('running make test-api');
    expect(html).toContain('Working');
    expect(html).not.toContain('hatch-queue-runner--gone');
  });

  it('mutes a gone runner, says when it was heard from and draws no link', () => {
    const html = strip([
      runner({ claimKey: 'AER-1', line: 'running make test-api', lastSeenAt: '2026-09-09T09:00:00Z', goneAfterSeconds: 90 }),
    ]);

    expect(html).toContain('hatch-queue-runner--gone');
    expect(html).toContain('Gone');
    expect(html).toContain('3 hours ago');
    expect(html).not.toContain('href="/issues/');
  });

  it("keeps the array's order", () => {
    const html = strip([runner({ name: 'Jeff Winger' }), runner({ name: 'Britta Perry' })]);

    expect(html.indexOf('Britta Perry')).toBeGreaterThan(html.indexOf('Jeff Winger'));
    expect(html.lastIndexOf('Jeff Winger')).toBeLessThan(html.lastIndexOf('Britta Perry'));
  });

  it('says so, in one sentence and no list, when the list is empty', () => {
    const html = strip([]);

    expect(html).toContain('No runner has been heard from lately.');
    expect(html).not.toContain('hatch-queue-runners');
  });

  it('draws nothing while the list has not arrived', () => {
    const html = strip(null);

    expect(html).not.toContain('No runner has been heard from lately.');
    expect(html).not.toContain('hatch-queue-runners');
  });
});

import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { Capabilities, EpicsInFlight, Serves } from './IssuePage';
import { DatedAt } from '../components/DatedAt';
import type { Board, Capability, ChildRollup, Issue, IssueCard, IssueRollup, ServedCapability, Status } from '../types';

/* No DOM runner: both sections are rendered from props, the way
   PlanPage.test.tsx renders EpicCard. Mocked the same way BallotTree.test.tsx
   does - IssuePage.tsx pulls in MarkdownEditor, whose clientLogger import
   reads `window` at module load, which there is none of here. */
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

const statuses = [{ id: 1, name: 'In Review', color: '#3366cc' }] as Status[];

const board = (issues: Partial<IssueCard>[] = []): Board => ({
  statuses,
  issues: issues.map((i) => ({ key: i.key, type: 'epic', title: `title ${i.key}`, statusId: 1, ...i }) as IssueCard),
});

const ambition = { key: 'AER-1', type: 'ambition' } as Issue;
const epic = { key: 'AER-2', type: 'epic' } as Issue;

const capability = (over: Partial<Capability>): Capability => ({
  id: 1,
  position: 1,
  statement: 'statement',
  howToTell: 'how to tell',
  createdBy: 'someone',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  state: 'not_checked',
  newestVerdict: null,
  servedBy: [],
  ...over,
});

const renderCapabilities = (caps: Capability[] | null, b: Board = board(), error: string | null = null): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <Capabilities issue={ambition} board={b} capabilities={caps} error={error} />
    </MemoryRouter>,
  );

const renderServes = (serves: ServedCapability[] | null, issue: Issue = epic): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <Serves issue={issue} serves={serves} />
    </MemoryRouter>,
  );

describe('Capabilities', () => {
  it('returns null off an ambition', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <Capabilities issue={epic} board={board()} capabilities={[capability({})]} error={null} />
      </MemoryRouter>,
    );
    expect(html).toBe('');
  });

  it('says there are none and draws the add command', () => {
    const html = renderCapabilities([]);
    expect(html).toContain('no capabilities');
    expect(html).toContain('hatch capability add AER-1');
  });

  it('names each of the four states by word', () => {
    const html = renderCapabilities([
      capability({ id: 1, position: 1, statement: 'alpha', state: 'holds' }),
      capability({ id: 2, position: 2, statement: 'beta', state: 'does_not_hold' }),
      capability({ id: 3, position: 3, statement: 'gamma', state: 'cannot_tell' }),
      capability({ id: 4, position: 4, statement: 'delta', state: 'not_checked' }),
    ]);
    expect(html).toContain('Holds');
    expect(html).toContain('Does not hold');
    expect(html).toContain('Cannot tell');
    expect(html).toContain('Not checked');
  });

  it('draws rows in array order, not sorted', () => {
    const html = renderCapabilities([
      capability({ id: 1, position: 1, statement: 'zeta' }),
      capability({ id: 2, position: 2, statement: 'alpha' }),
    ]);
    expect(html.indexOf('zeta')).toBeLessThan(html.indexOf('alpha'));
  });

  it('shows the newest verdict for one that has one, and no verdict line for one that has none', () => {
    const html = renderCapabilities([
      capability({
        id: 1,
        state: 'holds',
        newestVerdict: {
          id: 1,
          capabilityId: 1,
          verdict: 'holds',
          lookedAt: 'the tests',
          sha: 'abcdef1234567',
          author: 'nathan',
          at: '2026-01-01T00:00:00Z',
        },
      }),
      capability({ id: 2, position: 2, state: 'not_checked', newestVerdict: null }),
    ]);
    expect(html).toContain('the tests');
    expect(html).toContain('abcdef1');
    expect(html).not.toContain('abcdef1234567');
    expect(html).toContain('by nathan, ');
    expect(html).toContain('dateTime="2026-01-01T00:00:00Z"');
    expect(html).toContain(`>${new Date('2026-01-01T00:00:00Z').toLocaleDateString()}</time>`);
    expect(html).toContain('title="');
  });

  it('draws no date for a capability with no verdict', () => {
    const html = renderCapabilities([capability({ id: 2, state: 'not_checked', newestVerdict: null })]);
    expect(html).toContain('Not checked');
    expect(html).not.toContain('<time');
  });

  it('links an epic serving it with its status pill, and bare-keys one off the board', () => {
    const html = renderCapabilities(
      [capability({ servedBy: ['AER-2', 'AER-3'] })],
      board([{ key: 'AER-2', statusId: 1 }]),
    );
    expect(html).toContain('href="/issues/AER-2"');
    expect(html).toContain('In Review');
    expect(html).toContain('AER-3');
    expect(html).not.toContain('href="/issues/AER-3"');
  });
});

describe('Serves', () => {
  it('returns null off an epic', () => {
    expect(renderServes([{ ambitionKey: 'AER-1', capabilityId: 1, position: 1, statement: 'a', state: 'holds' }], ambition)).toBe('');
  });

  it('returns null when it serves nothing', () => {
    expect(renderServes([])).toBe('');
    expect(renderServes(null)).toBe('');
  });

  it('draws the ambition key, position, statement and state per row, in array order', () => {
    const html = renderServes([
      { ambitionKey: 'AER-1', capabilityId: 1, position: 2, statement: 'zeta', state: 'holds' },
      { ambitionKey: 'AER-1', capabilityId: 2, position: 1, statement: 'alpha', state: 'not_checked' },
    ]);
    expect(html).toContain('href="/issues/AER-1"');
    expect(html).toContain('#2');
    expect(html).toContain('#1');
    expect(html).toContain('Holds');
    expect(html).toContain('Not checked');
    expect(html.indexOf('zeta')).toBeLessThan(html.indexOf('alpha'));
  });
});

describe('DatedAt', () => {
  it('prints the date inline and puts the full time and the ago phrase in the title', () => {
    const at = '2026-01-01T00:00:00Z';
    const html = renderToStaticMarkup(<DatedAt at={at} now={new Date('2026-01-03T00:00:00Z')} />);
    expect(html).toContain(`dateTime="${at}"`);
    expect(html).toContain(`>${new Date(at).toLocaleDateString()}</time>`);
    expect(html).toContain(new Date(at).toLocaleString());
    expect(html).toContain('2 days ago');
  });
});

describe('EpicsInFlight', () => {
  const columns = [
    { id: 1, name: 'In Progress', isTerminal: false, isDeferred: false, color: '#3366cc' },
    { id: 2, name: 'In Review', isTerminal: false, isDeferred: false, color: '#3366cc' },
    { id: 3, name: 'Done', isTerminal: true, isDeferred: false, color: '#3366cc' },
    { id: 4, name: 'Parked', isTerminal: false, isDeferred: true, color: '#3366cc' },
  ] as Status[];

  const child = (key: string, statusId: number): ChildRollup =>
    ({ issue: { key, type: 'epic', title: `title ${key}`, statusId }, isLeaf: false }) as ChildRollup;

  const rollupOf = (...children: ChildRollup[]): IssueRollup => ({ key: 'AER-1', children }) as IssueRollup;

  const renderEpics = (
    issue: Issue,
    rollup: IssueRollup | null,
    capabilities: Capability[] | null = [],
    error: string | null = null,
  ): string =>
    renderToStaticMarkup(
      <MemoryRouter>
        <EpicsInFlight issue={issue} rollup={rollup} error={error} capabilities={capabilities} statuses={columns} />
      </MemoryRouter>,
    );

  const caps = [
    capability({ id: 1, position: 1, statement: 'first', servedBy: ['AER-10'] }),
    capability({ id: 2, position: 2, statement: 'second', servedBy: ['AER-10', 'AER-11'] }),
  ];

  it('lists the open epics with their column and served capabilities, and leaves a closed one out', () => {
    const html = renderEpics(
      { ...ambition, wipLimit: null } as Issue,
      rollupOf(child('AER-10', 1), child('AER-11', 2), child('AER-12', 3)),
      caps,
    );
    expect(html).toContain('href="/issues/AER-10"');
    expect(html).toContain('href="/issues/AER-11"');
    expect(html).toContain('In Progress');
    expect(html).toContain('In Review');
    expect(html).toContain('#1');
    expect(html).toContain('#2');
    expect(html).toContain('2 open, runs 1 at once');
    expect(html).not.toContain('AER-12');
  });

  it('reads the ambition\'s own limit', () => {
    const html = renderEpics(
      { ...ambition, wipLimit: 2 } as Issue,
      rollupOf(child('AER-10', 1), child('AER-11', 2)),
      caps,
    );
    expect(html).toContain('2 open, runs 2 at once');
  });

  it('says so, and draws no list, with no open epic', () => {
    const html = renderEpics(ambition, rollupOf(child('AER-10', 3), child('AER-11', 4)), caps);
    expect(html).toContain('No epic is open under this ambition.');
    expect(html).not.toContain('<ul');
    expect(html).not.toContain('runs');
  });

  it.each([
    ['an epic', 'epic'],
    ['a story', 'story'],
    ['a task', 'task'],
  ])('draws nothing on %s', (_, type) => {
    expect(renderEpics({ key: 'AER-3', type } as Issue, rollupOf(child('AER-10', 1)), caps)).toBe('');
  });
});

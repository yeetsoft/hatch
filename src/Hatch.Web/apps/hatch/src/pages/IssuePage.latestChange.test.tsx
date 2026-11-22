import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { LatestChange } from './IssuePage';
import type { AmbitionChange, Board, ChangeEntry, ChangeOutcome, Issue, Status } from '../types';

/* No DOM runner, the same reason IssuePage.passes.test.tsx gives. */
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));
vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));

const ambition = { key: 'AER-1', type: 'ambition' } as Issue;

const status = (over: Partial<Status>): Status => ({
  id: 1,
  name: 'To Do',
  sortOrder: 1,
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
  color: '#888888',
  ...over,
});

const statuses: Status[] = [
  status({ id: 1, name: 'In Progress' }),
  status({ id: 2, name: 'Done', isTerminal: true }),
  status({ id: 3, name: 'Deferred', isDeferred: true }),
];

const board: Board = { statuses, issues: [] };

const entry = (over: Partial<ChangeEntry>): ChangeEntry => ({
  eventId: 1,
  kind: 'added',
  capabilityId: 1,
  position: 1,
  statementBefore: null,
  howToTellBefore: null,
  statementAfter: 'A partner can place an order without a phone call',
  howToTellAfter: 'Place one and count the calls',
  actor: 'nathan',
  at: '2026-01-01T00:00:00Z',
  ...over,
});

const outcome = (over: Partial<ChangeOutcome>): ChangeOutcome => ({
  id: 1,
  changeEventId: 1,
  epicKey: 'AER-2',
  epicTitle: 'A partner order form',
  epicColumn: 'In Progress',
  outcome: 'serves_nothing',
  why: 'what it served was removed',
  recordedBy: 'a session',
  recordedAt: '2026-01-01T00:00:00Z',
  decision: null,
  ...over,
});

const change = (over: Partial<AmbitionChange>): AmbitionChange => ({
  id: 1,
  savedAt: '2026-01-01T00:00:00Z',
  workedOut: true,
  workedOutAt: '2026-01-01T00:00:00Z',
  entries: [entry({})],
  outcomes: [],
  ...over,
});

const render = (
  changes: AmbitionChange[] | null,
  { issue = ambition, error = null as string | null, boardOverride = board } = {},
): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <LatestChange issue={issue} board={boardOverride} changes={changes} error={error} onDecided={async () => {}} />
    </MemoryRouter>,
  );

describe('LatestChange', () => {
  it.each([
    ['an epic', { key: 'AER-2', type: 'epic' } as Issue],
    ['a story', { key: 'AER-3', type: 'story' } as Issue],
    ['a task', { key: 'AER-4', type: 'task' } as Issue],
  ])('draws nothing on %s', (_, issue) => {
    expect(render([change({})], { issue })).toBe('');
  });

  it('draws nothing for an ambition that was never changed', () => {
    expect(render([])).toBe('');
    expect(render(null)).toBe('');
  });

  it('shows an error line rather than blanking the section', () => {
    const html = render(null, { error: 'could not be read' });
    expect(html).toContain('could not be read');
  });

  it('reads the entries of the first change only - outcomes from an older change are not drawn', () => {
    const newer = change({ id: 2, entries: [entry({ eventId: 10, kind: 'removed', statementBefore: 'gone' })] });
    const older = change({ id: 1, entries: [entry({ eventId: 1, kind: 'added', statementAfter: 'older' })] });
    const html = render([newer, older]);
    expect(html).toContain('gone');
    expect(html).not.toContain('older');
  });

  it('says a change not worked out has not been worked out, draws no epic lines and no presses', () => {
    const html = render([
      change({ workedOut: false, outcomes: [outcome({})] }),
    ]);
    expect(html).toContain('This change has not been worked out yet. A session will work out what it does to each epic.');
    expect(html).not.toContain('AER-2');
    expect(html).not.toContain('Keep it running');
    expect(html).not.toContain('Shelve it');
  });

  it('shows a badge and the words for an added entry', () => {
    const html = render([change({ entries: [entry({ kind: 'added', statementAfter: 'a new one' })] })]);
    expect(html).toContain('Added');
    expect(html).toContain('a new one');
    expect(html).not.toContain('<s>');
  });

  it('shows the old words struck through beside the new ones for a changed entry', () => {
    const html = render([
      change({
        entries: [entry({ kind: 'changed', statementBefore: 'the old words', statementAfter: 'the new words' })],
      }),
    ]);
    expect(html).toContain('Changed');
    expect(html).toContain('<s>the old words</s>');
    expect(html).toContain('the new words');
  });

  it('strikes through a removed entry with no new words', () => {
    const html = render([change({ entries: [entry({ kind: 'removed', statementBefore: 'gone now', statementAfter: null })] })]);
    expect(html).toContain('Removed');
    expect(html).toContain('<s>gone now</s>');
  });

  it('shows each outcome word with its why', () => {
    const html = render([
      change({
        outcomes: [
          outcome({ id: 1, epicKey: 'AER-2', outcome: 'carries_on', why: 'still holds' }),
          outcome({ id: 2, epicKey: 'AER-3', outcome: 'not_touched', why: 'nothing it serves changed' }),
          outcome({ id: 3, epicKey: 'AER-4', outcome: 'serves_nothing', why: 'what it served was removed' }),
        ],
      }),
    ]);
    expect(html).toContain('Carries on');
    expect(html).toContain('still holds');
    expect(html).toContain('Not touched');
    expect(html).toContain('nothing it serves changed');
    expect(html).toContain('Serves nothing now');
    expect(html).toContain('what it served was removed');
  });

  it('shows the presses only for an open, undecided, serves-nothing outcome', () => {
    const html = render([
      change({
        outcomes: [
          outcome({ id: 1, epicKey: 'AER-2', outcome: 'carries_on' }),
          outcome({ id: 2, epicKey: 'AER-3', outcome: 'not_touched' }),
          outcome({ id: 3, epicKey: 'AER-4', outcome: 'serves_nothing', epicColumn: 'In Progress' }),
        ],
      }),
    ]);
    const pressCount = html.split('Keep it running').length - 1;
    expect(pressCount).toBe(1);
    expect(html).toContain('AER-4');
  });

  it('shows a decided line in place of the presses', () => {
    const html = render([
      change({
        outcomes: [
          outcome({
            decision: { decision: 'shelve', decidedBy: 'nathan', decidedAt: '2026-01-02T00:00:00Z' },
          }),
        ],
      }),
    ]);
    expect(html).toContain('Shelved by nathan');
    expect(html).not.toContain('Keep it running');
    expect(html).not.toContain('Shelve it');
  });

  it('reads a kept decision as kept running', () => {
    const html = render([
      change({
        outcomes: [outcome({ decision: { decision: 'keep', decidedBy: 'nathan', decidedAt: '2026-01-02T00:00:00Z' } })],
      }),
    ]);
    expect(html).toContain('Kept running by nathan');
  });

  it('shows the column, not the presses, for an epic that has since closed', () => {
    const html = render([change({ outcomes: [outcome({ epicColumn: 'Done' })] })]);
    expect(html).not.toContain('Keep it running');
    expect(html).not.toContain('Shelve it');
  });

  it('shows the column, not the presses, for an epic that has since been deferred', () => {
    const html = render([change({ outcomes: [outcome({ epicColumn: 'Deferred' })] })]);
    expect(html).not.toContain('Keep it running');
    expect(html).not.toContain('Shelve it');
  });
});

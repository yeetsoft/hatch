import { describe, expect, it, vi } from 'vitest';
import { decideChangeOutcome } from '../api/client';
import {
  decideOutcome,
  decisionWord,
  entryLabel,
  entryTone,
  epicIsOpen,
  latestChange,
  outcomeTone,
  outcomeWord,
  shelveSentence,
  showsPresses,
} from './latestChange';
import type { AmbitionChange, ChangeOutcome, Status } from '../types';

vi.mock('../api/client', () => ({ decideChangeOutcome: vi.fn() }));

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
  status({ id: 1, name: 'To Do' }),
  status({ id: 2, name: 'In Progress' }),
  status({ id: 3, name: 'Done', isTerminal: true }),
  status({ id: 4, name: 'Deferred', isDeferred: true }),
];

const outcome = (over: Partial<ChangeOutcome>): ChangeOutcome => ({
  id: 1,
  changeEventId: 1,
  epicKey: 'AER-2',
  epicTitle: 'An epic',
  epicColumn: 'To Do',
  outcome: 'serves_nothing',
  why: 'what was removed is all it served',
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
  entries: [],
  outcomes: [],
  ...over,
});

describe('latestChange', () => {
  it('is null with no changes', () => {
    expect(latestChange(null)).toBeNull();
    expect(latestChange([])).toBeNull();
  });

  it('is the first - changes answer newest first', () => {
    const newer = change({ id: 2 });
    const older = change({ id: 1 });
    expect(latestChange([newer, older])).toBe(newer);
  });
});

describe('entryTone', () => {
  it('reads added as success, changed as primary, removed as danger', () => {
    expect(entryTone('added')).toBe('success');
    expect(entryTone('changed')).toBe('primary');
    expect(entryTone('removed')).toBe('danger');
  });
});

describe('entryLabel', () => {
  it('capitalizes the kind', () => {
    expect(entryLabel('added')).toBe('Added');
    expect(entryLabel('changed')).toBe('Changed');
    expect(entryLabel('removed')).toBe('Removed');
  });
});

describe('outcomeWord', () => {
  it('reads each of the three words', () => {
    expect(outcomeWord('carries_on')).toBe('Carries on');
    expect(outcomeWord('not_touched')).toBe('Not touched');
    expect(outcomeWord('serves_nothing')).toBe('Serves nothing now');
  });
});

describe('outcomeTone', () => {
  it('tones each of the three outcomes', () => {
    expect(outcomeTone('carries_on')).toBe('success');
    expect(outcomeTone('not_touched')).toBe('muted');
    expect(outcomeTone('serves_nothing')).toBe('warn');
  });
});

describe('decisionWord', () => {
  it('reads shelve and keep in the past tense', () => {
    expect(decisionWord('shelve')).toBe('Shelved');
    expect(decisionWord('keep')).toBe('Kept running');
  });
});

describe('epicIsOpen', () => {
  it('is true for a column that is neither terminal nor deferred', () => {
    expect(epicIsOpen('In Progress', statuses)).toBe(true);
  });

  it('is false for a terminal column', () => {
    expect(epicIsOpen('Done', statuses)).toBe(false);
  });

  it('is false for a deferred column', () => {
    expect(epicIsOpen('Deferred', statuses)).toBe(false);
  });

  it('is false for a column the board no longer carries', () => {
    expect(epicIsOpen('Gone', statuses)).toBe(false);
  });
});

describe('showsPresses', () => {
  it('shows for an open epic that serves nothing and has no decision', () => {
    expect(showsPresses(outcome({ epicColumn: 'To Do' }), statuses)).toBe(true);
  });

  it('does not show for carries on or not touched', () => {
    expect(showsPresses(outcome({ outcome: 'carries_on' }), statuses)).toBe(false);
    expect(showsPresses(outcome({ outcome: 'not_touched' }), statuses)).toBe(false);
  });

  it('does not show once decided', () => {
    const decided = outcome({ decision: { decision: 'keep', decidedBy: 'nathan', decidedAt: '2026-01-01T00:00:00Z' } });
    expect(showsPresses(decided, statuses)).toBe(false);
  });

  it('does not show for an epic that has since closed or been deferred', () => {
    expect(showsPresses(outcome({ epicColumn: 'Done' }), statuses)).toBe(false);
    expect(showsPresses(outcome({ epicColumn: 'Deferred' }), statuses)).toBe(false);
  });
});

describe('shelveSentence', () => {
  it('names the count and the deferred column for one or more open issues', () => {
    expect(shelveSentence('AER-2', 3, 'Deferred')).toBe(
      'Shelve AER-2? It and the 3 open issues under it move to Deferred.',
    );
  });

  it('is singular at one', () => {
    expect(shelveSentence('AER-2', 1, 'Deferred')).toBe('Shelve AER-2? It and the 1 open issue under it move to Deferred.');
  });

  it('says nothing is open at zero', () => {
    expect(shelveSentence('AER-2', 0, 'Deferred')).toBe('Shelve AER-2? Nothing under it is open.');
  });
});

describe('decideOutcome', () => {
  it('calls decideChangeOutcome and answers null once it goes through', async () => {
    vi.mocked(decideChangeOutcome).mockResolvedValueOnce({} as never);

    await expect(decideOutcome('AER-1', 5, 'keep')).resolves.toBeNull();

    expect(decideChangeOutcome).toHaveBeenCalledWith('AER-1', 5, { decision: 'keep' });
  });

  it('answers the server refusal sentence instead of throwing', async () => {
    vi.mocked(decideChangeOutcome).mockRejectedValueOnce(new Error('a person has already decided'));

    await expect(decideOutcome('AER-1', 5, 'shelve')).resolves.toBe('a person has already decided');
  });
});

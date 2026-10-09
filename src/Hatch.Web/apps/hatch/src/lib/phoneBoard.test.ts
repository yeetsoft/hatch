import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  BOARD_VIEW_STORAGE_KEY,
  columnSections,
  readBoardView,
  resolveBoardLayout,
  startsCollapsed,
  writeBoardView,
} from './phoneBoard';
import { boardColumns } from './columns';
import type { Status } from '../types';

/** A localStorage that holds what it is given, and can be made to throw - the
    same stub confirmationLifetime.test.ts uses for its storage pair. */
function stubStorage(initial: Record<string, string> = {}, throws = false) {
  const data = { ...initial };
  const storage = {
    getItem: (k: string) => {
      if (throws) throw new Error('blocked');
      return k in data ? data[k] : null;
    },
    setItem: (k: string, v: string) => {
      if (throws) throw new Error('blocked');
      data[k] = v;
    },
  };
  vi.stubGlobal('window', { localStorage: storage });
  return data;
}

afterEach(() => vi.unstubAllGlobals());

const status = (over: Partial<Status>): Status => ({
  id: 1,
  name: 'Some column',
  sortOrder: 0,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  color: '#000000',
  ...over,
});

describe('columnSections', () => {
  it('is boardColumns, so the two layouts can never disagree about column order', () => {
    expect(columnSections).toBe(boardColumns);
  });
});

describe('startsCollapsed', () => {
  it('is true for a terminal column', () => {
    expect(startsCollapsed(status({ isTerminal: true }))).toBe(true);
  });

  it('is true for a deferred column', () => {
    expect(startsCollapsed(status({ isDeferred: true }))).toBe(true);
  });

  it('is false for an ordinary column', () => {
    expect(startsCollapsed(status({}))).toBe(false);
  });
});

describe('readBoardView / writeBoardView', () => {
  it('defaults to stacked when nobody has chosen', () => {
    stubStorage();

    expect(readBoardView()).toBe('stacked');
  });

  it('round-trips both choices', () => {
    stubStorage();
    writeBoardView('full');
    expect(readBoardView()).toBe('full');

    writeBoardView('stacked');
    expect(readBoardView()).toBe('stacked');
  });

  it('falls back to stacked for a value this build does not offer', () => {
    stubStorage({ [BOARD_VIEW_STORAGE_KEY]: 'kanban' });

    expect(readBoardView()).toBe('stacked');
  });

  it('falls back to stacked when storage throws', () => {
    stubStorage({}, true);

    expect(readBoardView()).toBe('stacked');
  });

  it('does not throw when storage cannot be written', () => {
    stubStorage({}, true);

    expect(() => writeBoardView('full')).not.toThrow();
  });
});

describe('resolveBoardLayout', () => {
  it('is always full off the phone, whatever is stored', () => {
    expect(resolveBoardLayout(false, 'stacked')).toBe('full');
    expect(resolveBoardLayout(false, 'full')).toBe('full');
  });

  it('echoes the stored view on the phone', () => {
    expect(resolveBoardLayout(true, 'stacked')).toBe('stacked');
    expect(resolveBoardLayout(true, 'full')).toBe('full');
  });
});

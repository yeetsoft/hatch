import { afterEach, describe, expect, it, vi } from 'vitest';
import { ISSUE_SECTIONS_STORAGE_KEY, readSectionStates, sectionOpens, writeSectionState } from './issueSections';

/** A localStorage that holds what it is given, and can be made to throw - the
    same stub phoneBoard.test.ts uses for its own storage pair. */
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

describe('sectionOpens', () => {
  it('opens when the stored choice is open', () => {
    expect(sectionOpens({ history: 'open' }, 'history', false)).toBe(true);
  });

  it('closes when the stored choice is closed', () => {
    expect(sectionOpens({ comments: 'closed' }, 'comments', true)).toBe(false);
  });

  it('falls back to the default when nothing is stored for that section', () => {
    expect(sectionOpens({}, 'comments', true)).toBe(true);
    expect(sectionOpens({ comments: 'open' }, 'history', false)).toBe(false);
  });
});

describe('readSectionStates / writeSectionState', () => {
  it('reads nothing when nothing has been chosen', () => {
    stubStorage();

    expect(readSectionStates()).toEqual({});
  });

  it('round-trips a choice under its own id, leaving the others alone', () => {
    stubStorage();
    writeSectionState('history', false);
    writeSectionState('comments', true);

    expect(readSectionStates()).toEqual({ history: 'closed', comments: 'open' });
  });

  it('falls back to empty when storage throws', () => {
    stubStorage({}, true);

    expect(readSectionStates()).toEqual({});
  });

  it('falls back to empty when the stored value is not valid JSON', () => {
    stubStorage({ [ISSUE_SECTIONS_STORAGE_KEY]: 'not json' });

    expect(readSectionStates()).toEqual({});
  });

  it('does not throw when storage cannot be written', () => {
    stubStorage({}, true);

    expect(() => writeSectionState('history', true)).not.toThrow();
  });
});

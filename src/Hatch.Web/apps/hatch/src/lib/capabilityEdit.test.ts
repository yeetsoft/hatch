import { describe, expect, it, vi } from 'vitest';
import { addCapability, editCapability, moveCapabilities, removeCapability } from '../api/client';
import type { Capability } from '../types';
import {
  canMoveDown,
  canMoveUp,
  draftOk,
  implementationNote,
  removeSentence,
  reordered,
  writeAddCapability,
  writeCapabilityOrder,
  writeEditCapability,
  writeRemoveCapability,
} from './capabilityEdit';

vi.mock('../api/client', () => ({
  addCapability: vi.fn(),
  editCapability: vi.fn(),
  moveCapabilities: vi.fn(),
  removeCapability: vi.fn(),
}));

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

describe('draftOk', () => {
  it('holds for two non-blank lines', () => {
    expect(draftOk('a', 'b')).toBe(true);
  });

  it('fails for a blank statement', () => {
    expect(draftOk('  ', 'b')).toBe(false);
  });

  it('fails for a blank how-to-tell line', () => {
    expect(draftOk('a', '  ')).toBe(false);
  });
});

describe('canMoveUp', () => {
  it('is false at the first row', () => {
    expect(canMoveUp(0)).toBe(false);
  });

  it('is true past the first row', () => {
    expect(canMoveUp(1)).toBe(true);
  });
});

describe('canMoveDown', () => {
  it('is false at the last row', () => {
    expect(canMoveDown(2, 3)).toBe(false);
  });

  it('is true short of the last row', () => {
    expect(canMoveDown(1, 3)).toBe(true);
  });
});

describe('reordered', () => {
  it('swaps a row up with its neighbour', () => {
    expect(reordered([1, 2, 3], 2, 'up')).toEqual([2, 1, 3]);
  });

  it('swaps a row down with its neighbour', () => {
    expect(reordered([1, 2, 3], 2, 'down')).toEqual([1, 3, 2]);
  });

  it('is null moving the first row up', () => {
    expect(reordered([1, 2, 3], 1, 'up')).toBeNull();
  });

  it('is null moving the last row down', () => {
    expect(reordered([1, 2, 3], 3, 'down')).toBeNull();
  });

  it('is null for an id not in the list', () => {
    expect(reordered([1, 2, 3], 9, 'up')).toBeNull();
  });
});

describe('removeSentence', () => {
  it('says plainly that none serves it', () => {
    expect(removeSentence(capability({ position: 2, servedBy: [] }))).toBe('No epic serves this capability.');
  });

  it('names the one epic serving it', () => {
    expect(removeSentence(capability({ position: 1, servedBy: ['AER-2'] }))).toBe(
      'AER-2 serves this capability, and will be unlinked.',
    );
  });

  it('names every epic serving it, joined with and', () => {
    expect(removeSentence(capability({ position: 1, servedBy: ['AER-2', 'AER-3'] }))).toBe(
      'AER-2 and AER-3 serve this capability, and will be unlinked.',
    );
  });
});

describe('implementationNote', () => {
  it('says a session works out the consequences', () => {
    expect(implementationNote).toBe('A session will work out what a change does to the epics in flight.');
  });
});

describe('writeAddCapability', () => {
  it('trims both lines and answers null on success', async () => {
    vi.mocked(addCapability).mockResolvedValueOnce({} as never);

    await expect(writeAddCapability('AER-1', ' a ', ' b ')).resolves.toBeNull();

    expect(addCapability).toHaveBeenCalledWith('AER-1', { statement: 'a', howToTell: 'b' });
  });

  it('answers the server refusal instead of throwing', async () => {
    vi.mocked(addCapability).mockRejectedValueOnce(new Error('a capability needs a statement and a how-to-tell line'));

    await expect(writeAddCapability('AER-1', '', '')).resolves.toBe(
      'a capability needs a statement and a how-to-tell line',
    );
  });
});

describe('writeEditCapability', () => {
  it('trims both lines and answers null on success', async () => {
    vi.mocked(editCapability).mockResolvedValueOnce({} as never);

    await expect(writeEditCapability('AER-1', 5, ' a ', ' b ')).resolves.toBeNull();

    expect(editCapability).toHaveBeenCalledWith('AER-1', 5, { statement: 'a', howToTell: 'b' });
  });

  it('answers the server refusal instead of throwing', async () => {
    vi.mocked(editCapability).mockRejectedValueOnce(new Error('a statement is at most 500 characters'));

    await expect(writeEditCapability('AER-1', 5, 'a', 'b')).resolves.toBe('a statement is at most 500 characters');
  });
});

describe('writeCapabilityOrder', () => {
  it('writes the order and answers null on success', async () => {
    vi.mocked(moveCapabilities).mockResolvedValueOnce([] as never);

    await expect(writeCapabilityOrder('AER-1', [2, 1])).resolves.toBeNull();

    expect(moveCapabilities).toHaveBeenCalledWith('AER-1', [2, 1]);
  });

  it('answers the server refusal instead of throwing', async () => {
    vi.mocked(moveCapabilities).mockRejectedValueOnce(new Error('the order names a capability twice'));

    await expect(writeCapabilityOrder('AER-1', [1, 1])).resolves.toBe('the order names a capability twice');
  });
});

describe('writeRemoveCapability', () => {
  it('removes it and answers null on success', async () => {
    vi.mocked(removeCapability).mockResolvedValueOnce(undefined);

    await expect(writeRemoveCapability('AER-1', 5)).resolves.toBeNull();

    expect(removeCapability).toHaveBeenCalledWith('AER-1', 5);
  });

  it('answers the server refusal instead of throwing', async () => {
    vi.mocked(removeCapability).mockRejectedValueOnce(new Error('writing a capability past the implementation column is a person\'s call, not an agent\'s'));

    await expect(writeRemoveCapability('AER-1', 5)).resolves.toBe(
      "writing a capability past the implementation column is a person's call, not an agent's",
    );
  });
});

import { describe, expect, it, vi } from 'vitest';
import { putIssueServes } from '../api/client';
import type { ServedCapability } from '../types';
import { findAmbitionAbove, sameSet, servedIds, toggleId, writeServes } from './serves';

vi.mock('../api/client', () => ({ getIssue: vi.fn(), putIssueServes: vi.fn() }));

const row = (capabilityId: number): ServedCapability => ({
  ambitionKey: 'AER-1',
  capabilityId,
  position: capabilityId,
  statement: 'statement',
  state: 'not_checked',
});

describe('servedIds', () => {
  it('lists the capability ids in order, and none for null', () => {
    expect(servedIds([row(3), row(1)])).toEqual([3, 1]);
    expect(servedIds(null)).toEqual([]);
  });
});

describe('toggleId', () => {
  it('adds an id that was not there', () => {
    expect(toggleId([1], 2)).toEqual([1, 2]);
  });

  it('removes an id that was', () => {
    expect(toggleId([1, 2], 1)).toEqual([2]);
  });

  it('leaves the draft it was given alone', () => {
    const draft = [1];
    toggleId(draft, 2);
    expect(draft).toEqual([1]);
  });
});

describe('sameSet', () => {
  it('ignores order', () => {
    expect(sameSet([1, 2], [2, 1])).toBe(true);
    expect(sameSet([], [])).toBe(true);
  });

  it('rejects a different size or a different member', () => {
    expect(sameSet([1], [1, 2])).toBe(false);
    expect(sameSet([1, 2], [1, 3])).toBe(false);
  });
});

describe('writeServes', () => {
  it('sends the whole set once and answers null', async () => {
    vi.mocked(putIssueServes).mockResolvedValueOnce([] as never);

    await expect(writeServes('AER-2', [1, 3])).resolves.toBeNull();

    expect(putIssueServes).toHaveBeenCalledTimes(1);
    expect(putIssueServes).toHaveBeenCalledWith('AER-2', [1, 3]);
  });

  it('answers the refusal sentence instead of throwing', async () => {
    vi.mocked(putIssueServes).mockRejectedValueOnce(new Error('capability 9 belongs to another ambition'));

    await expect(writeServes('AER-2', [9])).resolves.toBe('capability 9 belongs to another ambition');
  });
});

describe('findAmbitionAbove', () => {
  type Row = { key: string; type: 'ambition' | 'epic'; parentKey: string | null };
  const reading = (rows: Row[]) => async (key: string) => {
    const found = rows.find((r) => r.key === key);
    if (!found) throw new Error('not found');

    return found;
  };

  it('finds an ambition two epics up', async () => {
    const read = reading([
      { key: 'AER-3', type: 'epic', parentKey: 'AER-2' },
      { key: 'AER-2', type: 'epic', parentKey: 'AER-1' },
      { key: 'AER-1', type: 'ambition', parentKey: null },
    ]);

    await expect(findAmbitionAbove({ key: 'AER-4', parentKey: 'AER-3' }, read)).resolves.toBe('AER-1');
  });

  it('answers null at a root with no ambition', async () => {
    const read = reading([{ key: 'AER-2', type: 'epic', parentKey: null }]);

    await expect(findAmbitionAbove({ key: 'AER-3', parentKey: 'AER-2' }, read)).resolves.toBeNull();
  });

  it('answers null for an issue with no parent', async () => {
    await expect(findAmbitionAbove({ key: 'AER-3', parentKey: null }, reading([]))).resolves.toBeNull();
  });

  it('survives a cycle', async () => {
    const read = reading([
      { key: 'AER-2', type: 'epic', parentKey: 'AER-3' },
      { key: 'AER-3', type: 'epic', parentKey: 'AER-2' },
    ]);

    await expect(findAmbitionAbove({ key: 'AER-4', parentKey: 'AER-2' }, read)).resolves.toBeNull();
  });

  it('answers null when a parent cannot be read', async () => {
    await expect(findAmbitionAbove({ key: 'AER-4', parentKey: 'AER-2' }, reading([]))).resolves.toBeNull();
  });
});

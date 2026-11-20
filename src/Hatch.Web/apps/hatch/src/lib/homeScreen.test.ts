import { describe, expect, it, vi } from 'vitest';
import { chooseHome, homeCardApplies, homeOf } from './homeScreen';
import type { Me } from '../types';

const meOf = (over: Partial<Me> = {}): Me => ({
  kind: 'person',
  name: '',
  role: 'user',
  configured: true,
  canSignOut: true,
  canSignIn: false,
  ...over,
});

describe('homeCardApplies', () => {
  it('is true for a person', () => {
    expect(homeCardApplies(meOf({ kind: 'person' }))).toBe(true);
  });

  it('is true for the local person with the wall off', () => {
    expect(homeCardApplies(meOf({ kind: 'local' }))).toBe(true);
  });

  it('is false for a browser nobody has signed into', () => {
    expect(homeCardApplies(meOf({ kind: 'none' }))).toBe(false);
  });

  it('is false for a key', () => {
    expect(homeCardApplies(meOf({ kind: 'key' }))).toBe(false);
  });

  it('is false before the first read has answered', () => {
    expect(homeCardApplies(null)).toBe(false);
  });
});

describe('homeOf', () => {
  it('is ambitions when that is saved', () => {
    expect(homeOf(meOf({ home: 'ambitions' }))).toBe('ambitions');
  });

  it('is board when that is saved', () => {
    expect(homeOf(meOf({ home: 'board' }))).toBe('board');
  });

  it('is board when nothing is saved', () => {
    expect(homeOf(meOf({ home: undefined }))).toBe('board');
  });

  it('is board with no caller at all', () => {
    expect(homeOf(null)).toBe('board');
  });
});

describe('chooseHome', () => {
  it('writes the choice, announces once, and returns the answered value', async () => {
    const put = vi.fn().mockResolvedValue(meOf({ home: 'ambitions' }));
    const announce = vi.fn();

    const result = await chooseHome('ambitions', { put, announce });

    expect(put).toHaveBeenCalledTimes(1);
    expect(put).toHaveBeenCalledWith('ambitions');
    expect(announce).toHaveBeenCalledTimes(1);
    expect(result).toEqual({ saved: 'ambitions' });
  });

  it('returns the refusal sentence and never announces', async () => {
    const put = vi.fn().mockRejectedValue(new Error('Nobody has signed in.'));
    const announce = vi.fn();

    const result = await chooseHome('ambitions', { put, announce });

    expect(announce).not.toHaveBeenCalled();
    expect(result).toEqual({ error: 'Nobody has signed in.' });
  });
});

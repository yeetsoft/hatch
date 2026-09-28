import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  DEFAULT_LIFETIME,
  LIFETIME_CHOICES,
  LIFETIME_STORAGE_KEY,
  readLifetime,
  writeLifetime,
} from './confirmationLifetime';

/** A localStorage that holds what it is given, and can be made to throw. */
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

describe('readLifetime', () => {
  it('is fifteen seconds when nobody has chosen', () => {
    stubStorage();

    expect(readLifetime()).toBe(15_000);
    expect(DEFAULT_LIFETIME).toBe(15_000);
  });

  it.each([10_000, 15_000, 30_000, 60_000])('reads back %i', (ms) => {
    stubStorage();
    writeLifetime(ms);

    expect(readLifetime()).toBe(ms);
  });

  it('reads never as null', () => {
    const data = stubStorage();
    writeLifetime(null);

    expect(data[LIFETIME_STORAGE_KEY]).toBe('never');
    expect(readLifetime()).toBeNull();
  });

  it('falls back to the default for a value this build does not offer', () => {
    for (const stored of ['soon', '', '12345', '-1', '0', 'NaN']) {
      stubStorage({ [LIFETIME_STORAGE_KEY]: stored });

      expect(readLifetime()).toBe(15_000);
    }
  });

  it('falls back to the default when storage throws', () => {
    stubStorage({}, true);

    expect(readLifetime()).toBe(15_000);
  });
});

describe('writeLifetime', () => {
  it('does not throw when storage cannot be written', () => {
    stubStorage({}, true);

    expect(() => writeLifetime(30_000)).not.toThrow();
  });
});

describe('LIFETIME_CHOICES', () => {
  it('offers 10 seconds, 15 seconds, 30 seconds, 1 minute and Never, in that order', () => {
    expect(LIFETIME_CHOICES.map((c) => c.label)).toEqual(['10 seconds', '15 seconds', '30 seconds', '1 minute', 'Never']);
    expect(LIFETIME_CHOICES.map((c) => c.value)).toEqual([10_000, 15_000, 30_000, 60_000, null]);
  });

  it('includes the default', () => {
    expect(LIFETIME_CHOICES.some((c) => c.value === DEFAULT_LIFETIME)).toBe(true);
  });
});

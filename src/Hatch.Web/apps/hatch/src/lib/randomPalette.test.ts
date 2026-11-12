/* What Random needs beyond the store: rolling, checking a pasted seed, and the
   two stored keys. Plain Node, with localStorage stubbed as themePalette.test.ts
   does. */
import {
  checkSeed,
  LAST_PALETTE_STORAGE_KEY,
  readLastSeed,
  readPalette,
  rollSeed,
  storePalette,
} from '@hatch/ui';
import { decodeSeed, PARAMETER_KEYS, PARAMETERS, makeSeedCodec, IDENTITY } from '@hatch/ui/palette';
import { afterEach, describe, expect, it, vi } from 'vitest';

function mulberry32(seed: number): () => number {
  let state = seed;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function stubStorage(fail = false) {
  const items = new Map<string, string>();
  const refuse = () => {
    if (fail) throw new Error('locked');
  };
  vi.stubGlobal('window', {
    localStorage: {
      getItem: (key: string) => {
        refuse();
        return items.get(key) ?? null;
      },
      setItem: (key: string, value: string) => {
        refuse();
        items.set(key, value);
      },
      removeItem: (key: string) => {
        refuse();
        items.delete(key);
      },
    },
    matchMedia: () => ({ matches: false }),
  });
  return items;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('rollSeed', () => {
  it('rolls a seed the engine reads, inside every roll window', () => {
    const random = mulberry32(2259);
    for (let at = 0; at < 50; at++) {
      const decoded = decodeSeed(rollSeed(random));
      if (!decoded.ok) throw new Error(decoded.message);
      for (const key of PARAMETER_KEYS) {
        const { window, min, max } = PARAMETERS[key];
        const value = decoded.params[key];
        expect(value).toBeGreaterThanOrEqual(min);
        expect(value).toBeLessThanOrEqual(max);
        /* The seed quantises to 256 steps, so a window edge reads back up to a step off. */
        const step = (max - min) / 255;
        if ('lo' in window) {
          expect(value).toBeGreaterThanOrEqual(window.lo - step);
          expect(value).toBeLessThanOrEqual(window.hi + step);
        }
      }
    }
  });

  it('is the same seed for the same draws, and a different one for others', () => {
    expect(rollSeed(mulberry32(1))).toBe(rollSeed(mulberry32(1)));
    expect(rollSeed(mulberry32(1))).not.toBe(rollSeed(mulberry32(2)));
  });

  it('draws from crypto by default and Math.random without it', () => {
    expect(decodeSeed(rollSeed()).ok).toBe(true);
    vi.stubGlobal('crypto', undefined);
    expect(decodeSeed(rollSeed()).ok).toBe(true);
  });
});

describe('checkSeed', () => {
  const good = rollSeed(mulberry32(7));

  it('accepts a seed and trims the whitespace around it', () => {
    expect(checkSeed(`  ${good}\n`)).toEqual({ ok: true, seed: good });
  });

  it.each(['', 'hx2-AAAAAAAA', 'hx1-short', 'hx1-!!!!!!!!', 'hello'])('refuses %j in words', (input) => {
    const checked = checkSeed(input);
    expect(checked.ok).toBe(false);
    if (!checked.ok) expect(checked.message.length).toBeGreaterThan(10);
  });
});

describe('storePalette', () => {
  const seed = rollSeed(mulberry32(11));

  it('applies a seed and remembers it as the last', () => {
    const items = stubStorage();
    storePalette(seed);
    expect(items.get('hatch.palette')).toBe(seed);
    expect(items.get(LAST_PALETTE_STORAGE_KEY)).toBe(seed);
    expect(readPalette()).toBe(seed);
    expect(readLastSeed()).toBe(seed);
  });

  it('forgets the applied seed for null and keeps the last', () => {
    stubStorage();
    storePalette(seed);
    storePalette(null);
    expect(readPalette()).toBeNull();
    expect(readLastSeed()).toBe(seed);
  });

  it('reads a missing, malformed or older-engine last seed as null', () => {
    const items = stubStorage();
    expect(readLastSeed()).toBeNull();
    items.set(LAST_PALETTE_STORAGE_KEY, 'not a seed');
    expect(readLastSeed()).toBeNull();
    items.set(LAST_PALETTE_STORAGE_KEY, makeSeedCodec(0).encodeSeed(IDENTITY));
    expect(readLastSeed()).toBeNull();
  });

  it('survives a store that throws', () => {
    stubStorage(true);
    expect(readLastSeed()).toBeNull();
    expect(() => storePalette(seed)).not.toThrow();
    expect(() => storePalette(null)).not.toThrow();
  });
});

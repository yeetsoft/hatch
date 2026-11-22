import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  STRIP_SHOWN_STORAGE_KEY,
  STRIP_WINDOW_STORAGE_KEY,
  readStripShown,
  readStripWindow,
  writeStripShown,
  writeStripWindow,
} from './boardStrip';

/** A localStorage that holds what it is given, and can be made to throw - the
    same stub filter.test.ts uses for its storage pairs. */
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

describe('the keys', () => {
  it('sit next to the board choices', () => {
    expect(STRIP_SHOWN_STORAGE_KEY).toBe('hatch.board.strip.shown');
    expect(STRIP_WINDOW_STORAGE_KEY).toBe('hatch.board.strip.window');
  });
});

describe('the strip being shown', () => {
  it('is shown on a first visit', () => {
    stubStorage();

    expect(readStripShown()).toBe(true);
  });

  it('reads back what was written, both ways', () => {
    const data = stubStorage();

    writeStripShown(false);
    expect(data[STRIP_SHOWN_STORAGE_KEY]).toBe('false');
    expect(readStripShown()).toBe(false);

    writeStripShown(true);
    expect(data[STRIP_SHOWN_STORAGE_KEY]).toBe('true');
    expect(readStripShown()).toBe(true);
  });

  it('reads anything but false as shown', () => {
    for (const stored of ['', 'true', 'no', '0', 'FALSE']) {
      stubStorage({ [STRIP_SHOWN_STORAGE_KEY]: stored });
      expect(readStripShown()).toBe(true);
    }
  });

  it('is shown when the store throws, and a write swallows the throw', () => {
    stubStorage({}, true);

    expect(readStripShown()).toBe(true);
    expect(() => writeStripShown(false)).not.toThrow();
  });
});

describe('the strip window', () => {
  it('is eight hours on a first visit', () => {
    stubStorage();

    expect(readStripWindow()).toBe('8h');
  });

  it('reads back every window it offers', () => {
    const data = stubStorage();

    for (const choice of ['2h', '4h', '8h', '12h', '24h'] as const) {
      writeStripWindow(choice);
      expect(data[STRIP_WINDOW_STORAGE_KEY]).toBe(choice);
      expect(readStripWindow()).toBe(choice);
    }
  });

  it('falls back to eight hours for a stored value it does not offer', () => {
    for (const stored of ['', '3h', '1d', 'banana', '8H']) {
      stubStorage({ [STRIP_WINDOW_STORAGE_KEY]: stored });
      expect(readStripWindow()).toBe('8h');
    }
  });

  it('is eight hours when the store throws, and a write swallows the throw', () => {
    stubStorage({}, true);

    expect(readStripWindow()).toBe('8h');
    expect(() => writeStripWindow('24h')).not.toThrow();
  });
});

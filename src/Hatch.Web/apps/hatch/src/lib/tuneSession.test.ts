/* The configurator's state machine (packages/ui/src/theme/tuneSession.ts), driven the way the component drives it:
   fake ports for the preview and the frame, and the real `storePalette` over a stubbed localStorage as the apply
   port, so "writes" and "leaves storage as found" are read off the store itself. Plain Node, as
   randomPalette.test.ts is - the component's portal and focus are for the driver. */
import { decodeSeed, encodeSeed, IDENTITY, PARAMETERS, rollParams, type PaletteParams } from '@hatch/ui/palette';
import { createTuneSession, type TuneSession } from '@hatch/ui/tuneSession';
import { LAST_PALETTE_STORAGE_KEY, storePalette } from '@hatch/ui';
import { PALETTE_STORAGE_KEY } from '@hatch/ui/themeStore';
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

function stubStorage(initial: Record<string, string> = {}) {
  const items = new Map<string, string>(Object.entries(initial));
  vi.stubGlobal('window', {
    localStorage: {
      getItem: (key: string) => items.get(key) ?? null,
      setItem: (key: string, value: string) => void items.set(key, value),
      removeItem: (key: string) => void items.delete(key),
    },
    matchMedia: () => ({ matches: false }),
  });
  return items;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

const STORED: PaletteParams = { hue: 200, spread: 1.1, chroma: 1.2, lift: 0.01, tint: 0.012, tintHue: 210 };
/** What a seed holds of a vector: the codec quantises, so a comparison goes through it. */
const held = (params: PaletteParams): PaletteParams => {
  const decoded = decodeSeed(encodeSeed(params));
  if (!decoded.ok) throw new Error(decoded.message);
  return decoded.params;
};
const STORED_SEED = encodeSeed(STORED);

/** A session over fake ports. `frames` is the queue of callbacks `schedule` was given; `runFrame` is the browser
    getting to the next frame. */
function harness(snapshot: string | null, initial: Record<string, string> = {}) {
  const storage = stubStorage(initial);
  const previews: (string | null)[] = [];
  const frames: { run: () => void; cancelled: boolean }[] = [];
  const session: TuneSession = createTuneSession(snapshot, {
    preview: (seed) => previews.push(seed),
    apply: (seed) => storePalette(seed),
    schedule: (run) => {
      const frame = { run, cancelled: false };
      frames.push(frame);
      return () => {
        frame.cancelled = true;
      };
    },
  });
  const runFrame = () => {
    for (const frame of frames.splice(0)) if (!frame.cancelled) frame.run();
  };
  return { session, storage, previews, frames, runFrame };
}

const stored = (seed: string) => ({ [PALETTE_STORAGE_KEY]: seed, [LAST_PALETTE_STORAGE_KEY]: seed });

describe('opening a session', () => {
  it('starts from the vector of the stored seed, and previews nothing', () => {
    const { session, previews, frames } = harness(STORED_SEED, stored(STORED_SEED));
    expect(session.getState().params).toEqual(held(STORED));
    expect(session.getState().seed).toBe(STORED_SEED);
    expect(session.getState().refusal).toBeNull();
    expect(previews).toEqual([]);
    expect(frames).toEqual([]);
  });

  it('starts from the identity vector with no seed applied, and previews nothing', () => {
    const { session, previews } = harness(null);
    expect(session.getState().params).toEqual(IDENTITY);
    expect(session.getState().seed).toBe(encodeSeed(IDENTITY));
    expect(previews).toEqual([]);
  });
});

describe('a control move', () => {
  it('sets the state at once and previews the candidate seed on the frame, writing nothing', () => {
    const { session, storage, previews, runFrame } = harness(STORED_SEED, stored(STORED_SEED));
    const moved = { ...STORED, hue: 90 };
    session.move(moved);
    expect(session.getState().params).toEqual(moved);
    expect(session.getState().seed).toBe(encodeSeed(moved));
    expect(previews).toEqual([]);
    runFrame();
    expect(previews).toEqual([encodeSeed(moved)]);
    expect(storage.get(PALETTE_STORAGE_KEY)).toBe(STORED_SEED);
    expect(storage.get(LAST_PALETTE_STORAGE_KEY)).toBe(STORED_SEED);
  });

  it('coalesces moves inside one frame into one preview of the last of them', () => {
    const { session, previews, frames, runFrame } = harness(null);
    session.move({ ...IDENTITY, hue: 10 });
    session.move({ ...IDENTITY, hue: 20 });
    session.move({ ...IDENTITY, hue: 30 });
    expect(frames).toHaveLength(1);
    runFrame();
    expect(previews).toEqual([encodeSeed({ ...IDENTITY, hue: 30 })]);
    session.move({ ...IDENTITY, hue: 40 });
    runFrame();
    expect(previews).toHaveLength(2);
  });

  it('notifies a subscriber on every change, and not after it unsubscribes', () => {
    const { session } = harness(null);
    const heard = vi.fn();
    const off = session.subscribe(heard);
    session.move({ ...IDENTITY, hue: 10 });
    expect(heard).toHaveBeenCalledTimes(1);
    off();
    session.move({ ...IDENTITY, hue: 20 });
    expect(heard).toHaveBeenCalledTimes(1);
  });
});

describe('Revert', () => {
  it('previews the snapshot at once, resets the controls and drops the pending frame', () => {
    const { session, previews, runFrame } = harness(STORED_SEED, stored(STORED_SEED));
    session.move({ ...STORED, hue: 90 });
    runFrame();
    session.move({ ...STORED, hue: 120 });
    session.revert();
    expect(previews.at(-1)).toBe(STORED_SEED);
    expect(session.getState().params).toEqual(held(STORED));
    expect(session.getState().seed).toBe(STORED_SEED);
    const seen = previews.length;
    runFrame();
    expect(previews).toHaveLength(seen);
  });

  it('previews the house palette, null, when nothing was applied', () => {
    const { session, previews, runFrame } = harness(null);
    session.move({ ...IDENTITY, hue: 90 });
    runFrame();
    session.revert();
    expect(previews.at(-1)).toBeNull();
    expect(session.getState().params).toEqual(IDENTITY);
  });

  it('takes a refusal down with it', () => {
    const { session } = harness(null);
    session.load('not a seed');
    expect(session.getState().refusal).not.toBeNull();
    session.revert();
    expect(session.getState().refusal).toBeNull();
  });
});

describe('Apply', () => {
  it('writes the candidate seed once, and is the only thing that writes', () => {
    const { session, storage, runFrame } = harness(null);
    const moved = { ...IDENTITY, hue: 90, tint: 0.02 };
    session.move(moved);
    runFrame();
    expect(storage.has(PALETTE_STORAGE_KEY)).toBe(false);
    const write = vi.spyOn(window.localStorage, 'setItem');
    session.apply();
    expect(storage.get(PALETTE_STORAGE_KEY)).toBe(encodeSeed(moved));
    expect(storage.get(LAST_PALETTE_STORAGE_KEY)).toBe(encodeSeed(moved));
    expect(write.mock.calls.filter(([key]) => key === PALETTE_STORAGE_KEY)).toHaveLength(1);
  });

  it('drops a pending frame, so no preview follows the write', () => {
    const { session, previews, runFrame } = harness(null);
    session.move({ ...IDENTITY, hue: 90 });
    session.apply();
    runFrame();
    expect(previews).toEqual([]);
  });
});

describe('closing without Apply', () => {
  it('leaves storage as it was found, with a seed applied', () => {
    const { session, storage, runFrame } = harness(STORED_SEED, stored(STORED_SEED));
    const before = new Map(storage);
    session.move({ ...STORED, hue: 90 });
    runFrame();
    session.roll(mulberry32(1));
    runFrame();
    session.revert();
    expect(storage).toEqual(before);
  });

  it('leaves storage as it was found, with none applied', () => {
    const { session, storage, runFrame } = harness(null);
    session.move({ ...IDENTITY, hue: 90 });
    runFrame();
    session.revert();
    expect(storage.size).toBe(0);
  });

  it('puts the snapshot back if the session is disposed with a candidate showing', () => {
    const { session, previews, runFrame } = harness(STORED_SEED, stored(STORED_SEED));
    session.move({ ...STORED, hue: 90 });
    runFrame();
    session.dispose();
    expect(previews.at(-1)).toBe(STORED_SEED);
    const seen = previews.length;
    session.dispose();
    expect(previews).toHaveLength(seen);
  });

  it('shows nothing on dispose when nothing was ever previewed', () => {
    const { session, previews } = harness(null);
    session.move({ ...IDENTITY, hue: 90 });
    session.dispose();
    expect(previews).toEqual([]);
  });

  it('shows nothing on dispose after Apply', () => {
    const { session, previews, runFrame } = harness(null);
    session.move({ ...IDENTITY, hue: 90 });
    runFrame();
    session.apply();
    const seen = previews.length;
    session.dispose();
    expect(previews).toHaveLength(seen);
  });
});

describe('Roll', () => {
  it('draws from the roll windows and previews what a seed holds of it', () => {
    const { session, previews, runFrame } = harness(null);
    session.roll(mulberry32(2275));
    const drawn = held(rollParams(mulberry32(2275)));
    expect(session.getState().params).toEqual(drawn);
    expect(session.getState().seed).toBe(encodeSeed(drawn));
    expect(previews).toEqual([]);
    runFrame();
    expect(previews).toEqual([encodeSeed(drawn)]);
  });

  it('rolls a vector inside every full range', () => {
    const { session } = harness(null);
    const random = mulberry32(2276);
    for (let at = 0; at < 20; at++) {
      session.roll(random);
      for (const [key, { min, max }] of Object.entries(PARAMETERS)) {
        const value = session.getState().params[key as keyof PaletteParams];
        expect(value).toBeGreaterThanOrEqual(min);
        expect(value).toBeLessThanOrEqual(max);
      }
    }
  });
});

describe('a seed committed in the field', () => {
  it('loads its vector and previews it at once', () => {
    const { session, previews } = harness(null);
    session.load(`  ${STORED_SEED}  `);
    expect(session.getState().params).toEqual(held(STORED));
    expect(session.getState().seed).toBe(STORED_SEED);
    expect(session.getState().refusal).toBeNull();
    expect(previews).toEqual([STORED_SEED]);
  });

  it('refuses a seed the engine refuses, in the engine’s words, and changes nothing else', () => {
    const { session, storage, previews, runFrame } = harness(STORED_SEED, stored(STORED_SEED));
    session.move({ ...STORED, hue: 90 });
    runFrame();
    const before = session.getState();
    const seen = previews.length;
    const refused = decodeSeed('hx9-nonsense');
    if (refused.ok) throw new Error('the fixture should be refused');
    session.load('hx9-nonsense');
    const after = session.getState();
    expect(after.refusal).toBe(refused.message);
    expect(after.params).toBe(before.params);
    expect(after.seed).toBe(before.seed);
    expect(previews).toHaveLength(seen);
    expect(storage.get(PALETTE_STORAGE_KEY)).toBe(STORED_SEED);
  });

  it('clears the refusal when asked, and on the next good move', () => {
    const { session } = harness(null);
    session.load('nope');
    expect(session.getState().refusal).not.toBeNull();
    session.clearRefusal();
    expect(session.getState().refusal).toBeNull();
    session.load('nope');
    session.move({ ...IDENTITY, hue: 10 });
    expect(session.getState().refusal).toBeNull();
  });
});

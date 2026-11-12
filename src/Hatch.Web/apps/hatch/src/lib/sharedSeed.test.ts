/* A theme seed shared as a link: the parameter is read, applied (or refused)
   and stripped. Plain Node, with `window` stubbed as randomPalette.test.ts
   stubs it - storePalette reaches the global `window.localStorage` directly,
   so the stub needs that too, not only the `location`/`history` this file's
   own functions read. */
import { applySharedSeed, readLastSeed, readPalette, seedFromHref, shareLink } from '@hatch/ui';
import { decodeSeed } from '@hatch/ui/palette';
import { afterEach, describe, expect, it, vi } from 'vitest';

/** A seed matching the engine's format, with no particular colour meant. */
function rollSeed(): string {
  return `hx1-${'A'.repeat(8)}`;
}

function stubWindow(href: string, throwOnReplace = false) {
  const items = new Map<string, string>();
  const state = { key: 'router-state' };
  const replaceState = vi.fn((_state: unknown, _title: string, url: string) => {
    if (throwOnReplace) throw new Error('blocked');
    href = new URL(url, href).toString();
  });
  const win = {
    location: { get href() { return href; } },
    history: { state, replaceState },
    localStorage: {
      getItem: (key: string) => items.get(key) ?? null,
      setItem: (key: string, value: string) => items.set(key, value),
      removeItem: (key: string) => items.delete(key),
    },
    matchMedia: () => ({ matches: false }),
  };
  vi.stubGlobal('window', win);
  return { win, items, replaceState };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('seedFromHref', () => {
  it('reads the seed and cleans it off, keeping other params and the hash', () => {
    const { seed, cleaned } = seedFromHref('https://hatch.example/board?foo=1&palette=hx1-AAAAAAAA&bar=2#panel');
    expect(seed).toBe('hx1-AAAAAAAA');
    expect(cleaned).toBe('/board?foo=1&bar=2#panel');
  });

  it('removes every repeat of the parameter', () => {
    const { seed, cleaned } = seedFromHref('https://hatch.example/?palette=first&palette=second');
    expect(seed).toBe('first');
    expect(cleaned).toBe('/');
  });

  it('leaves a URL with no palette param untouched', () => {
    const { seed, cleaned } = seedFromHref('https://hatch.example/board?foo=1#panel');
    expect(seed).toBeNull();
    expect(cleaned).toBe('/board?foo=1#panel');
  });

  it('leaves no dangling "?" once the only param is removed', () => {
    const { cleaned } = seedFromHref('https://hatch.example/board?palette=hx1-AAAAAAAA');
    expect(cleaned).toBe('/board');
  });
});

describe('applySharedSeed', () => {
  it('stores a good seed as both the applied and the last, and cleans the address', () => {
    const seed = rollSeed();
    const { win, items, replaceState } = stubWindow(`https://hatch.example/board?palette=${seed}`);
    applySharedSeed(win as unknown as Window);
    expect(decodeSeed(seed).ok).toBe(true);
    expect(items.get('hatch.palette')).toBe(seed);
    expect(items.get('hatch.palette.last')).toBe(seed);
    expect(readPalette()).toBe(seed);
    expect(readLastSeed()).toBe(seed);
    expect(replaceState).toHaveBeenCalledTimes(1);
    expect(replaceState).toHaveBeenCalledWith(win.history.state, '', '/board');
  });

  it('stores nothing and warns for a seed the engine refuses, but still cleans the address', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const { win, items, replaceState } = stubWindow('https://hatch.example/board?palette=hx1-nope');
    applySharedSeed(win as unknown as Window);
    expect(items.has('hatch.palette')).toBe(false);
    expect(items.has('hatch.palette.last')).toBe(false);
    expect(warn).toHaveBeenCalledTimes(1);
    expect(warn.mock.calls[0][0]).toContain('Ignoring ?palette=');
    expect(replaceState).toHaveBeenCalledTimes(1);
    warn.mockRestore();
  });

  it('does nothing, including no replaceState, with no palette param', () => {
    const { win, items, replaceState } = stubWindow('https://hatch.example/board');
    applySharedSeed(win as unknown as Window);
    expect(items.size).toBe(0);
    expect(replaceState).not.toHaveBeenCalled();
  });

  it('survives replaceState throwing, as in a sandboxed frame', () => {
    const seed = rollSeed();
    const { win, items } = stubWindow(`https://hatch.example/board?palette=${seed}`, true);
    expect(() => applySharedSeed(win as unknown as Window)).not.toThrow();
    expect(items.get('hatch.palette')).toBe(seed);
  });
});

describe('shareLink', () => {
  it('round-trips through seedFromHref', () => {
    const seed = rollSeed();
    const link = shareLink(seed, 'https://hatch.example/board?foo=1#panel');
    expect(seedFromHref(link).seed).toBe(seed);
    expect(link).toContain('foo=1');
    expect(link).toContain('#panel');
  });

  it('replaces an existing palette param rather than adding a second one', () => {
    const seed = rollSeed();
    const link = shareLink(seed, 'https://hatch.example/board?palette=hx1-OLDOLDOL');
    expect([...new URL(link).searchParams.getAll('palette')]).toEqual([seed]);
  });
});

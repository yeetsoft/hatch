/* The stored palette and the generated stylesheet: the sheet read back as the
   token file's own blocks, the storage round trip, and the DOM half against a
   stubbed document. Plain Node, as themeColor.test.ts is. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import {
  colourTokens,
  decodeSeed,
  derivePalette,
  encodeSeed,
  IDENTITY,
  makeSeedCodec,
  PARAMETER_KEYS,
  PARAMETERS,
  parseTokenBlocks,
  paletteCss,
  rollParams,
  type PaletteParams,
} from '@hatch/ui/palette';
import { applyChoice, applyPalette, readPalette, themeColorFor, writePalette } from '@hatch/ui';
import { afterEach, describe, expect, it, vi } from 'vitest';

const blocks = parseTokenBlocks(
  readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8'),
);

function mulberry32(seed: number): () => number {
  let state = seed;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const corner = (pick: 'min' | 'max'): PaletteParams => {
  const params = { ...IDENTITY };
  for (const key of PARAMETER_KEYS) params[key] = PARAMETERS[key][pick];
  return params;
};

const random = mulberry32(2258);
const VECTORS: PaletteParams[] = [
  IDENTITY,
  ...Array.from({ length: 12 }, () => rollParams(random)),
  corner('min'),
  corner('max'),
];
const seedOf = (params: PaletteParams) => encodeSeed(params);
/** What a stored seed derives, which is what a page gets: the seed quantises the vector. */
const paramsOf = (params: PaletteParams): PaletteParams => {
  const decoded = decodeSeed(seedOf(params));
  if (!decoded.ok) throw new Error(decoded.message);
  return decoded.params;
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('paletteCss', () => {
  it.each(VECTORS.map((params, at) => [at, params] as const))(
    'reads back as the token file blocks (vector %i)',
    (_, params) => {
      const css = parseTokenBlocks(paletteCss(params, blocks));
      expect(css.dark).toEqual(css.darkMedia);
      for (const token of Object.keys(css.dark)) expect(css.light).toHaveProperty(token);
      const light = derivePalette(params, blocks.light);
      const dark = derivePalette(params, blocks.dark);
      expect(css.light).toEqual(Object.fromEntries(colourTokens(blocks.light).map((t) => [t, light[t]])));
      expect(css.dark).toEqual(Object.fromEntries(colourTokens(blocks.dark).map((t) => [t, dark[t]])));
    },
  );

  it('restates colour tokens only', () => {
    const css = parseTokenBlocks(paletteCss(rollParams(random), blocks));
    expect(Object.keys(css.light)).toEqual(colourTokens(css.light));
    expect(Object.keys(css.dark)).toEqual(colourTokens(css.dark));
  });
});

/** A localStorage stub; `fail` makes every call throw, as a locked-down context does. */
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

describe('readPalette and writePalette', () => {
  it('round-trips a seed', () => {
    const items = stubStorage();
    const seed = seedOf(rollParams(random));
    writePalette(seed);
    expect(items.get('hatch.palette')).toBe(seed);
    expect(readPalette()).toBe(seed);
  });

  it('reads a malformed value, an older-engine seed and a missing key as null', () => {
    const items = stubStorage();
    expect(readPalette()).toBeNull();
    items.set('hatch.palette', 'not a seed');
    expect(readPalette()).toBeNull();
    items.set('hatch.palette', makeSeedCodec(0).encodeSeed(IDENTITY));
    expect(readPalette()).toBeNull();
  });

  it('forgets the seed for null', () => {
    const items = stubStorage();
    writePalette(seedOf(IDENTITY));
    writePalette(null);
    expect(items.has('hatch.palette')).toBe(false);
  });

  it('survives a store that throws', () => {
    stubStorage(true);
    expect(readPalette()).toBeNull();
    expect(() => writePalette(seedOf(IDENTITY))).not.toThrow();
    expect(() => writePalette(null)).not.toThrow();
  });
});

interface El {
  tag: string;
  attrs: Record<string, string>;
  textContent: string;
  setAttribute(name: string, value: string): void;
  remove(): void;
}

/** A document whose <head> holds <style> and <meta> elements and answers the selectors the store uses. */
function stubDocument(dataTheme: string | null = null, dark = false) {
  const children: El[] = [];
  const make = (tag: string): El => ({
    tag,
    attrs: {},
    textContent: '',
    setAttribute(name, value) {
      this.attrs[name] = value;
    },
    remove() {
      children.splice(children.indexOf(this), 1);
    },
  });
  const root = {
    attr: dataTheme,
    getAttribute() {
      return this.attr;
    },
    setAttribute(_: string, value: string) {
      this.attr = value;
    },
    removeAttribute() {
      this.attr = null;
    },
  };
  const attrOf = (selector: string) => /\[([^\]]+)\]/.exec(selector)?.[1] ?? '';
  const found = (selector: string) =>
    children.find((el) => el.tag === selector.split('[')[0] && attrOf(selector) in el.attrs) ?? null;
  vi.stubGlobal('document', {
    documentElement: root,
    head: {
      querySelector: found,
      appendChild: (el: El) => children.push(el),
      prepend: (el: El) => children.unshift(el),
    },
    createElement: make,
  });
  vi.stubGlobal('window', { matchMedia: () => ({ matches: dark }) });
  return {
    sheets: () => children.filter((el) => el.tag === 'style'),
    meta: () => children.find((el) => el.tag === 'meta') ?? null,
  };
}

describe('applyPalette', () => {
  it('does not throw where document is absent', () => {
    expect(() => applyPalette(seedOf(IDENTITY))).not.toThrow();
    expect(() => applyPalette(null)).not.toThrow();
  });

  it('writes one owned stylesheet and updates it in place', () => {
    const { sheets } = stubDocument();
    const first = rollParams(random);
    const second = rollParams(random);
    applyPalette(seedOf(first));
    expect(sheets()).toHaveLength(1);
    expect(sheets()[0].attrs).toHaveProperty('data-theme-palette');
    expect(sheets()[0].textContent).toBe(paletteCss(paramsOf(first), blocks));

    applyPalette(seedOf(second));
    expect(sheets()).toHaveLength(1);
    expect(sheets()[0].textContent).toBe(paletteCss(paramsOf(second), blocks));
    applyPalette(null);
  });

  it('removes the stylesheet for null and for a refused seed', () => {
    const { sheets } = stubDocument();
    applyPalette(seedOf(IDENTITY));
    applyPalette(null);
    expect(sheets()).toHaveLength(0);
    applyPalette(seedOf(IDENTITY));
    applyPalette('hx0-nonsense');
    expect(sheets()).toHaveLength(0);
  });
});

describe('the status bar with a palette on', () => {
  const params = rollParams(mulberry32(7));
  const derived = {
    light: derivePalette(paramsOf(params), blocks.light)['--chrome'],
    dark: derivePalette(paramsOf(params), blocks.dark)['--chrome'],
  };

  it('themeColorFor reads the derived chrome, and the house one again once it is off', () => {
    stubDocument();
    const house = { light: themeColorFor('light'), dark: themeColorFor('dark') };
    applyPalette(seedOf(params));
    expect(themeColorFor('light')).toBe(derived.light);
    expect(themeColorFor('dark')).toBe(derived.dark);
    applyPalette(null);
    expect(themeColorFor('light')).toBe(house.light);
    expect(themeColorFor('dark')).toBe(house.dark);
  });

  it('an explicit choice writes the derived chrome', () => {
    const { meta } = stubDocument();
    applyPalette(seedOf(params));
    applyChoice('light');
    expect(meta()?.attrs['content']).toBe(derived.light);
    applyChoice('dark');
    expect(meta()?.attrs['content']).toBe(derived.dark);
    applyPalette(null);
  });

  it('auto writes the override for the OS preference, and none without a palette', () => {
    for (const dark of [false, true]) {
      const { meta } = stubDocument(null, dark);
      applyPalette(seedOf(params));
      applyChoice('auto');
      expect(meta()?.attrs['content']).toBe(dark ? derived.dark : derived.light);
      applyPalette(null);
      expect(meta()).toBeNull();
    }
  });

  it('applying a palette keeps the choice already on <html>', () => {
    const { meta } = stubDocument('dark');
    applyPalette(seedOf(params));
    expect(meta()?.attrs['content']).toBe(derived.dark);
    applyPalette(null);
  });
});

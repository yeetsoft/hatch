/* The palette engine, end to end: one block of tokens.css and a parameter
   vector in, a colour map out, every contrast pair still clear. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import {
  colourTokens,
  derivePalette,
  hexToOklch,
  IDENTITY,
  measurePairs,
  PARAMETER_KEYS,
  PARAMETERS,
  parseCssColor,
  parseTokenBlocks,
  rollParams,
  ROLES,
  semanticHueGap,
  windowBands,
  type PaletteParams,
  type TokenBlock,
} from '@hatch/ui/palette';
import { describe, expect, it } from 'vitest';

function mulberry32(seed: number): () => number {
  let state = seed;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const { light, dark } = parseTokenBlocks(
  readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8'),
);
const BLOCKS: [string, TokenBlock][] = [['light', light], ['dark', dark]];

/** Every vector the engine is held to: seeded draws across the full ranges, then all 64 corners of the range box. */
const VECTORS: PaletteParams[] = (() => {
  const random = mulberry32(20_256);
  const draws = Array.from({ length: 1000 }, () => {
    const params = { ...IDENTITY };
    for (const key of PARAMETER_KEYS) {
      const { min, max } = PARAMETERS[key];
      params[key] = min + random() * (max - min);
    }
    return params;
  });
  const corners = Array.from({ length: 1 << PARAMETER_KEYS.length }, (_, mask) => {
    const params = { ...IDENTITY };
    PARAMETER_KEYS.forEach((key, at) => {
      params[key] = mask & (1 << at) ? PARAMETERS[key].max : PARAMETERS[key].min;
    });
    return params;
  });
  return [...draws, ...corners];
})();

const NEUTRALS = Object.keys(ROLES).filter((token) => ROLES[token] === 'neutral');
const FIXED = Object.keys(ROLES).filter((token) => ROLES[token] === 'fixed');
const side = (l: number) => l >= 0.5;
const stepsApart = (a: string, b: string) => {
  const [x, y] = [parseCssColor(a)!, parseCssColor(b)!];
  return Math.max(Math.abs(x.r - y.r), Math.abs(x.g - y.g), Math.abs(x.b - y.b));
};

describe('derivePalette', () => {
  it('returns every colour token of the block: hex when opaque, rgba at the authored alpha for washes, fixed as authored', () => {
    for (const [name, block] of BLOCKS) {
      const out = derivePalette(rollParams(mulberry32(7)), block);
      expect(Object.keys(out), name).toEqual(colourTokens(block));
      for (const token of colourTokens(block)) {
        const authored = block[token];
        if (ROLES[token] === 'fixed') {
          expect(out[token], `${name} ${token}`).toBe(authored);
        } else if (parseCssColor(authored)!.a < 1) {
          expect(out[token], `${name} ${token}`).toMatch(/^rgba\(\d+, \d+, \d+, [\d.]+\)$/);
          expect(parseCssColor(out[token])!.a, `${name} ${token}`).toBe(parseCssColor(authored)!.a);
        } else {
          expect(out[token], `${name} ${token}`).toMatch(/^#[0-9a-f]{6}$/);
        }
      }
    }
  });

  it('gives the same bytes for the same params and block', () => {
    for (const [, block] of BLOCKS) {
      for (const params of VECTORS.slice(0, 50)) {
        expect(JSON.stringify(derivePalette({ ...params }, block))).toBe(JSON.stringify(derivePalette({ ...params }, block)));
      }
    }
  });

  it('leaves the neutrals and the fixed tokens as authored at the identity vector', () => {
    for (const [name, block] of BLOCKS) {
      const out = derivePalette(IDENTITY, block);
      for (const token of NEUTRALS) {
        expect(stepsApart(out[token], block[token]), `${name} ${token}: ${out[token]} for ${block[token]}`).toBeLessThanOrEqual(1);
      }
      for (const token of FIXED.filter((t) => t in block)) expect(out[token], `${name} ${token}`).toBe(block[token]);
    }
  });

  it('pins one value per block, so retuning the guard is a visible change', () => {
    const params: PaletteParams = { hue: 150, spread: 1.2, chroma: 1.3, lift: 0.02, tint: 0.02, tintHue: 30 };
    const light_ = derivePalette(params, light);
    const dark_ = derivePalette(params, dark);
    expect([light_['--primary'], light_['--danger'], light_['--bg'], light_['--primary-bg']]).toEqual(PINNED.light);
    expect([dark_['--primary'], dark_['--danger'], dark_['--bg'], dark_['--primary-bg']]).toEqual(PINNED.dark);
  });

  describe('over 1,000 seeded vectors and the 64 corners of the range box', () => {
    it('keeps the neutrals within 0.04 of authored lightness, at chroma tint and the rotated primary hue plus tintHue', () => {
      for (const [name, block] of BLOCKS) {
        const primaryHue = hexToOklch(block['--primary']).h;
        for (const params of VECTORS) {
          const out = derivePalette(params, block);
          for (const token of NEUTRALS) {
            const was = hexToOklch(block[token]);
            const now = hexToOklch(out[token]);
            expect(Math.abs(now.l - was.l), `${name} ${token} for ${JSON.stringify(params)}`).toBeLessThanOrEqual(0.04);
          }
          // Chroma and hue are what the paper is set to, so read them off the one neutral the guard never touches.
          const card = hexToOklch(out['--line-strong']);
          expect(card.c, `${name} ${JSON.stringify(params)}`).toBeLessThanOrEqual(params.tint + 0.004);
          if (params.tint >= 0.02 && card.l < 0.95 && card.l > 0.1) {
            const want = (primaryHue + params.hue + params.tintHue) % 360;
            expect(Math.abs(((card.h - want + 540) % 360) - 180), `${name} paper hue for ${JSON.stringify(params)}`).toBeLessThan(6);
          }
        }
      }
    }, 30_000);

    it('never carries a non-fixed token across the midpoint from the side it was authored on', () => {
      for (const [name, block] of BLOCKS) {
        for (const params of VECTORS) {
          const out = derivePalette(params, block);
          for (const token of colourTokens(block).filter((t) => ROLES[t] !== 'fixed')) {
            const was = parseCssColor(block[token])!;
            const now = parseCssColor(out[token])!;
            const lightness = (c: { r: number; g: number; b: number }) =>
              hexToOklch(`#${[c.r, c.g, c.b].map((v) => v.toString(16).padStart(2, '0')).join('')}`).l;
            expect(side(lightness(now)), `${name} ${token} for ${JSON.stringify(params)}`).toBe(side(lightness(was)));
          }
        }
      }
    }, 30_000);

    it('keeps danger, success and warn at least 50 degrees apart in hue, measured on the output hex', () => {
      for (const [name, block] of BLOCKS) {
        for (const params of VECTORS) {
          const gap = semanticHueGap(derivePalette(params, block));
          expect(gap, `${name} for ${JSON.stringify(params)}`).toBeGreaterThanOrEqual(50);
        }
      }
    }, 30_000);

    it('clears the floor of every contrast pair', () => {
      for (const [name, block] of BLOCKS) {
        for (const params of VECTORS) {
          for (const pair of measurePairs(derivePalette(params, block))) {
            expect(
              pair.ratio,
              `${name}: ${pair.fg} on ${pair.bg}${pair.over ? ` over ${pair.over}` : ''} for ${JSON.stringify(params)}`,
            ).toBeGreaterThanOrEqual(pair.floor);
          }
        }
      }
    }, 30_000);
  });
});

describe('PARAMETERS and rollParams', () => {
  it('draws inside the roll windows, every window inside its full range', () => {
    const random = mulberry32(99);
    for (let at = 0; at < 500; at += 1) {
      const params = rollParams(random);
      for (const key of PARAMETER_KEYS) {
        const { min, max, window } = PARAMETERS[key];
        const value = params[key];
        expect(value, key).toBeGreaterThanOrEqual(min);
        expect(value, key).toBeLessThanOrEqual(max);
        if ('pick' in window) {
          const nearest = Math.min(
            ...window.pick.map((pick) => {
              const apart = Math.abs(value - pick) % 360;
              return Math.min(apart, 360 - apart);
            }),
          );
          expect(nearest, key).toBeLessThanOrEqual(window.jitter);
        } else {
          expect(value, key).toBeGreaterThanOrEqual(window.lo);
          expect(value, key).toBeLessThanOrEqual(window.hi);
          expect(window.lo, key).toBeGreaterThanOrEqual(min);
          expect(window.hi, key).toBeLessThanOrEqual(max);
        }
      }
    }
  });

  it('calls random exactly seven times', () => {
    let calls = 0;
    rollParams(() => {
      calls += 1;
      return 0.5;
    });
    expect(calls).toBe(7);
  });

  it('rolls the same vector from the same stream', () => {
    expect(rollParams(mulberry32(5))).toEqual(rollParams(mulberry32(5)));
  });

  it('bands a plain window as one segment', () => {
    expect(windowBands(PARAMETERS.spread)).toEqual([[0.8, 1.25]]);
  });

  it('bands the whole range for an unrestricted parameter', () => {
    expect(windowBands(PARAMETERS.hue)).toEqual([[0, 360]]);
  });

  it('splits a picked window into five segments, two of them wrapped', () => {
    const bands = windowBands(PARAMETERS.tintHue);
    expect(bands).toHaveLength(5);
    expect(bands).toEqual(
      expect.arrayContaining([
        [0, 12],
        [348, 360],
        [18, 42],
        [168, 192],
        [198, 222],
      ]),
    );
  });
});

/* primary, danger, bg and the primary wash, for the vector in the pinning test. Retuning the guard or the
   windows legitimately moves these: update them in the same change, having looked at why. */
const PINNED = {
  light: ['#b44b00', '#00847f', '#fdf4e7', 'rgba(180, 75, 0, 0.08)'],
  dark: ['#ff926d', '#00bbe1', '#201910', 'rgba(255, 146, 109, 0.12)'],
};

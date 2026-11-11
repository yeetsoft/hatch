/* The theme seed: a parameter vector written as a string and read back exactly,
   the version a seed carries refused when it is not this engine's. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import {
  decodeSeed,
  derivePalette,
  encodeSeed,
  IDENTITY,
  makeSeedCodec,
  PARAMETER_KEYS,
  PARAMETERS,
  parseTokenBlocks,
  rollParams,
  SEED_VERSION,
  type PaletteParams,
  type SeedRefusal,
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

/** Seeded draws across the full ranges, all 64 corners of the range box, and a few rolls. */
const VECTORS: PaletteParams[] = (() => {
  const random = mulberry32(20_257);
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
  const rolls = Array.from({ length: 50 }, (_, at) => rollParams(mulberry32(at + 1)));
  return [...draws, ...corners, ...rolls];
})();

/* Each seed written out by hand: bytes are round((value - min) / (max - min) * 255),
   in the order hue, spread, chroma, lift, tint, tintHue, then base64url in sixes of bits. */
const PINNED: { name: string; seed: string; params: PaletteParams }[] = [
  { name: 'every minimum', seed: 'hx1-AAAAAAAA', params: { hue: 0, spread: 0.5, chroma: 0.5, lift: -0.08, tint: 0, tintHue: 0 } },
  { name: 'every maximum', seed: 'hx1-________', params: { hue: 360, spread: 1.5, chroma: 2, lift: 0.08, tint: 0.05, tintHue: 360 } },
  // bytes 0, 128, 85, 128, 0, 0: the middle of spread and lift is 127.5, which rounds up.
  {
    name: 'identity',
    seed: 'hx1-AIBVgAAA',
    params: { hue: 0, spread: 0.5 + 128 / 255, chroma: 0.5 + (85 / 255) * 1.5, lift: -0.08 + (128 / 255) * 0.16, tint: 0, tintHue: 0 },
  },
  // bytes 51, 102, 153, 204, 51, 102: the fifths of every range.
  { name: 'mid-range', seed: 'hx1-M2aZzDNm', params: { hue: 72, spread: 0.9, chroma: 1.4, lift: 0.048, tint: 0.01, tintHue: 144 } },
  // bytes 255, 0, 255, 0, 255, 0: alternating ends.
  { name: 'alternating', seed: 'hx1-_wD_AP8A', params: { hue: 360, spread: 0.5, chroma: 2, lift: -0.08, tint: 0.05, tintHue: 0 } },
];

function expectParams(actual: PaletteParams, expected: PaletteParams): void {
  for (const key of PARAMETER_KEYS) expect(actual[key], key).toBeCloseTo(expected[key], 9);
}

function decoded(seed: string): PaletteParams {
  const result = decodeSeed(seed);
  if (!result.ok) throw new Error(`${seed} refused: ${result.message}`);
  return result.params;
}

function refusal(seed: string): SeedRefusal | 'ok' {
  const result = decodeSeed(seed);
  return result.ok ? 'ok' : result.refusal;
}

describe('encodeSeed', () => {
  it('writes the version and eight base64url characters', () => {
    expect(encodeSeed(IDENTITY)).toMatch(/^hx1-[A-Za-z0-9_-]{8}$/);
  });

  it('clamps a value outside its range to the end of it', () => {
    expect(encodeSeed({ ...IDENTITY, hue: 1000, lift: -5 })).toBe(encodeSeed({ ...IDENTITY, hue: 360, lift: -0.08 }));
  });

  it('throws on a value that is not a number', () => {
    expect(() => encodeSeed({ ...IDENTITY, chroma: NaN })).toThrow(RangeError);
    expect(() => encodeSeed({ ...IDENTITY, tint: Infinity })).toThrow(RangeError);
  });
});

describe('pinned seeds', () => {
  for (const { name, seed, params } of PINNED) {
    it(`${name}: decodes to its vector and encodes back`, () => {
      expectParams(decoded(seed), params);
      expect(encodeSeed(params)).toBe(seed);
      expect(encodeSeed(decoded(seed))).toBe(seed);
    });
  }
});

describe('round trip', () => {
  it('re-encodes to the same seed, within half a step of the original', () => {
    for (const params of VECTORS) {
      const seed = encodeSeed(params);
      const back = decoded(seed);
      expect(encodeSeed(back)).toBe(seed);
      for (const key of PARAMETER_KEYS) {
        const { min, max } = PARAMETERS[key];
        expect(Math.abs(back[key] - params[key])).toBeLessThanOrEqual((max - min) / 510 + 1e-12);
      }
    }
  });

  it('derives the same palette in both modes from a seed read twice', () => {
    for (const params of VECTORS.slice(0, 40)) {
      const seed = encodeSeed(params);
      for (const block of [light, dark]) {
        expect(derivePalette(decoded(seed), block)).toEqual(derivePalette(decoded(seed), block));
      }
    }
  });
});

describe('decodeSeed', () => {
  const seed = 'hx1-M2aZzDNm';

  it('forgives whitespace around the seed and nothing else', () => {
    expectParams(decoded(`  ${seed}\n`), decoded(seed));
    const bad = [
      'hx1-M2aZ zDNm',
      'hx1-M2aZ\tzDNm',
      'HX1-M2aZzDNm',
      'hx1_M2aZzDNm',
      'M2aZzDNm',
      'hx1-M2aZzDN=',
      '',
      '   ',
      'hx1-M2aZzDNé',
      'hx-M2aZzDNm',
    ];
    for (const text of bad) expect(refusal(text), JSON.stringify(text)).toBe('malformed');
  });

  it('refuses a body of the wrong length', () => {
    for (const bad of ['hx1-', 'hx1-AAAAAAA', 'hx1-AAAAAAAAA']) expect(refusal(bad), bad).toBe('length');
  });

  it('refuses another version, naming both', () => {
    for (const bad of ['hx2-AAAAAAAA', 'hx0-AAAAAAAA', 'hx10-AAAAAAAA']) {
      const result = decodeSeed(bad);
      expect(result.ok).toBe(false);
      if (result.ok) continue;
      expect(result.refusal, bad).toBe('version');
      expect(result.message).toContain(`version ${/^hx(\d+)/.exec(bad)![1]}`);
      expect(result.message).toContain(`version ${SEED_VERSION}`);
    }
  });

  it('reports the version before it judges the body', () => {
    expect(refusal('hx2-not a seed!')).toBe('version');
    expect(refusal('hx2-')).toBe('version');
  });

  it('never throws on what a person might paste', () => {
    for (const odd of ['\u0000', 'hx1-\u{1F600}', 'hx99999999999999999999-AAAAAAAA', '-', 'hx-']) {
      expect(() => decodeSeed(odd)).not.toThrow();
    }
  });
});

describe('the version', () => {
  const next = makeSeedCodec(SEED_VERSION + 1);

  it('makes every pinned seed refuse, rather than misread, once bumped', () => {
    for (const { seed } of PINNED) {
      const result = next.decodeSeed(seed);
      expect(result.ok, seed).toBe(false);
      if (!result.ok) expect(result.refusal).toBe('version');
    }
  });

  it('writes the bumped prefix, which this engine then refuses', () => {
    for (const { seed, params } of PINNED) {
      const bumped = next.encodeSeed(params);
      expect(bumped).toBe(seed.replace(`hx${SEED_VERSION}-`, `hx${SEED_VERSION + 1}-`));
      const back = next.decodeSeed(bumped);
      expect(back.ok).toBe(true);
      if (back.ok) expectParams(back.params, decoded(seed));
      expect(refusal(bumped)).toBe('version');
    }
  });
});

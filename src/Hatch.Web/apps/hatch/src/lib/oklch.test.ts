/* The colour arithmetic under the palette engine. Lives in apps/hatch for the
   reason tokens.test.ts says: packages/ui has no runner of its own. */
import { clipChroma, formatRgba, hexToOklch, oklchToHex, parseCssColor } from '@hatch/ui/palette';
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

const hex = (r: number, g: number, b: number) => `#${[r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('')}`;
const channelsOf = (color: string) => [1, 3, 5].map((at) => parseInt(color.slice(at, at + 2), 16));

function expectRoundTrip(color: string) {
  const { l, c, h } = hexToOklch(color);
  const back = channelsOf(oklchToHex(l, c, h));
  channelsOf(color).forEach((channel, at) => {
    expect(Math.abs(back[at] - channel), `${color} came back ${oklchToHex(l, c, h)}`).toBeLessThanOrEqual(1);
  });
}

describe('hexToOklch and oklchToHex', () => {
  it('round-trips a 17-step grid of the sRGB cube', () => {
    const steps = Array.from({ length: 17 }, (_, at) => Math.min(255, at * 16));
    for (const r of steps) for (const g of steps) for (const b of steps) expectRoundTrip(hex(r, g, b));
  }, 30_000);

  it('round-trips 2,000 seeded colours', () => {
    const random = mulberry32(2256);
    for (let at = 0; at < 2000; at += 1) {
      expectRoundTrip(hex(Math.floor(random() * 256), Math.floor(random() * 256), Math.floor(random() * 256)));
    }
  }, 30_000);

  it('puts white at lightness 1 and black at 0, both without chroma', () => {
    expect(hexToOklch('#ffffff').l).toBeCloseTo(1, 4);
    expect(hexToOklch('#000000').l).toBeCloseTo(0, 6);
    expect(hexToOklch('#808080').c).toBeLessThan(1e-4);
  });

  it('keeps lightness and hue, and gives up chroma, for a colour out of gamut', () => {
    for (const [l, c, h] of [[0.6, 0.4, 145], [0.5, 0.35, 265], [0.8, 0.3, 30], [0.4, 0.3, 320], [0.7, 0.5, 200]]) {
      const kept = clipChroma(l, c, h);
      expect(kept, `chroma ${c} at l ${l}, h ${h}`).toBeLessThan(c);
      const back = hexToOklch(oklchToHex(l, c, h));
      expect(back.l).toBeCloseTo(l, 2);
      expect(back.c).toBeLessThan(c);
      expect(Math.abs(((back.h - h + 540) % 360) - 180)).toBeLessThan(2);
    }
  });

  it('leaves a colour in gamut alone', () => {
    expect(clipChroma(0.6, 0.05, 250)).toBe(0.05);
  });
});

describe('parseCssColor', () => {
  it('reads hex, rgb and rgba, and nothing else', () => {
    expect(parseCssColor('#0066cc')).toEqual({ r: 0, g: 102, b: 204, a: 1 });
    expect(parseCssColor('rgb(1, 2, 3)')).toEqual({ r: 1, g: 2, b: 3, a: 1 });
    expect(parseCssColor('rgba(0, 102, 204, 0.08)')).toEqual({ r: 0, g: 102, b: 204, a: 0.08 });
    expect(parseCssColor('0 2px 8px rgba(0, 0, 0, 0.1)')).toBeNull();
    expect(parseCssColor('red')).toBeNull();
  });

  it('formats rgba the way tokens.css writes it', () => {
    expect(formatRgba(0, 102, 204, 0.08)).toBe('rgba(0, 102, 204, 0.08)');
  });
});

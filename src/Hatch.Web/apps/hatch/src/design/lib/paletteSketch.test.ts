import { measurePairs, PARAMETERS, PARAMETER_KEYS } from '@hatch/ui/palette';
import { describe, expect, it } from 'vitest';
import { ACCENT_TOKENS, deriveBoth, rimMarks, SEED, sketchPalettes } from './paletteSketch';
import { SECTIONS } from '../sections';

describe('the palette sketch fixture', () => {
  it('decodes the fixed seed and derives both blocks', () => {
    const { params, light, dark } = sketchPalettes();
    expect(params.hue).toBeGreaterThanOrEqual(0);
    expect(Object.keys(light).length).toBeGreaterThan(0);
    expect(Object.keys(dark).length).toBeGreaterThan(0);
  });

  it('lies inside every roll window, including the picked windows', () => {
    const { params } = sketchPalettes();
    for (const key of PARAMETER_KEYS) {
      const { window } = PARAMETERS[key];
      const value = params[key];
      if ('lo' in window) {
        expect(value).toBeGreaterThanOrEqual(window.lo);
        expect(value).toBeLessThanOrEqual(window.hi);
      } else {
        const inSomePick = window.pick.some((pick) => {
          const lo = pick - window.jitter;
          const hi = pick + window.jitter;
          const wrapped = value - 360;
          return (value >= lo && value <= hi) || (wrapped >= lo && wrapped <= hi);
        });
        expect(inSomePick).toBe(true);
      }
    }
  });

  it('clears every contrast floor on both derived blocks', () => {
    const { light, dark } = sketchPalettes();
    for (const palette of [light, dark]) {
      for (const pair of measurePairs(palette)) {
        expect(pair.ratio).toBeGreaterThanOrEqual(pair.floor);
      }
    }
  });

  it('derives both blocks from any vector, as the live page does on every move', () => {
    const { params, light, dark } = sketchPalettes();
    expect(deriveBoth(params)).toEqual({ light, dark });
    expect(deriveBoth({ ...params, hue: (params.hue + 90) % 360 }).light['--primary']).not.toBe(light['--primary']);
  });

  it('marks one rim position per accent, each hue in range', () => {
    const { light } = sketchPalettes();
    const marks = rimMarks(light);
    expect(marks.map((mark) => mark.token)).toEqual([...ACCENT_TOKENS]);
    for (const mark of marks) {
      expect(mark.hue).toBeGreaterThanOrEqual(0);
      expect(mark.hue).toBeLessThan(360);
    }
  });

  it('ends Components with the palette configurator', () => {
    const components = SECTIONS.filter((section) => section.group === 'Components');
    expect(components.at(-1)?.slug).toBe('palette-configurator');
  });

  it('keeps the seed this file promises not to change', () => {
    expect(SEED).toBe('hx1-jpl3jz2V');
  });
});

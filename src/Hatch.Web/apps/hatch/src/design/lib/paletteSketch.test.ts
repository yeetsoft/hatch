import { measurePairs, PARAMETERS, PARAMETER_KEYS } from '@hatch/ui/palette';
import { describe, expect, it } from 'vitest';
import { ACCENT_TOKENS, rimMarks, SEED, sketchPalettes, windowBands } from './paletteSketch';
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

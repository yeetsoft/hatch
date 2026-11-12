/* <ParameterControls> lives in @hatch/ui (packages/ui/src), not in this app - the test stays here for the same
   reason PaletteBoard.test.tsx does: packages/ui has no test runner, and this app has no DOM to drive. The markup
   is rendered to a string; what a pointer or a key does is pure arithmetic with its own tests below, and the real
   drag is checked in the driver. */
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import tokensCss from '@hatch/ui/tokens.css?raw';
import { ParameterControls, RIM_TOKENS } from '@hatch/ui';
import {
  derivePalette,
  hexToOklch,
  IDENTITY,
  PARAMETER_KEYS,
  PARAMETERS,
  parseTokenBlocks,
  snapToSeed,
  windowBands,
  type ParameterSpec,
  type PaletteParams,
} from '@hatch/ui/palette';
import { angleFromPoint, keyValue } from '@hatch/ui/dialMath';
import { formatReadout, inWindow, stepOf } from '@hatch/ui/readoutMath';

const blocks = parseTokenBlocks(tokensCss);
const palette = derivePalette(IDENTITY, blocks.light);
const INSIDE: PaletteParams = { hue: 200, spread: 1, chroma: 1.2, lift: 0, tint: 0.012, tintHue: 210 };

const render = (value: PaletteParams, parameters?: Record<string, ParameterSpec>, shown = palette) =>
  renderToStaticMarkup(<ParameterControls value={value} onChange={() => {}} palette={shown} parameters={parameters} />);

const count = (haystack: string, needle: string) => haystack.split(needle).length - 1;

describe('ParameterControls, the default table', () => {
  const html = render(INSIDE);

  it('draws one control per entry, in table order, the hue as the dial and the rest as sliders', () => {
    expect(count(html, 'role="slider"')).toBe(1);
    expect(count(html, 'type="range"')).toBe(PARAMETER_KEYS.length - 1);
    const labels = [...html.matchAll(/hatch-(?:hue-dial|parameter-slider)__label[^>]*>([^<]+)</g)].map((m) => m[1]);
    expect(labels).toEqual(PARAMETER_KEYS.map((key) => PARAMETERS[key].label));
  });

  it('names the group', () => {
    expect(html).toContain('role="group" aria-label="Palette parameters"');
  });

  it('draws the dial as a slider over 0 to 360 with its value in degrees', () => {
    expect(html).toContain('aria-valuemin="0"');
    expect(html).toContain('aria-valuemax="360"');
    expect(html).toContain('aria-valuenow="200"');
    expect(html).toContain('aria-valuetext="200 degrees"');
    expect(html).toMatch(/role="slider"[^>]*aria-labelledby="/);
  });

  it('draws every slider as a labelled native range, a seed step apart', () => {
    for (const key of PARAMETER_KEYS.filter((k) => k !== 'hue')) {
      const { min, max } = PARAMETERS[key];
      expect(html, key).toContain(`min="${min}" max="${max}" step="${(max - min) / 255}"`);
    }
    expect(count(html, '<label for=')).toBe(PARAMETER_KEYS.length - 1);
    for (const [, id] of html.matchAll(/<label for="([^"]+)"/g)) expect(html).toContain(`id="${id}"`);
  });

  it('announces every readout politely', () => {
    expect(count(html, 'aria-live="polite"')).toBe(PARAMETER_KEYS.length);
    expect(count(html, '<output')).toBe(PARAMETER_KEYS.length);
  });

  it('draws one lit band per window segment on the sliders, and none on the dial', () => {
    const bands = PARAMETER_KEYS.filter((k) => k !== 'hue').reduce((sum, k) => sum + windowBands(PARAMETERS[k]).length, 0);
    expect(count(html, 'hatch-parameter-slider__band"')).toBe(bands);
    expect(windowBands(PARAMETERS.tintHue).length).toBeGreaterThan(4);
    expect(windowBands(PARAMETERS.spread)).toHaveLength(1);
    expect(html).not.toMatch(/hatch-hue-dial[^>]*__band/);
  });
});

describe('ParameterControls, bound to the table', () => {
  it('draws one more labelled control for an entry added to a copy of the table, with no other change', () => {
    const gloss: ParameterSpec = { min: 0, max: 1, window: { lo: 0.2, hi: 0.4 }, label: 'Gloss', unit: '%' };
    const table = { ...PARAMETERS, gloss };
    const html = render({ ...INSIDE, gloss: 0.3 } as PaletteParams, table);
    expect(count(html, 'type="range"')).toBe(PARAMETER_KEYS.length);
    expect(html).toContain('>Gloss</label>');
    expect(html).toContain('>0.300%</output>');
    expect(html).toContain(`step="${1 / 255}"`);
  });

  it('falls back to the key as a label and to no unit, and reads a missing value as the minimum', () => {
    const html = render(INSIDE, { sheen: { min: 2, max: 3, window: { lo: 2, hi: 3 } } });
    expect(html).toContain('>sheen</label>');
    expect(html).toContain('>2.000</output>');
  });

  it('draws no dial for a table without hue', () => {
    const { hue: _hue, ...rest } = PARAMETERS;
    const html = render(INSIDE, rest);
    expect(html).not.toContain('role="slider"');
    expect(count(html, 'type="range"')).toBe(PARAMETER_KEYS.length - 1);
  });
});

describe('the readout', () => {
  const spread = PARAMETERS.spread;

  it('shows the value with its unit and glows inside the roll window', () => {
    const html = render({ ...INSIDE, spread: 1.1 });
    expect(html).toContain('>1.100×</output>');
    expect(html).not.toContain('outside the roll');
    expect(html).not.toContain('is-outside');
  });

  it('dims and says so in words outside the window, for that control only', () => {
    const html = render({ ...INSIDE, spread: 0.6 });
    expect(count(html, 'is-outside')).toBe(1);
    expect(count(html, 'outside the roll')).toBe(1);
    expect(html).toMatch(/is-outside" aria-live="polite">0\.600× outside the roll</);
  });

  it('counts the window edges as inside, and a picked window per pick', () => {
    expect(inWindow(spread, 0.8)).toBe(true);
    expect(inWindow(spread, 1.25)).toBe(true);
    expect(inWindow(spread, 1.26)).toBe(false);
    expect(inWindow(PARAMETERS.tintHue, 30)).toBe(true);
    expect(inWindow(PARAMETERS.tintHue, 100)).toBe(false);
    expect(inWindow(PARAMETERS.tintHue, 355)).toBe(true);
    expect(inWindow(PARAMETERS.hue, 0)).toBe(true);
    expect(inWindow(PARAMETERS.hue, 360)).toBe(true);
  });

  it('gives each neighbouring seed step its own reading', () => {
    for (const key of PARAMETER_KEYS) {
      const spec = PARAMETERS[key];
      const step = stepOf(spec);
      for (let at = 0; at < 255; at += 1) {
        expect(formatReadout(spec, spec.min + at * step), `${key} ${at}`).not.toBe(
          formatReadout(spec, spec.min + (at + 1) * step),
        );
      }
    }
  });
});

describe('the dial rim', () => {
  const dots = (html: string) =>
    [...html.matchAll(/data-token="([^"]+)" cx="([\d.]+)" cy="([\d.]+)" r="[\d.]+" fill="(#[0-9a-f]{6})"/g)].map((m) => ({
      token: m[1],
      x: Number(m[2]),
      y: Number(m[3]),
      fill: m[4],
    }));
  const hueOf = ({ x, y }: { x: number; y: number }) => angleFromPoint(x - 50, y - 50);

  it('draws one dot per accent, in the hex the engine derived, at the hue of that hex', () => {
    const drawn = dots(render(INSIDE));
    expect(drawn.map((d) => d.token)).toEqual([...RIM_TOKENS]);
    expect(RIM_TOKENS).toHaveLength(9);
    for (const dot of drawn) {
      expect(dot.fill).toBe(palette[dot.token]);
      const want = hexToOklch(palette[dot.token]).h;
      const got = hueOf(dot);
      expect(Math.abs(((got - want + 540) % 360) - 180), dot.token).toBeLessThan(0.5);
    }
  });

  it('reads the rim from the palette, not the vector: spread moves a dot with the hue held', () => {
    const wide = derivePalette({ ...IDENTITY, spread: 1.4 }, blocks.light);
    const before = dots(render(INSIDE, undefined, palette)).find((d) => d.token === '--warn')!;
    const after = dots(render(INSIDE, undefined, wide)).find((d) => d.token === '--warn')!;
    expect(hueOf(after)).not.toBeCloseTo(hueOf(before), 0);
  });

  it('draws no dot for a token the palette does not carry', () => {
    const { '--warn': _warn, ...without } = palette;
    expect(dots(render(INSIDE, undefined, without))).toHaveLength(RIM_TOKENS.length - 1);
  });
});

describe('the dial arithmetic', () => {
  it('reads the four compass points, 0 at 3 o’clock and clockwise', () => {
    expect(angleFromPoint(1, 0)).toBe(0);
    expect(angleFromPoint(0, 1)).toBe(90);
    expect(angleFromPoint(-1, 0)).toBe(180);
    expect(angleFromPoint(0, -1)).toBe(270);
  });

  it('steps one degree by arrow, fifteen with shift, to the ends by Home and End, and ignores the rest', () => {
    expect(keyValue('ArrowRight', false, 100, 0, 360)).toBe(101);
    expect(keyValue('ArrowUp', false, 100, 0, 360)).toBe(101);
    expect(keyValue('ArrowLeft', false, 100, 0, 360)).toBe(99);
    expect(keyValue('ArrowDown', true, 100, 0, 360)).toBe(85);
    expect(keyValue('ArrowRight', true, 100, 0, 360)).toBe(115);
    expect(keyValue('Home', false, 100, 0, 360)).toBe(0);
    expect(keyValue('End', false, 100, 0, 360)).toBe(360);
    expect(keyValue('a', false, 100, 0, 360)).toBeNull();
  });

  it('clamps at the ends rather than wrapping', () => {
    expect(keyValue('ArrowLeft', false, 0, 0, 360)).toBe(0);
    expect(keyValue('ArrowRight', true, 355, 0, 360)).toBe(360);
  });

  it('changes the snapped value on every arrow press along the range, except at the ends', () => {
    const hue = PARAMETERS.hue;
    for (let at = 0; at <= 255; at += 1) {
      const value = snapToSeed(hue, at * stepOf(hue));
      for (const [key, shift] of [['ArrowRight', false], ['ArrowLeft', false], ['ArrowRight', true], ['ArrowLeft', true]] as const) {
        const next = snapToSeed(hue, keyValue(key, shift, value, hue.min, hue.max)!);
        const atEnd = (key === 'ArrowRight' && value === hue.max) || (key === 'ArrowLeft' && value === hue.min);
        if (!atEnd) expect(next, `${key} ${shift} from ${value}`).not.toBe(value);
      }
    }
  });
});

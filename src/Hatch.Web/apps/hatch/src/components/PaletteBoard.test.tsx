/* <PaletteBoard> lives in @hatch/ui (packages/ui/src), not in this app - the test stays here for the same reason
   ProjectMark.test.tsx does: packages/ui has no test runner. */
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import tokensCss from '@hatch/ui/tokens.css?raw';
import { PaletteBoard } from '@hatch/ui';
import {
  CONTRAST_PAIRS,
  IDENTITY,
  ROLES,
  derivePalette,
  measurePairs,
  parseTokenBlocks,
  type MeasuredPair,
  type Palette,
} from '@hatch/ui/palette';

const blocks = parseTokenBlocks(tokensCss);

const render = (palette: Palette, pairs: readonly MeasuredPair[], children?: ReactNode) =>
  renderToStaticMarkup(
    <PaletteBoard palette={palette} pairs={pairs}>
      {children}
    </PaletteBoard>,
  );

const count = (haystack: string, needle: string) => haystack.split(needle).length - 1;

describe.each([
  ['light', blocks.light],
  ['dark', blocks.dark],
] as const)('PaletteBoard, %s block', (_name, block) => {
  const palette = derivePalette(IDENTITY, block);
  const pairs = measurePairs(palette);
  const html = render(palette, pairs);

  it('shows a swatch for every non-fixed token and none for a fixed one', () => {
    for (const [token, value] of Object.entries(palette)) {
      if (ROLES[token] === 'fixed') {
        expect(html).not.toContain(`data-token="${token}"`);
      } else {
        expect(html).toContain(`data-token="${token}"`);
        expect(html).toContain(`>${value}<`);
      }
    }
  });

  it('has one table row for every guarded pair', () => {
    expect(count(html, '<tr data-pair')).toBe(CONTRAST_PAIRS.length);
  });

  it('lights every row under the guard and says nothing is under floor', () => {
    expect(count(html, 'data-state="clears"')).toBe(CONTRAST_PAIRS.length);
    expect(html).not.toContain('under floor');
  });

  it('draws a primary fill in exactly the palette’s primary', () => {
    expect(html).toContain(`background:${palette['--primary']};color:${palette['--on-accent']}`);
  });
});

describe('PaletteBoard readout', () => {
  const palette = derivePalette(IDENTITY, blocks.light);
  const measured = measurePairs(palette);
  const withRatio = (at: number, ratio: number) => measured.map((pair, i) => (i === at ? { ...pair, ratio } : pair));

  it('marks a pair a tenth under its floor, and only that one', () => {
    const html = render(palette, withRatio(2, measured[2].floor - 0.1));
    expect(count(html, 'data-state="under"')).toBe(1);
    expect(count(html, 'under floor')).toBe(1);
    expect(html).toContain('-0.10 under floor');
  });

  it('keeps a pair exactly on its floor lit', () => {
    const html = render(palette, withRatio(2, measured[2].floor));
    expect(html).not.toContain('data-state="under"');
    expect(html).toContain('+0.00');
  });

  it('reads the ratio to one decimal and says what a pair sits over', () => {
    const over = CONTRAST_PAIRS.findIndex((pair) => pair.over !== undefined);
    const html = render(palette, withRatio(over, 4.56));
    expect(html).toContain('4.6:1');
    expect(html).toContain(`${CONTRAST_PAIRS[over].fg} on ${CONTRAST_PAIRS[over].bg} over ${CONTRAST_PAIRS[over].over}`);
  });

  it('renders children after the last swatch and before the table', () => {
    const html = render(palette, measured, <p id="dial">dial</p>);
    const last = html.lastIndexOf('data-token=');
    expect(html.indexOf('id="dial"')).toBeGreaterThan(last);
    expect(html.indexOf('id="dial"')).toBeLessThan(html.indexOf('<table'));
  });

  it('files a token the board does not place under Other, and paints nothing for a missing one', () => {
    const html = render({ ...palette, '--future': '#123456' }, measured);
    expect(html).toContain('<h3>Other</h3>');
    expect(html).toContain('data-token="--future"');
    const bare = { ...palette };
    delete bare['--chrome-end'];
    expect(() => render(bare, measured)).not.toThrow();
  });
});

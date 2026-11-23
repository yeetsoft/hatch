/* The health strip's stylesheet, read as text the way boardHeaderCss.test.ts
   reads the board header's: there is no DOM in this runner to lay anything out
   in. It pins what the screenshot cannot: no literal colour, tiles of equal
   width, and a tone that colours from a token both dark blocks define. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const strip = (text: string): string => text.replace(/\/\*[\s\S]*?\*\//g, '');

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8');
const tokens = strip(readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8'));

/** The health strip's own section: from its heading comment to the next
    section heading. */
const section = ((): string => {
  const start = css.indexOf('/* ---- The board health strip ----');
  const end = css.indexOf('\n/* ', css.indexOf('.hatch-health-toggle-glyph'));
  if (start === -1) throw new Error('App.css has no board health strip section');
  return strip(css.slice(start, end === -1 ? undefined : end));
})();

/** The declarations of `selector` in the section. */
function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of section.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

/** The blocks of tokens.css that carry a dark value, by their opening line. */
const darkBlocks = ((): string[] => {
  const blocks: string[] = [];
  for (const opener of ['@media (prefers-color-scheme: dark) {', ":root[data-theme='dark'] {"]) {
    const start = tokens.indexOf(opener);
    if (start === -1) throw new Error(`tokens.css has no ${opener}`);
    let depth = 0;
    let end = tokens.indexOf('{', start);
    const open = end;
    for (; end < tokens.length; end++) {
      if (tokens[end] === '{') depth++;
      else if (tokens[end] === '}' && --depth === 0) break;
    }
    blocks.push(tokens.slice(open, end));
  }
  return blocks;
})();

describe('the board health strip stylesheet', () => {
  it('uses no literal colour, so both themes follow the tokens', () => {
    expect(section).not.toMatch(/#[0-9a-f]{3,8}\b/i);
    expect(section).not.toMatch(/\b(?:rgb|hsl)a?\(/i);
  });

  it('gives every tile an equal share of the width, after one fixed control track', () => {
    const strip = declarationsOf('.hatch-health-strip');

    expect(strip['display']).toBe('grid');
    expect(strip['grid-auto-flow']).toBe('column');
    expect(strip['grid-auto-columns']).toBe('minmax(0, 1fr)');
    expect(strip['grid-template-columns']).toMatch(/^[\d.]+rem$/);
  });

  it('keeps its own height, so the columns scroll inside themselves under it', () => {
    expect(declarationsOf('.hatch-health-strip')['flex']).toBe('none');
  });

  it.each(['good', 'warn', 'neutral'])('colours the %s tone from a token defined in both dark blocks', (tone) => {
    const colours = [...section.matchAll(new RegExp(`\\.hatch-health-tile--${tone}[^{]*\\{([^{}]*)\\}`, 'g'))].flatMap(
      ([, body]) => [...body.matchAll(/color:\s*var\((--[\w-]+)\)/g)].map((m) => m[1]),
    );

    expect(colours.length).toBeGreaterThan(0);
    for (const token of colours) for (const block of darkBlocks) expect(block).toContain(`${token}:`);
  });

  it('colours every drawn mark from a token defined in both dark blocks', () => {
    const used = new Set([...section.matchAll(/var\((--[\w-]+)\)/g)].map((m) => m[1]));

    for (const token of used) {
      // Spacing, type, radius and the filter bar's row height do not change with the theme.
      if (/^--(sp|t|r|facet-h)(-|$)/.test(token)) continue;
      for (const block of darkBlocks) expect(block, token).toContain(`${token}:`);
    }
  });
});

describe('the board health strip on a phone', () => {
  it('holds the closed row to a finger-high, full-width button', () => {
    const row = declarationsOf('.hatch-health-row');

    expect(row['width']).toBe('100%');
    expect(row['min-height']).toBe('44px');
    expect(row['display']).toBe('flex');
  });

  it('lets each of the four cells shrink and cuts its text rather than wrapping it', () => {
    expect(declarationsOf('.hatch-health-cell')['min-width']).toBe('0');
    expect(declarationsOf('.hatch-health-cell')['flex']).toBe('1 1 0');
    expect(declarationsOf('.hatch-health-cell-second')['white-space']).toBe('nowrap');
  });

  it('draws the chevron from borders, with no image', () => {
    const chevron = declarationsOf('.hatch-health-chevron');

    expect(chevron['border-right']).toContain('2px solid');
    expect(chevron['border-bottom']).toContain('2px solid');
    expect(section).not.toMatch(/url\(/);
  });

  it('takes no gap for the open region while it is empty', () => {
    expect(declarationsOf('.hatch-health-open:empty')['display']).toBe('none');
  });

  it('stacks the open tiles in one column, one tile to a row', () => {
    const stacked = declarationsOf('.hatch-health-strip--stacked');

    expect(stacked['grid-template-columns']).toBe('minmax(0, 1fr)');
    expect(stacked['grid-auto-flow']).toBe('row');
  });

  it('puts the sparkline to the right of the numbers, spanning the text lines', () => {
    const tile = declarationsOf('.hatch-health-strip--stacked .hatch-health-tile');

    expect(tile['grid-template-areas']).toContain("'label spark'");
    expect(tile['grid-template-areas']).toContain("'detail spark'");
    expect(declarationsOf('.hatch-health-strip--stacked .hatch-health-spark')['grid-area']).toBe('spark');
    expect(declarationsOf('.hatch-health-strip--stacked .hatch-health-spark')['min-width']).toBe('0');
  });

  it('gives the window choices a finger-high target when open', () => {
    const window = declarationsOf('.hatch-health-strip--stacked .hatch-health-window');

    expect(window['min-height']).toBe('44px');
    expect(window['min-width']).toBe('44px');
  });
});

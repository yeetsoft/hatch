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

describe('the Oldest in flight tile', () => {
  it.each([
    ['healthy', '--success-fill'],
    ['aging', '--warn'],
    ['past', '--danger'],
    ['nonormal', '--muted'],
  ])('fills the %s band from %s, which both dark blocks define', (band, token) => {
    expect(declarationsOf(`.hatch-health-band--${band}`)['fill']).toBe(`var(${token})`);
    for (const block of darkBlocks) expect(block).toContain(`${token}:`);
  });

  it('draws the neutral band faint', () => {
    expect(Number(declarationsOf('.hatch-health-band--nonormal')['opacity'])).toBeLessThan(0.5);
  });

  it('hides the peek until the number or the detail line is pointed at or focused', () => {
    expect(declarationsOf('.hatch-health-peek')['display']).toBe('none');
    for (const selector of [
      '.hatch-health-value:hover ~ .hatch-health-peek',
      '.hatch-health-value:focus ~ .hatch-health-peek',
      '.hatch-health-detail:hover ~ .hatch-health-peek',
      '.hatch-health-detail:focus-within ~ .hatch-health-peek',
    ]) {
      expect(declarationsOf(selector)['display']).toBe('block');
    }
  });

  it('never lets the peek take a pointer', () => {
    expect(declarationsOf('.hatch-health-peek')['pointer-events']).toBe('none');
  });

  it('spans the peek across a stacked tile', () => {
    expect(declarationsOf('.hatch-health-strip--stacked .hatch-health-peek')['grid-column']).toBe('1 / -1');
  });
});

describe('the Runners tile', () => {
  it('caps a lane at half a rem, over the lane count the body sets', () => {
    expect(declarationsOf('.hatch-health-lane-cells')['height']).toBe('min(0.5rem, calc(100% / var(--hatch-lanes, 1)))');
  });

  it('draws no names column, so the lanes fill the width the pointer is read against', () => {
    expect(css).not.toMatch(/\.hatch-health-lane-names?\b/);
    expect(declarationsOf('.hatch-health-lane-grid')['min-width']).toBe('0');
  });

  it('draws idle and off short, paused hollow, down striped - no state by colour alone', () => {
    const idle = declarationsOf('.hatch-health-lane-cell--idle');
    const off = declarationsOf('.hatch-health-lane-cell--off');
    const paused = declarationsOf('.hatch-health-lane-cell--paused');
    const down = declarationsOf('.hatch-health-lane-cell--down');

    expect(Number.parseInt(idle['height'], 10)).toBeLessThan(100);
    expect(Number.parseInt(off['height'], 10)).toBeLessThan(100);
    expect(paused['background']).toBe('transparent');
    expect(paused['border']).toContain('solid');
    expect(down['background-image']).toContain('repeating-linear-gradient');
  });

  it('marks a dotted let-go and a full moved and working, told apart by shade', () => {
    const letGo = declarationsOf('.hatch-health-lane-cell--let-go');
    const moved = declarationsOf('.hatch-health-lane-cell--moved');
    const working = declarationsOf('.hatch-health-lane-cell--working');

    expect(letGo['border-bottom']).toContain('dotted');
    expect(moved['background']).not.toBe(working['background']);
  });

  it('shows the legend on hover, focus-within or a pressed Key, and otherwise hides it', () => {
    expect(declarationsOf('.hatch-health-legend')['display']).toBe('none');
    for (const selector of [
      '.hatch-health-tile:hover .hatch-health-legend',
      '.hatch-health-tile:focus-within .hatch-health-legend',
      ".hatch-health-key[aria-expanded='true'] ~ .hatch-health-legend",
    ]) {
      expect(declarationsOf(selector)['display']).toBe('block');
    }
  });

  it('never lets the hover popover take a pointer, so it cannot steal the hover it draws from', () => {
    expect(declarationsOf('.hatch-health-lane-hover')['pointer-events']).toBe('none');
  });
});

describe('the board health strip on a phone', () => {
  it('holds the closed row to a finger-high, full-width button', () => {
    const row = declarationsOf('.hatch-health-row');

    expect(row['width']).toBe('100%');
    expect(row['min-height']).toBe('44px');
    expect(row['display']).toBe('flex');
  });

  it('lets each of the five cells shrink and cuts its text rather than wrapping it', () => {
    expect(declarationsOf('.hatch-health-cell')['min-width']).toBe('0');
    expect(declarationsOf('.hatch-health-cell')['flex']).toBe('1 1 0');
    expect(declarationsOf('.hatch-health-cell-second')['white-space']).toBe('nowrap');
  });

  it('cuts the fifth cell, Runners, the same way rather than wrapping it', () => {
    expect(declarationsOf('.hatch-health-cell-number')['overflow']).toBe('hidden');
    expect(declarationsOf('.hatch-health-cell-number')['text-overflow']).toBe('ellipsis');
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

describe('the dialog a health tile opens', () => {
  it('says a tile with a dialog is pressable, and leaves Runners its crosshair', () => {
    expect(declarationsOf('.hatch-health-spark--open')['cursor']).toBe('pointer');
    expect(declarationsOf('.hatch-health-spark')['cursor']).toBe('crosshair');
    expect(section).not.toContain('hatch-health-spark--link');
  });

  it('gives the chart a fixed height and the full width, so its svg can measure itself', () => {
    expect(declarationsOf('.hatch-health-dialog-chart')['height']).toBe('14rem');
    expect(declarationsOf('.hatch-health-dialog-svg')['width']).toBe('100%');
    expect(declarationsOf('.hatch-health-dialog-svg')['height']).toBe('100%');
  });

  it('lets a sideways drag mark a bar and a vertical one scroll', () => {
    expect(declarationsOf('.hatch-health-dialog-chart')['touch-action']).toBe('pan-y');
  });

  it('paints each patterned mark from its own ground, so a pattern is told apart by more than shade', () => {
    for (const mark of ['runner', 'person', 'board', 'pullRequests', 'questions']) {
      expect(declarationsOf(`.hatch-health-dialog-ground--${mark}`)['fill'], mark).toMatch(/^var\(--[\w-]+\)$/);
    }
    for (const mark of ['person', 'board', 'questions']) {
      expect(declarationsOf(`.hatch-health-dialog-ink--${mark}`)['fill'], mark).toMatch(/^var\(--[\w-]+\)$/);
    }
  });

  it('keeps the dialog\'s own marked line apart from the one the tiles count', () => {
    expect(declarationsOf('.hatch-health-dialog-moment')['stroke']).toBe('var(--ink)');
    expect(section).not.toMatch(/\.hatch-health-dialog[^{]*\.hatch-health-moment\b/);
  });

  it('holds every control in the dialog to a finger high on a phone', () => {
    expect(declarationsOf('.hatch-health-dialog .hatch-health-window')['min-height']).toBe('44px');
    expect(declarationsOf('.hatch-health-dialog-link')['min-height']).toBe('44px');
    expect(declarationsOf('.hatch-modal__panel:has(.hatch-health-dialog) .hatch-modal__close')['min-height']).toBe('44px');
    expect(declarationsOf('.hatch-modal__panel:has(.hatch-health-dialog) .hatch-modal__close')['min-width']).toBe('44px');
  });

  it('leaves Modal\'s own stylesheet to every other dialog', () => {
    const modal = readFileSync(new URL('../../../../packages/ui/src/components/Modal.css', import.meta.url), 'utf8');

    expect(modal).not.toContain('health');
  });
});

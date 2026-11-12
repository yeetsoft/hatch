/* The timeline's styles paint from tokens and the status's own colour, never
   from a literal - read as text, the way typeGlyphCss.test.ts reads App.css,
   since there is no DOM in this runner to lay anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

const RULE = /([^{}]+)\{([^{}]*)\}/g;

/** The declarations of every rule in `source` whose selector list names
    `selector` on its own - `.a` but not `.a-b`, and not `.a .b` - merged, later
    winning. */
function declarationsIn(source: string, selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of source.matchAll(RULE)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

/** The text between the braces of the first `@media (max-width: 40rem) {` that
    follows the timeline's own block. */
function mediaBlock(): string {
  const start = css.indexOf('@media (max-width: 40rem) {', css.indexOf('.hatch-timeline {'));
  expect(start).toBeGreaterThan(-1);
  const open = css.indexOf('{', start);
  let depth = 0;
  for (let i = open; i < css.length; i++) {
    if (css[i] === '{') depth++;
    if (css[i] === '}' && --depth === 0) return css.slice(open + 1, i);
  }
  throw new Error('the 40rem block is not closed');
}

describe('the timeline styles', () => {
  it('hold no literal colour, size or spacing', () => {
    const literal = /#[0-9a-f]{3,8}\b|\brgba?\(|\bhsla?\(|\d+(\.\d+)?(px|rem|em)\b/i;
    const border = /\b1px solid var\(--[a-z0-9-]+\)/g;
    let seen = 0;

    for (const [, selectors, body] of css.matchAll(RULE)) {
      if (!selectors.includes('.hatch-timeline')) continue;
      seen++;
      expect(body.replace(border, ''), selectors.trim()).not.toMatch(literal);
    }

    expect(seen).toBeGreaterThan(10);
  });

  it('fills a segment from its project if one is set, else its status, and Other from a neutral token', () => {
    expect(declarationsIn(css, '.hatch-timeline-segment')['fill']).toBe('var(--mark-color, var(--status-color))');
    expect(declarationsIn(css, '.hatch-timeline-segment--other')['fill']).toBe('var(--line-strong)');
  });

  it('hides the title on a phone', () => {
    expect(declarationsIn(mediaBlock(), '.hatch-timeline-title')['display']).toBe('none');
  });

  it('pushes the Hide button right and keeps it on a phone', () => {
    expect(declarationsIn(css, '.hatch-timeline-hide')['margin-left']).toBe('auto');
    expect(declarationsIn(mediaBlock(), '.hatch-timeline-hide')['display']).toBeUndefined();
  });

  it('makes Show the same pill as Hide, and keeps its title on a phone', () => {
    expect(declarationsIn(css, '.hatch-timeline-show')['margin-left']).toBe(
      declarationsIn(css, '.hatch-timeline-hide')['margin-left'],
    );
    expect(declarationsIn(mediaBlock(), '.hatch-timeline-hidden__title')['display']).toBeUndefined();
  });

  it('wraps the header at every width, so the controls drop under the title when they do not fit beside it', () => {
    expect(declarationsIn(css, '.hatch-timeline-header')['flex-wrap']).toBe('wrap');
    expect(declarationsIn(mediaBlock(), '.hatch-timeline-header')['flex-wrap']).toBeUndefined();
    expect(declarationsIn(css, '.hatch-timeline-header .hatch-page-header__actions')['flex-wrap']).toBe('wrap');
  });

  it('keeps a floor under the header titles, from a token', () => {
    expect(declarationsIn(css, '.hatch-timeline-header .hatch-page-header__titles')['flex']).toBe(
      '1 1 calc(var(--measure-prose) / 2)',
    );
  });

  it('gives the controls the facet height the board filters define for themselves', () => {
    expect(declarationsIn(css, '.hatch-timeline-controls')['--facet-h']).toBe('calc(var(--sp-8) + var(--sp-1))');
  });

  it('scrolls the pane sideways', () => {
    expect(declarationsIn(css, '.hatch-timeline-pane')['overflow-x']).toBe('auto');
  });

  it('paints the ruler brush from a token, over the plot, and lets the pointer through', () => {
    const brush = declarationsIn(css, '.hatch-timeline-brush');

    expect(brush['background']).toBe('var(--primary-bg)');
    expect(brush['position']).toBe('absolute');
    expect(brush['pointer-events']).toBe('none');
  });

  it('declares the row height where the Timeline and the Cumulative flow both read it, from tokens', () => {
    const timeline = declarationsIn(css, '.hatch-timeline')['--hatch-timeline-row'];
    const flow = declarationsIn(css, '.hatch-cumulative-flow')['--hatch-timeline-row'];

    expect(timeline).toMatch(/^calc\(var\(--sp-\d+\) \+ var\(--sp-\d+\)\)$/);
    expect(flow).toBe(timeline);
    expect(declarationsIn(css, '.hatch-timeline-ruler')['height']).toBe('var(--hatch-timeline-row)');
  });

  /* A new reader is one more box that must be rendered under one of the two
     declarers, so adding one is a decision this list makes visible. */
  it('reads the row height in only the boxes both charts render', () => {
    const readers: string[] = [];

    for (const [, selectors, body] of css.matchAll(RULE)) {
      if (body.includes('var(--hatch-timeline-row)')) readers.push(...selectors.split(',').map((s) => s.trim()));
    }

    expect(readers.sort()).toEqual([
      '.hatch-timeline-corner',
      '.hatch-timeline-label',
      '.hatch-timeline-lane',
      '.hatch-timeline-ruler',
    ]);
  });

  it('makes only the brushable ruler a drag target', () => {
    expect(declarationsIn(css, '.hatch-timeline-ruler--brushable')['cursor']).toBe('col-resize');
    expect(declarationsIn(css, '.hatch-timeline-ruler')['cursor']).toBeUndefined();
  });
});

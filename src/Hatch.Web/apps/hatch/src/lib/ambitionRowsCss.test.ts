/* The Ambitions page's rows line up: three columns set once on the list, a
   first line that sits level across a row, and the epics' columns shared by
   every line. Read as text, the way barShapeCss.test.ts reads App.css - there
   is no DOM in this runner to lay them out in; the e2e test measures the
   rendered page. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** The declarations of every rule whose selector list holds `selector` as one
    of its entries, merged, later winning. `.a .b` is its own entry, taken as
    written: it is not `.b`. Rules inside a media query are found too, so
    `inMedia` says which side of it to read. */
function declarationsOf(selector: string, inMedia = false): Record<string, string> {
  const props: Record<string, string> = {};
  const source = inMedia ? mediaBodies.join('\n') : withoutMedia;
  for (const [, selectors, body] of source.matchAll(/([^{}@;]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement
        .slice(colon + 1)
        .trim()
        .replace(/\s+/g, ' ');
    }
  }
  return props;
}

/** The inside of every `@media (max-width: 40rem) { ... }`, one level of braces deep. */
const mediaBodies = [...css.matchAll(/@media \(max-width: 40rem\) \{((?:[^{}]*\{[^{}]*\})*)\s*\}/g)].map((m) => m[1]);
const withoutMedia = css.replace(/@media[^{]*\{(?:[^{}]*\{[^{}]*\})*\s*\}/g, '');

const ROW = '.hatch-ambition-list > .hatch-ambition-row';

describe('the Ambitions list', () => {
  it('spaces two cards from the space ladder', () => {
    const list = declarationsOf('.hatch-ambition-list');

    expect(list.display).toBe('flex');
    expect(list['flex-direction']).toBe('column');
    expect(list.gap).toMatch(/^var\(--sp-[\w-]+\)$/);
  });

  it('sets the three columns once, on the list, and every row reads them', () => {
    const declared = [...withoutMedia.matchAll(/--ambition-columns:\s*([^;]+);/g)];

    expect(declared).toHaveLength(1);
    expect(declarationsOf('.hatch-ambition-list')['--ambition-columns']).toBe(
      'minmax(0, 1fr) minmax(0, 1fr) minmax(0, 2fr)',
    );
    expect(declarationsOf(ROW).display).toBe('grid');
    expect(declarationsOf(ROW)['grid-template-columns']).toBe('var(--ambition-columns)');
    expect(declarationsOf(ROW)['align-items']).toBe('start');
  });
});

describe('the stage on a row', () => {
  it('colours its pill from the fills the app already has', () => {
    expect(declarationsOf('.hatch-stage-pill').color).toBe('var(--muted)');
    for (const stage of ['budget-spent', 'stopped']) {
      expect(declarationsOf(`.hatch-stage-${stage}`).background).toBe('var(--warn-bg)');
    }
    for (const stage of ['running', 'complete', 'done']) {
      expect(declarationsOf(`.hatch-stage-${stage}`).background).toBe('var(--success-bg)');
    }
  });
});

describe('a row as a card', () => {
  it('has the card pad and ground, no grab cursor and no hover ring of the board tile', () => {
    const row = declarationsOf(ROW);

    expect(row.padding).toBe('var(--sp-4)');
    expect(row.background).toBe('var(--card)');
    expect(row.cursor).toBe('default');
    expect(declarationsOf(`${ROW}:hover`)['border-color']).toBe('var(--line)');
  });

  it('is written to beat the board tile, which wears the same class', () => {
    // 0,2,0 against the tile's 0,1,0 and its :hover at 0,2,0, written earlier.
    expect(ROW.split('.')).toHaveLength(3);
    expect(css.indexOf(ROW)).toBeGreaterThan(css.indexOf('.hatch-card:hover'));
  });
});

describe('the first line of each part', () => {
  it('is one size, weight, line height and margin for the title and both headings', () => {
    const title = declarationsOf('.hatch-ambition-title');
    const heading = declarationsOf('.hatch-ambition-heading');

    for (const property of ['font-size', 'font-weight', 'line-height', 'margin']) {
      expect(title[property]).toBeDefined();
      expect(title[property]).toBe(heading[property]);
    }
    expect(title['font-size']).toBe('var(--t-body)');
    expect(title.margin).toBe('0');
    expect(title.display).toBe('block');
  });

  it('is followed by its own top margin, not a bottom margin of its own', () => {
    expect(declarationsOf('.hatch-ambition-heading')['margin-bottom']).toBeUndefined();
    expect(declarationsOf('.hatch-ambition-head-meta')['margin-top']).toBe('var(--sp-2)');
    expect(declarationsOf('.hatch-ambition-part > p').margin).toBe('var(--sp-2) 0 0');
  });
});

describe('the epics in flight', () => {
  it('are one grid of four columns, each line and each meta a subgrid of it', () => {
    expect(declarationsOf('.hatch-ambition-epics').display).toBe('grid');
    expect(declarationsOf('.hatch-ambition-epics')['grid-template-columns']).toMatch(/^max-content minmax\(0, 1fr\) max-content /);

    const line = declarationsOf('.hatch-ambition-epics .hatch-progress-row');
    expect(line.display).toBe('grid');
    expect(line['grid-template-columns']).toBe('subgrid');
    expect(line['grid-column']).toBe('1 / -1');

    const meta = declarationsOf('.hatch-ambition-epics .hatch-epics-in-flight-meta');
    expect(meta.display).toBe('grid');
    expect(meta['grid-template-columns']).toBe('subgrid');
    expect(meta['grid-column']).toBe('3 / -1');
    expect(meta['margin-left']).toBe('0');
  });

  it('draw no rule above the first line', () => {
    expect(declarationsOf('.hatch-ambition-epics .hatch-progress-row:first-child')['border-top']).toBe('none');
  });

  it("leave the issue page's Progress list and rows as they were", () => {
    expect(declarationsOf('.hatch-progress-list')).toEqual({
      'list-style': 'none',
      margin: 'var(--sp-4) 0 0',
      padding: '0',
    });
    expect(declarationsOf('.hatch-progress-row')).toEqual({
      display: 'flex',
      'align-items': 'center',
      'flex-wrap': 'wrap',
      gap: 'var(--sp-2)',
      'min-width': '0',
      padding: 'var(--sp-2) 0',
      'border-top': '1px solid var(--line)',
    });
    expect(declarationsOf('.hatch-progress-title')).toEqual({
      flex: '1 1 12rem',
      'min-width': '0',
      'overflow-wrap': 'anywhere',
    });
  });
});

describe('on a phone', () => {
  it('stacks head, capabilities and epics in one column with one gap between', () => {
    const row = declarationsOf(ROW, true);

    expect(row['grid-template-columns']).toBe('1fr');
    expect(row['row-gap']).toMatch(/^var\(--sp-[\w-]+\)$/);
  });

  it('puts the epic lines back to wrapping lines', () => {
    expect(declarationsOf('.hatch-ambition-epics', true).display).toBe('block');
    expect(declarationsOf('.hatch-ambition-epics .hatch-progress-row', true).display).toBe('flex');
    expect(declarationsOf('.hatch-ambition-epics .hatch-epics-in-flight-meta', true).display).toBe('flex');
    expect(declarationsOf('.hatch-ambition-epics .hatch-epics-in-flight-meta', true).flex).toBe('1 0 100%');
  });
});

/* The rule that keeps a board with no WIP limit exactly as it was, and the
   rule that gives the four tints their colour. Read as text, the way
   confirmationsCss.test.ts reads App.css - there is no DOM in this runner to
   lay anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** The declarations of every rule whose selector list names `selector` on its
    own - `.a` but not `.a-b`, and not `.a .b` - merged, later winning. */
function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

describe('a board with no WIP limit', () => {
  it('leaves .hatch-board with no grid-template-rows and no grid-row of its own', () => {
    const board = declarationsOf('.hatch-board');

    expect(board).not.toHaveProperty('grid-template-rows');
    expect(board).not.toHaveProperty('grid-row');
  });

  it('leaves .hatch-column with no grid-row of its own', () => {
    expect(declarationsOf('.hatch-column')).not.toHaveProperty('grid-row');
  });
});

describe('the WIP tints', () => {
  it.each(['.hatch-wip-room', '.hatch-wip-tight', '.hatch-wip-full', '.hatch-wip-over'])(
    '%s sets --wip-tint, --wip-wash and --wip-ink',
    (selector) => {
      const rule = declarationsOf(selector);

      expect(rule).toHaveProperty('--wip-tint');
      expect(rule).toHaveProperty('--wip-wash');
      expect(rule).toHaveProperty('--wip-ink');
    },
  );

  it('draws over as a solid fill rather than a wash, unlike the other three', () => {
    expect(declarationsOf('.hatch-wip-over')['--wip-wash']).toBe('var(--danger)');
    expect(declarationsOf('.hatch-wip-room')['--wip-wash']).toBe('var(--success-bg)');
  });
});

describe('the WIP zone rows', () => {
  it('gives .hatch-board--wip a second row for the band', () => {
    expect(declarationsOf('.hatch-board--wip')).toHaveProperty('grid-template-rows');
  });

  it('puts the band in row 1 and lets the columns fall into row 2', () => {
    expect(declarationsOf('.hatch-wip-band')['grid-row']).toBe('1');
    expect(declarationsOf('.hatch-board--wip > .hatch-column')['grid-row']).toBe('2');
  });
});

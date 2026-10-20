/* The card's type glyph gets its colour from tokens.css alone - a hex literal
   here is a bug, per the brief on HA-547. Read as text, the way
   claimCss.test.ts reads App.css - there is no DOM in this runner to lay
   anything out in. */
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

describe('the board card type glyph', () => {
  it('colours each type off a token, never a hex literal', () => {
    expect(declarationsOf('.hatch-type-glyph--epic')['color']).toBe('var(--primary)');
    expect(declarationsOf('.hatch-type-glyph--bug')['color']).toBe('var(--danger)');
    expect(declarationsOf('.hatch-type-glyph--reflection')['color']).toBe('var(--series-6)');
    expect(declarationsOf('.hatch-type-glyph--story')['color']).toBe('var(--series-7)');
    expect(declarationsOf('.hatch-type-glyph--task')['color']).toBe('var(--series-3)');
  });
});

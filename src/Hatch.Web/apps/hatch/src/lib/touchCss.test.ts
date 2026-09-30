/* A resting card must let the browser pan a finger across it - a swipe scrolls
   the column instead of starting a drag. Read as text, the way wipCss.test.ts
   and confirmationsCss.test.ts read App.css - there is no DOM in this runner
   to lay anything out in. */
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

describe('a resting card on a touch screen', () => {
  it('does not block the browser from panning', () => {
    expect(declarationsOf('.hatch-card')['touch-action']).not.toBe('none');
  });
});

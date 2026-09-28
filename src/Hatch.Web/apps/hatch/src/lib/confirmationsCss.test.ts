/* The rule that makes every chicklet one width, held where a future chicklet
   would have to break it. The width is the list's: it is one fixed width and
   stretches whatever it holds, so a new kind of chicklet is the right width
   without doing anything. A chicklet that declares a width of its own is a
   chicklet that no longer lines up with the rest - which is only ever caught by
   eye, so it is caught here instead. Read as text, the way tokens.test.ts reads
   tokens.css: there is no DOM in this runner to lay anything out in. */
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

describe('the chicklet width rule', () => {
  it('finds the rules it reads', () => {
    expect(Object.keys(declarationsOf('.hatch-confirmation')).length).toBeGreaterThan(0);
    expect(Object.keys(declarationsOf('.hatch-confirmations-list')).length).toBeGreaterThan(0);
  });

  it('leaves a chicklet with no width of its own', () => {
    const chicklet = declarationsOf('.hatch-confirmation');

    for (const prop of ['width', 'min-width', 'max-width', 'flex-basis', 'flex']) {
      expect(chicklet, prop).not.toHaveProperty(prop);
    }
  });

  it('gives the list one width, and stretches every chicklet to it', () => {
    const list = declarationsOf('.hatch-confirmations-list');

    expect(list).toHaveProperty('width');
    expect(list['align-items']).toBe('stretch');
  });

  it('narrows the list with a window too small for it', () => {
    expect(declarationsOf('.hatch-confirmations-list').width).toMatch(/100vw/);
  });
});

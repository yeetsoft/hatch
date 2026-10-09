/** The facet's select laid transparent over the whole box, so a press anywhere
    inside the border opens the native list. Read as text, the way barCss.test.ts
    reads App.css - there is no DOM in this runner to press anything in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** The declarations of every rule whose selector list names `selector` on its
    own, merged, later winning. */
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

describe('the facet', () => {
  it('is the positioning context for its select', () => {
    expect(declarationsOf('.hatch-facet')['position']).toBe('relative');
  });

  it('has a select covering the box, invisible and still pressable', () => {
    const select = declarationsOf('.hatch-facet select');
    expect(select['position']).toBe('absolute');
    expect(select['inset']).toBe('0');
    expect(select['opacity']).toBe('0');
    expect(select).not.toHaveProperty('pointer-events');
  });

  it('caps the drawn value at 12rem', () => {
    expect(declarationsOf('.hatch-facet__value')['max-width']).toBe('12rem');
  });
});

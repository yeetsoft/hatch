/* A pasted image is as wide as the screenshot it came from. This holds the one
   rule that keeps it inside the column it is rendered in. Read as text, the way
   editorCss.test.ts reads App.css: there is no DOM in this runner. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

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

describe('an image in rendered markdown', () => {
  const img = declarationsOf('.hatch-markdown img');

  it('never overflows its column, and keeps its proportions', () => {
    expect(img['max-width']).toBe('100%');
    expect(img['height']).toBe('auto');
  });
});

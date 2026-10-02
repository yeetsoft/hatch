/* The build stamp is the same panel at both widths, so its own rule must not
   set display: none anywhere. Read as text, the way touchCss.test.ts and
   navCss.test.ts read a stylesheet - there is no DOM in this runner to lay
   anything out in. The real look is the operator's to verify in a browser. */
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

describe('the build stamp', () => {
  it('does not hide itself at any width', () => {
    expect(declarationsOf('.hatch-build-stamp')['display']).not.toBe('none');
  });
});

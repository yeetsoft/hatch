/* Lives in apps/hatch because packages/ui has no test runner and
   `make test-web` only loops over the apps - tokens.test.ts's header comment
   says the same. Board and Plan are <a>s wearing .hatch-menu__trigger, so the
   base rule must state text-decoration: none itself or the UA underline
   stacks under the active accent's gradient rule. Read as text, the way
   touchCss.test.ts reads App.css - there is no DOM in this runner. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../../../../packages/ui/src/components/Menu.css', import.meta.url), 'utf8').replace(
  /\/\*[\s\S]*?\*\//g,
  '',
);

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

describe('the primary nav trigger', () => {
  it('states its own text-decoration, so a link does not show the UA underline', () => {
    expect(declarationsOf('.hatch-menu__trigger')['text-decoration']).toBe('none');
  });
});

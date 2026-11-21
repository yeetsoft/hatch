/* The unscoped scope control is an outlined pill, painted from the bar's own
   tokens, with a caret that turns over while the panel is open. Read as text,
   the way touchCss.test.ts reads App.css - there is no DOM in this runner to
   lay anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const strip = (text: string) => text.replace(/\/\*[\s\S]*?\*\//g, '');
const css = strip(readFileSync(new URL('../App.css', import.meta.url), 'utf8'));
const menuCss = strip(
  readFileSync(new URL('../../../../packages/ui/src/components/Menu.css', import.meta.url), 'utf8'),
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

const pill = '.hatch-menu__trigger.hatch-scope-control--unscoped';

describe('the unscoped scope control', () => {
  it('is fully rounded, with a 1px outline from the bar ink', () => {
    const rule = declarationsOf(pill);
    expect(rule['border-radius']).toBe('var(--r-pill)');
    expect(rule.border).toMatch(/^1px solid /);
    expect(rule.border).toContain('--on-chrome');
  });

  it('has no gradient fill of its own, since the bar already is one', () => {
    const rule = declarationsOf(pill);
    expect(rule.background ?? '').not.toContain('gradient');
    expect(rule['background-image'] ?? '').not.toContain('gradient');
  });

  it('takes the full ink for its outline on hover and while open', () => {
    const rule = declarationsOf(`${pill}:hover`);
    expect(rule['border-color']).toBe('var(--on-chrome)');
    expect(declarationsOf(`${pill}[aria-expanded='true']`)['border-color']).toBe('var(--on-chrome)');
  });

  it('turns its caret over while the panel is open', () => {
    expect(declarationsOf(".hatch-scope-control--unscoped[aria-expanded='true'] .hatch-menu__caret").transform).toBe(
      'rotate(180deg)',
    );
  });

  it('leaves the shared menu stylesheet alone: no caret rotation there', () => {
    expect(menuCss).not.toContain('rotate(');
    expect(menuCss).not.toContain('scope-control');
  });
});

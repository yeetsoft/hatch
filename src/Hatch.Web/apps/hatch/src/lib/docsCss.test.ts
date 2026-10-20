/* The docs page sits on the design tokens, so day and night both work with no
   second rule. This holds the two things a browser cannot be asked here: that
   no rule under .hatch-docs names a colour or a pixel of its own, and that an
   image stays inside the reading column. Read as text, the way
   markdownCss.test.ts reads App.css: there is no DOM in this runner. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

const rules = [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(([, selectors, body]) => ({
  selectors: selectors.split(',').map((s) => s.trim()),
  body,
}));

function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const { selectors, body } of rules) {
    if (!selectors.includes(selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

describe('the docs page styles', () => {
  const docs = rules.filter((r) => r.selectors.some((s) => s.includes('.hatch-docs')));

  it('use tokens only: no colour literal and no px outside var()', () => {
    expect(docs.length).toBeGreaterThan(0);
    for (const { selectors, body } of docs) {
      const bare = body.replace(/var\([^)]*\)/g, '');
      expect(bare, selectors.join(', ')).not.toMatch(/#[0-9a-f]{3,8}\b/i);
      expect(bare, selectors.join(', ')).not.toMatch(/rgba?\(/i);
      expect(bare, selectors.join(', ')).not.toMatch(/hsla?\(/i);
      expect(bare, selectors.join(', ')).not.toMatch(/\d(\.\d+)?px/);
    }
  });

  it('keep an image inside the column, in proportion', () => {
    const img = declarationsOf('.hatch-docs-body img');
    expect(img['max-width']).toBe('100%');
    expect(img['height']).toBe('auto');
  });

  it('keep a diagram inside the column, scrolling sideways in its own box', () => {
    const svg = declarationsOf('.hatch-docs-mermaid svg');
    expect(svg['max-width']).toBe('100%');
    expect(svg['height']).toBe('auto');

    const box = declarationsOf('.hatch-docs-mermaid');
    expect(box['overflow-x']).toBe('auto');
  });

  it('keeps the sidebar sticky on the desk', () => {
    // declarationsOf merges the @media rule's declarations over the outer
    // rule's, since both share the selector string - the first match, the
    // one outside the media query, is the desk rule this checks.
    const first = docs.find((r) => r.selectors.includes('.hatch-docs-sidebar'));
    expect(first).toBeDefined();
    const props: Record<string, string> = {};
    for (const statement of first!.body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim();
    }
    expect(props['position']).toBe('sticky');
  });
});

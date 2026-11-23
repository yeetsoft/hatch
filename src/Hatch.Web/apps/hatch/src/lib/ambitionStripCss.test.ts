/* The strip's surface, its legend/note style and its column layout are in
   App.css, and there is no DOM in this runner to draw them in - so they are
   read as text, the way ballotCss.test.ts reads it. The browser look is the
   ticket's. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

function parse(body: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const statement of body.split(';')) {
    const colon = statement.indexOf(':');
    if (colon === -1) continue;
    props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
  }
  return props;
}

/** The declarations of the rules whose selector list names exactly `selector`,
    merged, later winning - outside any at-rule. */
function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of css.matchAll(/(?<=^|[;}])\s*([^{};@]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    Object.assign(props, parse(body));
  }
  return props;
}

/** The declarations of the rules whose selector list names exactly `selector`,
    inside a `@media (max-width: 40rem)` block. */
function phoneDeclarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, body] of css.matchAll(/@media \(max-width: 40rem\)\s*\{([\s\S]*?)\n\}/g)) {
    for (const [, selectors, rule] of body.matchAll(/(?<=^|[;}])\s*([^{};@]+)\{([^{}]*)\}/g)) {
      if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
      Object.assign(props, parse(rule));
    }
  }
  return props;
}

describe("the ambition strip's stylesheet", () => {
  it('reads the legend and the count line in the same muted label style', () => {
    const legend = declarationsOf('.hatch-ambition-tally');

    expect(legend['font-size']).toBe('var(--t-label)');
    expect(legend['color']).toBe('var(--muted)');
  });

  it('lays the strip lines on four columns at the desktop profile', () => {
    const lines = declarationsOf('.hatch-ambition-strip-lines');

    expect(lines['grid-template-columns']).toBe('minmax(0, 1fr) max-content minmax(0, 2fr) max-content');
  });

  it('stacks a strip line at the phone profile', () => {
    const line = phoneDeclarationsOf('.hatch-ambition-strip-line');

    expect(line['display']).toBe('flex');
    expect(line['flex-direction']).toBe('column');
  });
});

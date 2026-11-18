/* The ballot's indent and its refusal to widen the page are in App.css, and
   there is no DOM in this runner to draw them in - so they are read as text,
   the way boardNarrowCss.test.ts reads it. The browser look is the ticket's. */
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

describe('the ballot stylesheet', () => {
  it('indents a nested level one step and rules it off from its parent', () => {
    const nested = declarationsOf('.hatch-ballot-nested');

    expect(nested['padding-left']).toBeDefined();
    expect(nested['border-left']).toBeDefined();
  });

  it.each([
    '.hatch-ballot',
    '.hatch-ballot-node',
    '.hatch-ballot-head',
    '.hatch-ballot-title',
    '.hatch-ballot-excerpt',
    '.hatch-ballot-nested',
    '.hatch-ballot .hatch-question',
    '.hatch-ballot .hatch-answer-box input',
  ])('lets %s shrink below its content', (selector) => {
    expect(declarationsOf(selector)['min-width']).toBe('0');
  });

  it.each(['.hatch-ballot-title', '.hatch-ballot-excerpt', '.hatch-ballot .hatch-question-body'])(
    'wraps a long unbroken token in %s',
    (selector) => {
      expect(declarationsOf(selector)['overflow-wrap']).toBe('anywhere');
    },
  );

  it('keeps the excerpt\'s line breaks without rendering it as markdown', () => {
    expect(declarationsOf('.hatch-ballot-excerpt')['white-space']).toBe('pre-line');
  });
});

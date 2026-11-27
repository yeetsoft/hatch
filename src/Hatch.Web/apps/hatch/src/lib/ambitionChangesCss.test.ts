/* Since you last looked: its two columns and the phone's stacking are in
   App.css, and there is no DOM in this runner to draw them in - so they are
   read as text, the way ambitionStripCss.test.ts reads it. The e2e test
   measures the rendered page. */
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
  const outside = css.replace(/@media[^{]*\{[\s\S]*?\n\}/g, '');
  for (const [, selectors, body] of outside.matchAll(/(?<=^|[;}])\s*([^{};@]+)\{([^{}]*)\}/g)) {
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

const CARD = '.hatch-page > .hatch-ambition-changes';

describe("the changes section's stylesheet", () => {
  it('sets two columns on the list, the kind then the words, at the desktop profile', () => {
    expect(declarationsOf('.hatch-ambition-changes-lines')['grid-template-columns']).toBe('max-content minmax(0, 1fr)');
    expect(declarationsOf('.hatch-ambition-changes-lines li')['grid-template-columns']).toBe('subgrid');
    expect(declarationsOf('.hatch-ambition-changes-line')['grid-template-columns']).toBe('subgrid');
  });

  it('draws the kind in the muted label style', () => {
    const kind = declarationsOf('.hatch-ambition-changes-kind');

    expect(kind['font-size']).toBe('var(--t-label)');
    expect(kind['color']).toBe('var(--muted)');
  });

  it('lets long words wrap rather than push the page sideways', () => {
    const words = declarationsOf('.hatch-ambition-changes-words');

    expect(words['min-width']).toBe('0');
    expect(words['overflow-wrap']).toBe('anywhere');
  });

  it('has the card pad and ground, no grab cursor and no hover ring of the board tile', () => {
    const card = declarationsOf(CARD);

    expect(card['padding']).toBe('var(--sp-4)');
    expect(card['background']).toBe('var(--card)');
    expect(card['cursor']).toBe('default');
    expect(declarationsOf(`${CARD}:hover`)['border-color']).toBe('var(--line)');
    // 0,2,0 against the tile's :hover at 0,2,0, so it has to be written later.
    expect(css.indexOf(CARD)).toBeGreaterThan(css.indexOf('.hatch-card:hover'));
  });

  it('takes no margin shorthand on the card, which would undo the page centring', () => {
    expect(declarationsOf(CARD)['margin']).toBeUndefined();
  });

  it('stacks the kind above the words at the phone profile, with a touch target', () => {
    const line = phoneDeclarationsOf('.hatch-ambition-changes-line');

    expect(phoneDeclarationsOf('.hatch-ambition-changes-lines')['display']).toBe('block');
    expect(line['display']).toBe('flex');
    expect(line['flex-direction']).toBe('column');
    expect(line['min-height']).toBe('44px');
  });
});

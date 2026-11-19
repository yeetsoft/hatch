/* A resting card must let the browser pan a finger across it - a swipe scrolls
   the column instead of starting a drag. Read as text, the way wipCss.test.ts
   and confirmationsCss.test.ts read App.css - there is no DOM in this runner
   to lay anything out in. */
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

/** The declarations of `selector` as they read inside `@media (${mediaQuery})`
    blocks of `css` alone, every such block searched and merged, later winning -
    depth-counting braces to find each block's matching close; nothing in these
    files nests an `@media` inside another. */
function declarationsWithin(css: string, mediaQuery: string, selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  const needle = `@media (${mediaQuery}) {`;
  for (let start = css.indexOf(needle); start !== -1; start = css.indexOf(needle, start + needle.length)) {
    const openBrace = css.indexOf('{', start);
    let depth = 0;
    let end = openBrace;
    for (; end < css.length; end++) {
      if (css[end] === '{') depth++;
      else if (css[end] === '}') {
        depth--;
        if (depth === 0) break;
      }
    }
    const block = css.slice(openBrace + 1, end);

    for (const [, selectors, body] of block.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
      if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
      for (const statement of body.split(';')) {
        const colon = statement.indexOf(':');
        if (colon === -1) continue;
        props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
      }
    }
  }
  if (Object.keys(props).length === 0) throw new Error(`no @media (${mediaQuery}) block names ${selector}`);
  return props;
}

describe('a resting card on a touch screen', () => {
  it('does not block the browser from panning', () => {
    expect(declarationsOf('.hatch-card')['touch-action']).not.toBe('none');
  });
});

describe('a card on a touch screen', () => {
  it('opens no link menu and starts no text selection', () => {
    for (const selector of ['.hatch-card', '.hatch-card-face']) {
      const touch = declarationsWithin(css, 'pointer: coarse', selector);
      expect(touch['-webkit-touch-callout']).toBe('none');
      expect(touch['user-select']).toBe('none');
    }
  });

  it('still lets the page stand still under a lifted card', () => {
    expect(declarationsOf('.hatch-lifting')['overscroll-behavior']).toBe('none');
  });
});

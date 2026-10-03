/* The phone-width bar: the battery beside a narrowed attention control,
   both 44px on a coarse pointer, and the attention control's two states
   still measuring the same. Read as text, the way touchCss.test.ts,
   wipCss.test.ts and confirmationsCss.test.ts read App.css - there is no DOM
   in this runner to lay anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** The declarations of every rule whose selector list names `selector` on its
    own - `.a` but not `.a-b`, and not `.a .b` - merged, later winning. A flat
    merge over the whole file, with no awareness of `@media`: the wrong tool
    for a selector with more than one rule naming it (see declarationsWithin),
    but right for everything else here. */
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
    blocks alone - for a selector a later, same-specificity rule elsewhere in
    the file also names, where a flat declarationsOf merge would silently
    report whichever rule is textually last rather than the one a given block
    actually sets. The file has more than one block with this same query, so
    every one of them is searched and merged, later winning - depth-counting
    braces to find each block's matching close; nothing in this file nests an
    `@media` inside another. */
function declarationsWithin(mediaQuery: string, selector: string): Record<string, string> {
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
  if (Object.keys(props).length === 0) throw new Error(`no @media (${mediaQuery}) block in App.css names ${selector}`);
  return props;
}

describe('the attention control at phone width', () => {
  it('narrows to the loudest state\'s own exact floor', () => {
    expect(declarationsWithin('max-width: 40rem', '.hatch-attention-control')['min-width']).toBe('106px');
  });

  it('still measures the same resting as asking - neither state sets its own width', () => {
    expect(declarationsOf('.hatch-attention-rest')).not.toHaveProperty('width');
    expect(declarationsOf('.hatch-attention-rest')).not.toHaveProperty('min-width');
    expect(declarationsOf('.hatch-attention-asking')).not.toHaveProperty('width');
    expect(declarationsOf('.hatch-attention-asking')).not.toHaveProperty('min-width');
  });
});

describe('the battery and the attention control on a coarse pointer', () => {
  it('are both at least 44px tall, at every viewport width', () => {
    expect(declarationsOf('.hatch-battery')['min-height']).toBe('44px');
    expect(declarationsOf('.hatch-attention-control')['min-height']).toBe('44px');
  });
});

/* The status band's column buttons: 44px on a coarse pointer, the base rule
   left untouched on a fine one. Read as text, the way barCss.test.ts reads
   App.css - there is no DOM in this runner to lay anything out in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** The declarations of `selector` as they read inside `@media (${mediaQuery})`
    blocks alone, merged across every block carrying that query, later
    winning - copied from barCss.test.ts. `declarationsOf`-style flat merging
    is the wrong tool here: `.hatch-status-step` and `.hatch-status-steps`
    each have a rule outside any `@media` as well as one inside, so a flat
    merge can't tell which block set which declaration. */
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

/** The declarations of every rule naming `selector`, with every `@media { }`
    block's text removed first (same depth-counting walk as declarationsWithin,
    discarding the block instead of reading it). Needed because §2.4 asks for
    the base rule on its own - what `.hatch-status-step` and
    `.hatch-status-steps` read on a fine pointer - and neither existing helper
    gives that: declarationsOf would merge the coarse-pointer block's
    declarations straight over it, since both rules name the same selector. */
function declarationsOutsideMedia(selector: string): Record<string, string> {
  let plain = '';
  let i = 0;
  while (i < css.length) {
    const at = css.indexOf('@media', i);
    if (at === -1) {
      plain += css.slice(i);
      break;
    }
    plain += css.slice(i, at);
    const openBrace = css.indexOf('{', at);
    let depth = 0;
    let end = openBrace;
    for (; end < css.length; end++) {
      if (css[end] === '{') depth++;
      else if (css[end] === '}') {
        depth--;
        if (depth === 0) break;
      }
    }
    i = end + 1;
  }

  const props: Record<string, string> = {};
  for (const [, selectors, body] of plain.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

describe('the status band on a coarse pointer', () => {
  it('raises every column button to the 44px floor', () => {
    expect(declarationsWithin('pointer: coarse', '.hatch-status-step')['min-height']).toBe('44px');
  });

  it('widens the gap between neighbours', () => {
    expect(declarationsWithin('pointer: coarse', '.hatch-status-steps')['gap']).toBe('var(--sp-3)');
  });
});

describe('the status band on a fine pointer', () => {
  it('leaves the column buttons at the base rule - no min-height', () => {
    expect(declarationsOutsideMedia('.hatch-status-step')).not.toHaveProperty('min-height');
  });

  it('leaves the row at its original gap', () => {
    expect(declarationsOutsideMedia('.hatch-status-steps')['gap']).toBe('var(--sp-2)');
  });
});

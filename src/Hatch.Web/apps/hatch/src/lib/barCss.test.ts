/* The phone-width bar: the battery beside an attention and an overnight
   control that carry no min-width at phone width, all three 44px on a coarse
   pointer, the attention control's two states still measuring the same, and
   its four pills folded into one total.
   Read as text, the way touchCss.test.ts, wipCss.test.ts and
   confirmationsCss.test.ts read App.css - there is no DOM in this runner to
   lay anything out in. */
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
    actually sets. Throws when nothing names the selector, a guard against a
    typo'd one, unless `required` is false - for asserting a selector is
    absent from the query. The file has more than one block with this same
    query, so every one of them is searched and merged, later winning -
    depth-counting braces to find each block's matching close; nothing in this
    file nests an `@media` inside another. */
function declarationsWithin(mediaQuery: string, selector: string, required = true): Record<string, string> {
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
  if (required && Object.keys(props).length === 0) throw new Error(`no @media (${mediaQuery}) block in App.css names ${selector}`);
  return props;
}

/** The declarations of every rule naming `selector`, with every `@media { }`
    block's text removed first (same depth-counting walk as declarationsWithin,
    discarding the block instead of reading it) - copied from
    statusStepCss.test.ts (and topBarCss.test.ts). Needed once the phone query
    sets `.hatch-attention-control` and `.hatch-overnight-control` too:
    declarationsOf would merge the phone block's `min-width: auto` straight
    over the desk's `136px`, since both rules name the same selector and the
    phone block comes later in the file. */
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

describe('the attention and overnight controls at phone width', () => {
  it('reset to min-width: auto inside the phone query', () => {
    expect(declarationsWithin('max-width: 40rem', '.hatch-attention-control')['min-width']).toBe('auto');
    expect(declarationsWithin('max-width: 40rem', '.hatch-overnight-control')['min-width']).toBe('auto');
  });

  it('still set min-width: 136px outside it', () => {
    expect(declarationsOutsideMedia('.hatch-attention-control')['min-width']).toBe('136px');
    expect(declarationsOutsideMedia('.hatch-overnight-control')['min-width']).toBe('136px');
  });

  it('still measures the same resting as asking - neither state sets its own width', () => {
    expect(declarationsOf('.hatch-attention-rest')).not.toHaveProperty('width');
    expect(declarationsOf('.hatch-attention-rest')).not.toHaveProperty('min-width');
    expect(declarationsOf('.hatch-attention-asking')).not.toHaveProperty('width');
    expect(declarationsOf('.hatch-attention-asking')).not.toHaveProperty('min-width');
  });
});

describe('the attention pills at phone width', () => {
  it('fold into one total: the pills are hidden and the total drawn inside the phone query', () => {
    expect(declarationsWithin('max-width: 40rem', '.hatch-attention-pills').display).toBe('none');
    expect(declarationsWithin('max-width: 40rem', '.hatch-attention-pill-total').display).toBe('inline-flex');
  });

  it('draw the pills, and not the total, outside it', () => {
    expect(declarationsOutsideMedia('.hatch-attention-pills').display).toBe('flex');
    expect(declarationsOutsideMedia('.hatch-attention-pill-total').display).toBe('none');
  });

  it('give the total the colours of the review pill', () => {
    const total = declarationsOf('.hatch-attention-pill-total');
    const review = declarationsOf('.hatch-attention-pill-review');
    expect(total.background).toBe(review.background);
    expect(total.color).toBe(review.color);
  });
});

describe('the battery, the attention control and the overnight control on a coarse pointer', () => {
  it('are all at least 44px tall, at every viewport width', () => {
    expect(declarationsOf('.hatch-battery')['min-height']).toBe('44px');
    expect(declarationsOf('.hatch-attention-control')['min-height']).toBe('44px');
    expect(declarationsOf('.hatch-overnight-control')['min-height']).toBe('44px');
  });
});
